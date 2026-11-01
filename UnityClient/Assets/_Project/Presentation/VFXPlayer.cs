// §25.2 OnSpellFx 구독 → VFX 프리팹 재생 (ㅊㄱㅇ: VFX 연결 코드)
//   · 프리팹 이름: SpellDefinition.GetVfxPrefabName(part) = VFX_{SkillId}_{Name}_{Part}
//     (Spellbound > Skills > Build Skill Assets 가 에셋 팩으로 만든다 — 실제 크기로 제작, 코드에서 크기를 바꾸지 않는다)
//   · 방향: 레인 방향으로 정렬, 시전자 쪽에서 상대 쪽을 바라보게 (장판 · 띠 · 빔이 레인을 따라 놓인다, 4인 개인전은 레인마다 다르다)
//   · 수명: Field · Loop = 스킬 지속 시간, Telegraph = 예고 시간, 그 외 = oneShotMaxLifetime
//     수명이 끝나면 파티클 방출만 멈추고 남은 입자가 사라진 뒤 풀로 돌려보낸다 (뚝 끊기지 않게)
//   · H02 레이저 빔(Field · Telegraph)은 길이 1m(+Z)로 제작 → 전장 길이만큼 Z 스케일
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
            public bool Stopping;
            public bool IsLoop;
            public int SpellId;
        }

        public float oneShotMaxLifetime = 4f;
        [Tooltip("방출을 멈춘 뒤 남은 입자가 사라지길 기다리는 시간 (초)")]
        public float fadeOutSeconds = 1.2f;
        public int prewarmPerPrefab = 2;
        public int fallbackPrewarm = 24;
        [Tooltip("피격 연출 최소 간격 (초)")]
        public float hitFxInterval = 0.08f;

        private ClientSession _session;
        private VFXCatalog _catalog;
        private readonly Dictionary<string, ObjectPool> _pools = new Dictionary<string, ObjectPool>();
        private readonly List<TimedFx> _timed = new List<TimedFx>(64);
        private readonly List<ParticleSystem> _psBuffer = new List<ParticleSystem>(16);
        private readonly List<MeshRenderer> _meshBuffer = new List<MeshRenderer>(8);
        private ObjectPool _fallbackPool;
        private Transform _root;
        private float _nextHitFx;

        public void Initialize(ClientSession session, VFXCatalog catalog)
        {
            _session = session;
            _catalog = catalog;
            _root = new GameObject("[VFX Pool]").transform;
            _root.SetParent(transform, false);
            _fallbackPool = new ObjectPool(CreateFallbackFx, _root, fallbackPrewarm, "FallbackFx");
            _session.Events.OnSpellFxLane += HandleSpellFx;
            _session.Events.OnHitFeedback += HandleHit;
            _session.Events.OnMatchEnded += HandleMatchEnded;
        }

        private void OnDestroy()
        {
            if (_session == null) return;
            _session.Events.OnSpellFxLane -= HandleSpellFx;
            _session.Events.OnHitFeedback -= HandleHit;
            _session.Events.OnMatchEnded -= HandleMatchEnded;
        }

        private static GameObject CreateFallbackFx()
        {
            var go = FallbackVisuals.Primitive(PrimitiveType.Sphere, "FallbackFx", FallbackVisuals.Transparent(Color.white));
            go.AddComponent<FallbackFxAnimator>();
            return go;
        }

        /// <summary>
        /// 이름으로 VFX 한 번 재생 (표시 전용 — ArenaStateView의 유닛 공격 · 등장 연출도 이 경로를 쓴다).
        /// 프리팹이 없으면 false.
        /// </summary>
        public bool PlayNamed(string prefabName, Vector3 position, Quaternion rotation, float lifetime, Vector3? scale = null)
        {
            if (_catalog == null || !_catalog.TryGet(prefabName, out var prefab)) return false;
            var inst = GetPool(prefab).Get(position, rotation);
            inst.transform.localScale = scale ?? prefab.transform.localScale;
            RestartParticles(inst);
            _timed.Add(new TimedFx { Instance = inst, ReleaseTime = Time.time + Mathf.Max(0.1f, lifetime), IsLoop = false });
            return true;
        }

        /// <summary>피격 연출 (이펙트 팩 VFX_S00_hit) — 한꺼번에 맞아도 터지지 않게 간격을 둔다</summary>
        private void HandleHit(int targetId, Vector3 position, int amount, Network.HitResultKind kind)
        {
            if (kind != Network.HitResultKind.Damage || amount <= 0 || Time.time < _nextHitFx) return;
            _nextHitFx = Time.time + hitFxInterval;
            PlayNamed("VFX_S00_hit", position, Quaternion.identity, 0.6f);
        }

        /// <summary>레인 방향으로 눕힌 회전 — 1:1 은 위치가 어느 쪽 절반인지, 개인전은 가까운 발판에서 먼 쪽으로</summary>
        private Quaternion FacingFor(VfxPart part, Vector3 position, int lane)
        {
            var arena = _session.Arena;
            Vector2 uv = arena.ToArena(position);
            if (arena.IsDuel)
            {
                Vector3 facing = arena.Forward * (uv.y <= arena.FieldLength * 0.5f ? 1f : -1f);
                if (part != VfxPart.Cast && part != VfxPart.Projectile) facing = arena.Forward;
                return Quaternion.LookRotation(facing, Vector3.up);
            }
            if (part == VfxPart.Cast)
            {
                // 시전 연출의 column 은 시전자가 선 칸이라 레인을 모른다 — 가까운 발판이 바라보는 쪽
                Team nearest = Team.Home;
                float best = float.MaxValue;
                for (int t = 0; t < arena.TeamCount; t++)
                {
                    float d = (arena.BasePosition((Team)t) - uv).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        nearest = (Team)t;
                    }
                }
                return arena.FacingRotation(nearest);
            }
            var frame = arena.GetLane(Team.Home, lane);
            Vector2 dir = frame.Along(uv) <= frame.Length * 0.5f ? frame.Dir : -frame.Dir;
            return Quaternion.LookRotation(arena.ToWorldDirection(dir), Vector3.up);
        }

        private void HandleSpellFx(int spellId, VfxPart part, Vector3 position, int lane)
        {
            var def = _session.Catalog.Get(spellId);
            Color color = def != null ? def.ThemeColor : Color.white;
            color.a = 0.8f;
            var arena = _session.Arena;
            Quaternion rotation = FacingFor(part, position, lane);

            if (def != null && _catalog != null && _catalog.TryGet(def.GetVfxPrefabName(part), out var prefab))
            {
                var inst = GetPool(prefab).Get(position, rotation);
                Vector3 scale = prefab.transform.localScale;
                if (spellId == SpellIds.H02_Laser && (part == VfxPart.Field || part == VfxPart.Telegraph))
                    scale = new Vector3(scale.x, scale.y, scale.z * arena.GetLane(Team.Home, lane).Length);
                inst.transform.localScale = scale;
                RestartParticles(inst);
                bool loop = part == VfxPart.Loop || part == VfxPart.Field;
                float life = loop ? Lifetime(def, part) : Mathf.Min(Lifetime(def, part), PlayLength(inst));
                _timed.Add(new TimedFx { Instance = inst, ReleaseTime = Time.time + life, IsLoop = loop, SpellId = spellId });
                return;
            }

            float radius = def != null && (part == VfxPart.Impact || part == VfxPart.Field) && def.Executor == SpellExecutorType.Area
                ? def.Radius
                : 0f;
            PlayFallback(def, part, position, color, radius);
        }

        /// <summary>이펙트 팩의 일회성 연출은 Looping 인 것이 많다 — 한 바퀴만 돌고 멈추도록 파티클 길이까지만</summary>
        private float PlayLength(GameObject inst)
        {
            inst.GetComponentsInChildren(true, _psBuffer);
            float length = 0f;
            for (int i = 0; i < _psBuffer.Count; i++)
                if (_psBuffer[i].main.loop) length = Mathf.Max(length, _psBuffer[i].main.duration);
            return length > 0f ? length : oneShotMaxLifetime;
        }

        private float Lifetime(SpellDefinition def, VfxPart part)
        {
            switch (part)
            {
                case VfxPart.Field:
                    return def.EffectDuration > 0f ? def.EffectDuration : oneShotMaxLifetime;
                case VfxPart.Loop:
                    return def.EffectDuration > 0f ? Mathf.Min(def.EffectDuration, 2.5f) : 2f;
                case VfxPart.Telegraph:
                    return def.TelegraphDelay > 0f ? def.TelegraphDelay + 0.15f : 1.2f;
                default:
                    return oneShotMaxLifetime;
            }
        }

        private void RestartParticles(GameObject inst)
        {
            inst.GetComponentsInChildren(true, _meshBuffer);
            for (int i = 0; i < _meshBuffer.Count; i++) _meshBuffer[i].enabled = true; // 지난 재생에서 끈 빔 · 판 메시
            inst.GetComponentsInChildren(true, _psBuffer);
            for (int i = 0; i < _psBuffer.Count; i++)
            {
                var ps = _psBuffer[i];
                if (ps.transform.parent != null && ps.transform.parent.GetComponentInParent<ParticleSystem>() != null) continue;
                ps.Clear(true);
                ps.Play(true);
            }
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
                // 한 번에 여러 개 나오는 것: H01 프리즈 장판 15칸 · 화살 착탄 10발 · 총난사 15발
                int prewarm = prefab.name.StartsWith("VFX_H01_Freeze_Field") ? 16
                            : prefab.name.Contains("ArrowRain_Impact") || prefab.name.Contains("Gatling_Impact") ? 12
                            : prefab.name.Contains("Tornado_Field") || prefab.name.Contains("Rage_Field") ? 4
                            : prewarmPerPrefab;
                pool = new ObjectPool(prefab, _root, prewarm);
                _pools.Add(prefab.name, pool);
            }
            return pool;
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
                if (now < t.ReleaseTime) continue;

                if (!t.Stopping && StopEmitting(t.Instance))
                {
                    // 방출만 멈추고 남은 입자가 사라질 시간을 준다
                    t.Stopping = true;
                    t.ReleaseTime = now + fadeOutSeconds;
                    _timed[i] = t;
                    continue;
                }
                t.Instance.SetActive(false);
                _timed.RemoveAt(i);
            }
        }

        private bool StopEmitting(GameObject inst)
        {
            inst.GetComponentsInChildren(false, _psBuffer);
            if (_psBuffer.Count == 0) return false;
            bool anyAlive = false;
            for (int i = 0; i < _psBuffer.Count; i++)
            {
                _psBuffer[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
                if (_psBuffer[i].particleCount > 0) anyAlive = true;
            }
            // 메시 렌더러(빔 · 쉴드 판)는 바로 끈다
            inst.GetComponentsInChildren(false, _meshBuffer);
            for (int i = 0; i < _meshBuffer.Count; i++) _meshBuffer[i].enabled = false;
            return anyAlive;
        }
    }
}
