// §25.2 OnSpellFx 구독 → VFX 프리팹 재생 (ㅊㄱㅇ: VFX 연결 코드)
//   · 프리팹 이름: SpellDefinition.GetVfxPrefabName(part) = VFX_{SkillId}_{Name}_{Part}
//   · 범위형(Impact · Field)은 반경 1m 기준 제작 → 코드에서 스킬 반경 배율 적용 (§25.3)
//   · 일회성은 Stop Action(Disable)로 스스로 꺼져 풀로 돌아온다. 안전장치로 최대 수명 후 강제 반환.
//   · Loop는 연결된 상태 효과가 끝나면(OnStatusEffectChanged active=false) 반환한다.
//   · 프리팹이 없으면 대체 연출(FallbackFxAnimator)을 쓴다.

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Pooling;
using SpellboundVR.Spells;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class VFXPlayer : MonoBehaviour
    {
        private struct TimedFx
        {
            public GameObject Instance;
            public float ReleaseTime;
            public bool IsLoop;
            public int SpellId;
        }

        public float oneShotMaxLifetime = 4f;
        public int prewarmPerPrefab = 4;
        public int fallbackPrewarm = 24;

        private ClientSession _session;
        private VFXCatalog _catalog;
        private readonly Dictionary<string, ObjectPool> _pools = new Dictionary<string, ObjectPool>();
        private readonly List<TimedFx> _timed = new List<TimedFx>(64);
        private ObjectPool _fallbackPool;
        private Transform _root;

        public void Initialize(ClientSession session, VFXCatalog catalog)
        {
            _session = session;
            _catalog = catalog;
            _root = new GameObject("[VFX Pool]").transform;
            _root.SetParent(transform, false);
            _fallbackPool = new ObjectPool(CreateFallbackFx, _root, fallbackPrewarm, "FallbackFx");
            _session.Events.OnSpellFx += HandleSpellFx;
            _session.Events.OnStatusEffectChanged += HandleStatusEffect;
            _session.Events.OnMatchEnded += HandleMatchEnded;
        }

        private void OnDestroy()
        {
            if (_session == null) return;
            _session.Events.OnSpellFx -= HandleSpellFx;
            _session.Events.OnStatusEffectChanged -= HandleStatusEffect;
            _session.Events.OnMatchEnded -= HandleMatchEnded;
        }

        private static GameObject CreateFallbackFx()
        {
            var go = FallbackVisuals.Primitive(PrimitiveType.Sphere, "FallbackFx", FallbackVisuals.Transparent(Color.white));
            go.AddComponent<FallbackFxAnimator>();
            return go;
        }

        private void HandleSpellFx(int spellId, VfxPart part, Vector3 position, Column column)
        {
            var def = _session.Catalog.Get(spellId);
            Color color = def != null ? def.ThemeColor : Color.white;
            color.a = 0.8f;
            float radius = def != null && (part == VfxPart.Impact || part == VfxPart.Field) && def.Executor == SpellExecutorType.Area
                ? def.Radius
                : 0f;

            if (def != null && _catalog != null && _catalog.TryGet(def.GetVfxPrefabName(part), out var prefab))
            {
                var pool = GetPool(prefab);
                var inst = pool.Get(position, Quaternion.identity);
                if (radius > 0f) inst.transform.localScale = Vector3.one * radius;
                else inst.transform.localScale = prefab.transform.localScale;
                bool loop = part == VfxPart.Loop || part == VfxPart.Field;
                float life = loop && def.EffectDuration > 0f ? def.EffectDuration : oneShotMaxLifetime;
                _timed.Add(new TimedFx { Instance = inst, ReleaseTime = Time.time + life, IsLoop = loop, SpellId = spellId });
                return;
            }

            PlayFallback(def, part, position, color, radius);
        }

        private void PlayFallback(SpellDefinition def, VfxPart part, Vector3 position, Color color, float radius)
        {
            var go = _fallbackPool.Get(position, Quaternion.identity);
            var anim = go.GetComponent<FallbackFxAnimator>();
            switch (part)
            {
                case VfxPart.Cast:
                    anim.Play(color, position, position, 0.2f, 0.7f, 0.3f, false);
                    break;
                case VfxPart.Projectile:
                    // 범위 공격: 하늘에서 떨어지는 구체 (메테오)
                    anim.Play(color, position + Vector3.up * 7f, position, 0.6f, 0.9f, 0.25f, false);
                    break;
                case VfxPart.Impact:
                    float r = radius > 0f ? radius : 0.35f;
                    anim.Play(color, position, position, r * 0.5f, r * 2f, 0.45f, false);
                    break;
                case VfxPart.Loop:
                case VfxPart.Field:
                    float life = def != null && def.EffectDuration > 0f ? def.EffectDuration : 3f;
                    var c = color;
                    c.a = 0.35f;
                    anim.Play(c, position, position, 1.6f, 1.8f, life, true);
                    _timed.Add(new TimedFx { Instance = go, ReleaseTime = Time.time + life, IsLoop = true, SpellId = def != null ? def.SpellId : 0 });
                    break;
                default:
                    anim.Play(color, position, position, 0.3f, 0.6f, 0.4f, false);
                    break;
            }
        }

        private ObjectPool GetPool(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab.name, out var pool))
            {
                pool = new ObjectPool(prefab, _root, prewarmPerPrefab);
                _pools.Add(prefab.name, pool);
            }
            return pool;
        }

        private void HandleStatusEffect(int targetId, string effectId, bool active, float remaining)
        {
            if (active || !TeamUtil.IsPlayerId(targetId)) return;
            var snap = _session.Events.Latest;
            if (snap == null) return;
            Team team = TeamUtil.TeamOfPlayerId(targetId);
            var arena = _session.Arena;
            Vector3 playerPos = arena.ToWorld(arena.PlayerPosition(team, snap.Players[(int)team].WorldColumn), 1.2f);

            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                var t = _timed[i];
                if (!t.IsLoop || t.Instance == null) continue;
                Vector3 p = t.Instance.transform.position;
                p.y = playerPos.y;
                if ((p - playerPos).sqrMagnitude <= 4f)
                {
                    t.Instance.SetActive(false);
                    _timed.RemoveAt(i);
                }
            }
        }

        private void HandleMatchEnded(MatchResult result, MatchEndReason reason, float duration)
        {
            for (int i = 0; i < _timed.Count; i++)
                if (_timed[i].Instance != null) _timed[i].Instance.SetActive(false);
            _timed.Clear();
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                var t = _timed[i];
                if (t.Instance == null || !t.Instance.activeSelf)
                {
                    _timed.RemoveAt(i);
                    continue;
                }
                if (now >= t.ReleaseTime)
                {
                    t.Instance.SetActive(false);
                    _timed.RemoveAt(i);
                }
            }
        }
    }
}
