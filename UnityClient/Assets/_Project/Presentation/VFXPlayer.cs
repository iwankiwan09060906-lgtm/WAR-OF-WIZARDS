// §25.2 OnSpellFx 구독 → VFX 프리팹 재생 (ㅊㄱㅇ: VFX 연결 코드)
//   · 프리팹 이름: SpellDefinition.GetVfxPrefabName(part) = VFX_{SkillId}_{Name}_{Part}
//     (Spellbound > Skills > Build Skill Assets 가 에셋 팩으로 만든다 — 실제 크기로 제작, 코드에서 크기를 바꾸지 않는다)
//   · 방향: 레인 방향으로 정렬, 시전자 쪽에서 상대 쪽을 바라보게 (장판 · 띠 · 빔이 레인을 따라 놓인다, 4인 개인전은 레인마다 다르다)
//   · 수명: Field · Loop = 스킬 지속 시간, Telegraph = 예고 시간, 그 외 = oneShotMaxLifetime
//     수명이 끝나면 파티클 방출만 멈추고 남은 입자가 사라진 뒤 풀로 돌려보낸다 (뚝 끊기지 않게)
//   · H02 레이저 빔(Field · Telegraph)은 길이 1m(+Z)로 제작 → 전장 길이만큼 Z 스케일
//   · 프리팹이 없으면 대체 연출(FallbackFxAnimator)을 쓴다.
//   · 스킬별 연출 보강 (표시 전용):
//     S01 연막 착탄 ×5 · S02 독 장판은 판정 범위(FieldHalfDepth)만큼 길고 높게 · S03 파이어볼 폭발 ×2
//     S06 번개 ×2 크기 · 낙뢰 하나마다 둘레에 2줄기 더 (서버는 그대로, 화면에서만 3배) · 먹구름이 사방에서 날아와 자리 잡는다
//     H01 프리즈 = 시전자 자리에서 푸른 얼음이 전장(레인 · 발판 자리) 위로만 퍼진다 · H02 레이저 = 진한 심 + 지금 빔을 겉에 덮고, 빔을 따라 S06 번개가 친다
//     H03 흡혈 = 주변이 어두워지고 대상마다 가는 선이 이어졌다가 1초 뒤 사라진다
//   · EzTornado 처럼 월드 공간 · 큰 단위로 만든 파티클은 크기를 바꿀 수 있게 고친다 (MakeScalable).

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Pooling;
using SpellboundVR.Spells;
using SpellboundVR.Spells.Behaviors;
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

        [Header("스킬 연출 보강")]
        [Tooltip("S06 먹구름이 맵 밖에서 자리까지 날아오는 시간 (초) — 번개 예고 시간(TelegraphDelay)보다 짧게")]
        public float cloudFlightSeconds = 0.8f;
        [Tooltip("H03 흡혈 선이 남아 있는 시간 (초)")]
        public float drainLineSeconds = 1f;
        [Tooltip("H03 흡혈 때 주변 밝기 (1 = 그대로)")]
        [Range(0f, 1f)] public float drainDarkness = 0.3f;
        public Color freezeCoverColor = new Color(0.3f, 0.6f, 1f, 0.6f);
        [Tooltip("H01 얼음 덮개가 퍼지는 앞쪽 경계의 번짐 폭 (m)")]
        public float freezeEdgeSoftness = 4f;
        [Tooltip("S06 낙뢰 하나마다 둘레에 더 치는 번개 수 (표시 전용)")]
        public int extraBoltsPerStrike = 2;

        /// <summary>S03 착탄 때 사방으로 튀는 총난사 불꽃 (표시 전용, 피해 없음)</summary>
        private struct Spark
        {
            public GameObject Go;
            public Vector3 Velocity;
            public float End;
        }

        [Tooltip("H02 레이저가 켜져 있는 동안 빔을 따라 번개(S06)가 치는 간격 (초, 표시 전용)")]
        public float laserBoltInterval = 0.2f;

        /// <summary>H02 빔을 따라 치는 번개 (표시 전용, 피해 없음)</summary>
        private struct BeamBolts
        {
            public Vector3 Center;
            public Vector3 Dir;
            public float HalfLength;
            public float Next;
            public float End;
        }

        private readonly List<BeamBolts> _beamBolts = new List<BeamBolts>(4);

        [Tooltip("S03 파이어볼이 땅에 닿을 때 사방으로 튀는 총난사 불꽃 수")]
        public int fireballSparks = 10;
        private readonly List<Spark> _sparks = new List<Spark>(16);

        /// <summary>날아오는 먹구름 (S06)</summary>
        private struct FlyingCloud
        {
            public Transform Cloud;
            public Vector3 From;
            public Vector3 To;
            public float Start;
        }

        /// <summary>흡혈 선 (H03)</summary>
        private struct DrainLine
        {
            public LineRenderer Line;
            public float End;
        }

        private readonly List<FlyingCloud> _clouds = new List<FlyingCloud>(8);
        private readonly List<DrainLine> _drainLines = new List<DrainLine>(16);
        private readonly List<LineRenderer> _linePool = new List<LineRenderer>(16);
        private Vector3 _drainOrigin;
        private float _darkUntil = -1f;
        private float _darkness = 1f;
        private Light _sun;
        private float _sunIntensity = -1f;
        private Color _ambient;
        /// <summary>H01 얼음 덮개 — 전장 모양대로 깐 판 (Spellbound/FreezeCover 셰이더가 반경만큼 부드럽게 드러낸다)</summary>
        private Transform _freezeRoot;
        private Material _freezeMaterial;
        private float _freezeStart = -1f;
        private float _freezeEnd;

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
            return PlayNamed(prefab, position, rotation, lifetime, scale ?? prefab.transform.localScale);
        }

        /// <summary>프리팹 크기 × multiplier 로 한 번 재생</summary>
        public bool PlayScaled(string prefabName, Vector3 position, Quaternion rotation, float lifetime, float multiplier)
        {
            if (_catalog == null || !_catalog.TryGet(prefabName, out var prefab)) return false;
            return PlayNamed(prefab, position, rotation, lifetime, prefab.transform.localScale * multiplier);
        }

        private bool PlayNamed(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, Vector3 scale)
        {
            var inst = GetPool(prefab).Get(position, rotation);
            inst.transform.localScale = scale;
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

        /// <summary>스킬 · 부분별 크기 배율 (프리팹은 실제 크기로 만들었다 — 기획에서 키운 것만)</summary>
        private static float PartScale(int spellId, VfxPart part)
        {
            switch (spellId)
            {
                case SpellIds.S01_Smoke: return part == VfxPart.Impact ? 5f : 1f;
                case SpellIds.S03_Fireball: return part == VfxPart.Impact ? 2f : 1f;
                case SpellIds.S06_Lightning: return part == VfxPart.Impact ? 2f : 1f; // 먹구름(예고)은 FlyCloudIn 이 키운다
                default: return 1f;
            }
        }

        private void HandleSpellFx(int spellId, VfxPart part, Vector3 position, int lane)
        {
            var def = _session.Catalog.Get(spellId);
            if (def != null) PlayExtras(def, part, position, lane);
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
                // 띠 장판은 3열 폭으로 제작 → 개인전은 레인 폭으로 줄인다
                if ((def.Behavior == SpellBehaviorType.PoisonField || def.Behavior == SpellBehaviorType.RageField) && (part == VfxPart.Field || part == VfxPart.Impact))
                    scale.x *= arena.BandHalfWidth / (arena.ColumnSpacing * 1.5f);
                // 독 장판: 독병 크기는 그대로, 장판은 판정 범위만큼 길고 높게 (프리팹은 반폭 2.5m 기준)
                if (def.Behavior == SpellBehaviorType.PoisonField && (part == VfxPart.Field || part == VfxPart.Impact))
                {
                    scale.z *= Mathf.Max(1f, def.FieldHalfDepth / 2.5f);
                    scale.y *= 2f;
                }
                scale *= PartScale(spellId, part);
                inst.transform.localScale = scale;
                RestartParticles(inst);
                if (spellId == SpellIds.S06_Lightning && part == VfxPart.Telegraph) FlyCloudIn(inst, prefab, position);
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
                    // 맵 밖에서 날아오는 스킬은 도착할 때까지 예고 표시를 남긴다
                    float warning = Mathf.Max(def.TelegraphDelay, def.FromOffMap ? def.OffMapFlightSeconds : 0f);
                    // 번개: 먹구름이 낙뢰가 끝날 때까지 떠 있다
                    if (def.Behavior == SpellBehaviorType.LightningStrikes) warning += def.ShotCount * def.ShotInterval;
                    return warning > 0f ? warning + 0.15f : 1.2f;
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
                            : prefab.name.Contains("Gatling_Impact") ? 24
                            : prefab.name.Contains("Lightning_Impact") ? 36
                            : prefab.name.Contains("ArrowRain_Impact") ? 12
                            : prefab.name.Contains("Tornado_Field") || prefab.name.Contains("Rage_Field") ? 4
                            : prewarmPerPrefab;
                var source = prefab;
                pool = new ObjectPool(() =>
                {
                    var go = Instantiate(source);
                    go.name = source.name;
                    MakeScalable(go);
                    return go;
                }, _root, prewarm, prefab.name);
                _pools.Add(prefab.name, pool);
            }
            return pool;
        }

        private void HandleMatchEnded(MatchResult result, MatchEndReason reason, float duration)
        {
            for (int i = 0; i < _timed.Count; i++)
                if (_timed[i].Instance != null) _timed[i].Instance.SetActive(false);
            _timed.Clear();
            _clouds.Clear();
            _beamBolts.Clear();
            for (int i = 0; i < _sparks.Count; i++) if (_sparks[i].Go != null) _sparks[i].Go.SetActive(false);
            _sparks.Clear();
            for (int i = 0; i < _drainLines.Count; i++) _drainLines[i].Line.gameObject.SetActive(false);
            _drainLines.Clear();
            _darkUntil = -1f;
            _freezeStart = -1f;
            if (_freezeRoot != null) _freezeRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// 월드 공간 · 자기 스케일만 쓰는 파티클(EzTornado 등)은 위치를 옮기거나 크기를 바꾸면 따라오지 않는다 —
        /// 로컬 공간 · 계층 스케일로 바꿔 다른 VFX 처럼 Transform 하나로 옮기고 키운다. 소리는 게임 효과음(SoundManager)이 낸다.
        /// </summary>
        public static void MakeScalable(GameObject go)
        {
            if (!go.name.Contains("Tornado") || go.name.StartsWith("VFX_")) return; // 게임용으로 만든 VFX_* 는 그대로
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            foreach (var audio in go.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
        }

        // ── 스킬 연출 보강 ───────────────────────────────────

        private void PlayExtras(SpellDefinition def, VfxPart part, Vector3 position, int lane)
        {
            switch (def.SpellId)
            {
                case SpellIds.H01_Freeze when part == VfxPart.Cast:
                    StartFreezeCover(def, position);
                    break;
                case SpellIds.H02_Laser when part == VfxPart.Field:
                    LaserCore(def, position, lane);
                    _beamBolts.Add(new BeamBolts
                    {
                        Center = position, Dir = FacingFor(VfxPart.Field, position, lane) * Vector3.forward,
                        HalfLength = _session.Arena.GetLane(Team.Home, lane).Length * 0.5f, Next = Time.time, End = Time.time + def.EffectDuration,
                    });
                    break;
                case SpellIds.H03_Drain when part == VfxPart.Telegraph:
                    _drainOrigin = position;
                    _darkUntil = Time.time + drainLineSeconds + 0.2f; // 주변이 갑자기 어두워진다
                    break;
                case SpellIds.H03_Drain when part == VfxPart.Impact:
                    DrainLineTo(position);
                    break;
                case SpellIds.S06_Lightning when part == VfxPart.Impact:
                    ExtraBolts(position, lane);
                    break;
                case SpellIds.S03_Fireball when part == VfxPart.Impact:
                    SplashSparks(position);
                    break;
            }
        }

        /// <summary>S06 먹구름: 맵 밖 사방(번개 자리에서 바깥 방향)에서 날아와 자리 잡는다 — 크기 2배, 더 높이</summary>
        private void FlyCloudIn(GameObject inst, GameObject prefab, Vector3 position)
        {
            var cloud = inst.transform.Find("StormCloud");
            var original = prefab.transform.Find("StormCloud");
            if (cloud == null || original == null) return;
            cloud.localScale = original.localScale * 2f; // 풀에서 재사용 — 프리팹 값 기준으로
            Vector3 to = original.localPosition;
            to.y *= 2f;
            Vector3 center = _session.Arena.ToWorld(_session.Arena.Center);
            Vector3 outward = position - center;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1f) outward = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            outward = Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * outward.normalized;
            Vector3 from = to + inst.transform.InverseTransformDirection(outward) * 40f + Vector3.up * 6f;
            cloud.localPosition = from;
            _clouds.Add(new FlyingCloud { Cloud = cloud, From = from, To = to, Start = Time.time });
        }

        /// <summary>S03: 땅에 닿은 자리에서 총난사 불꽃이 사방으로 포물선을 그리며 튄다 (총난사 탄과 같은 2배 크기)</summary>
        private void SplashSparks(Vector3 position)
        {
            if (_catalog == null || !_catalog.TryGet("VFX_S07_Gatling_Projectile", out var prefab)) return;
            float ground = _session.Arena.GroundY;
            for (int i = 0; i < fireballSparks; i++)
            {
                float angle = (i + Random.Range(-0.3f, 0.3f)) / fireballSparks * Mathf.PI * 2f;
                Vector3 v = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Random.Range(6f, 10f) + Vector3.up * Random.Range(4f, 7f);
                var go = GetPool(prefab).Get(new Vector3(position.x, ground + 0.5f, position.z), Quaternion.LookRotation(v));
                go.transform.localScale = prefab.transform.localScale * 2f;
                RestartParticles(go);
                var trail = go.GetComponentInChildren<TrailRenderer>();
                if (trail != null) trail.Clear();
                _sparks.Add(new Spark { Go = go, Velocity = v, End = Time.time + Random.Range(0.7f, 1f) });
            }
        }

        /// <summary>S06: 낙뢰 둘레(레인 폭 안)에 번개를 더 친다</summary>
        private void ExtraBolts(Vector3 position, int lane)
        {
            var facing = FacingFor(VfxPart.Impact, position, lane);
            float half = _session.Arena.LaneHalfWidth * 0.7f;
            for (int i = 0; i < extraBoltsPerStrike; i++)
            {
                Vector3 offset = facing * new Vector3(Random.Range(-half, half), 0f, Random.Range(-2.5f, 2.5f));
                PlayScaled("VFX_S06_Lightning_Impact", position + offset, facing, 1f, PartScale(SpellIds.S06_Lightning, VfxPart.Impact));
            }
        }

        /// <summary>H02: 빔 가운데 진한 심 (겉은 기존 빔 프리팹이 덮는다)</summary>
        private void LaserCore(SpellDefinition def, Vector3 position, int lane)
        {
            float length = _session.Arena.GetLane(Team.Home, lane).Length;
            var rotation = FacingFor(VfxPart.Field, position, lane) * Quaternion.Euler(90f, 0f, 0f); // 원기둥 축(Y)을 빔 방향으로
            Beam(position, rotation, 0.32f, length, new Color(0.92f, 0.99f, 1f, 1f), def.EffectDuration);
            Beam(position, rotation, 0.6f, length, new Color(0.45f, 0.9f, 1f, 0.55f), def.EffectDuration);
        }

        private void Beam(Vector3 position, Quaternion rotation, float diameter, float length, Color color, float life)
        {
            var go = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "LaserCore", FallbackVisuals.Transparent(color));
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            Destroy(go, Mathf.Max(0.1f, life));
        }

        /// <summary>H03: 시전자에서 대상까지 가는 흡혈 선</summary>
        private void DrainLineTo(Vector3 target)
        {
            LineRenderer line = null;
            for (int i = 0; i < _linePool.Count; i++)
                if (!_linePool[i].gameObject.activeSelf) { line = _linePool[i]; break; }
            if (line == null)
            {
                var go = new GameObject("DrainLine");
                go.transform.SetParent(_root, false);
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.numCapVertices = 2;
                line.sharedMaterial = FallbackVisuals.Transparent(Color.white);
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                _linePool.Add(line);
            }
            line.gameObject.SetActive(true);
            line.widthMultiplier = 0.06f;
            line.startColor = new Color(1f, 0.15f, 0.25f, 0.95f);
            line.endColor = new Color(0.6f, 0f, 0.1f, 0.95f);
            line.SetPosition(0, _drainOrigin);
            line.SetPosition(1, target);
            _drainLines.Add(new DrainLine { Line = line, End = Time.time + drainLineSeconds });
        }

        /// <summary>H01: 시전자 자리에서 얼음 물결과 같은 속도로 전장(레인 · 발판 자리) 위로만 퍼진다 — 낭떠러지 쪽은 덮지 않는다</summary>
        private void StartFreezeCover(SpellDefinition def, Vector3 position)
        {
            if (_freezeRoot == null) BuildFreezeCover();
            _freezeStart = Time.time;
            float spread = _session.Arena.FieldLength * 1.5f / FreezeWave.Speed;
            _freezeEnd = Time.time + spread + def.EffectDuration;
            _freezeMaterial.SetVector("_Origin", position);
            _freezeMaterial.SetFloat("_Radius", 0f);
            _freezeRoot.gameObject.SetActive(true);
        }

        /// <summary>레인마다 판 하나 (1:1 은 3열이 이어져 전장 한 판), 개인전은 발판 자리도. 셰이더가 없으면 반경 없이 바로 덮는다</summary>
        private void BuildFreezeCover()
        {
            var arena = _session.Arena;
            var shader = Shader.Find("Spellbound/FreezeCover");
            _freezeMaterial = shader != null ? new Material(shader) : new Material(FallbackVisuals.Transparent(Color.white));
            _freezeMaterial.name = "FreezeCover";
            _freezeMaterial.SetFloat("_Softness", freezeEdgeSoftness);
            _freezeRoot = new GameObject("FreezeCover").transform;
            _freezeRoot.SetParent(_root, false);
            float width = arena.IsDuel ? arena.ColumnSpacing : arena.LaneHalfWidth * 2f + 1f;
            for (int lane = 0; lane < arena.LaneCount; lane++)
            {
                var frame = arena.GetLane(Team.Home, lane);
                FreezeSheet(frame.Point(frame.Length * 0.5f), Mathf.Atan2(frame.Dir.x, frame.Dir.y) * Mathf.Rad2Deg, width, frame.Length);
            }
            if (arena.IsDuel) return;
            for (int t = 0; t < arena.TeamCount; t++)
            {
                var team = (Team)t;
                Vector2 fwd = arena.SeatForward(team);
                FreezeSheet(arena.BasePosition(team) + fwd * (arena.NexusDepth * 0.5f), Mathf.Atan2(fwd.x, fwd.y) * Mathf.Rad2Deg,
                            arena.ColumnSpacing * 3f + 3f, arena.NexusDepth + arena.ColumnSpacing + 4f);
            }
        }

        private void FreezeSheet(Vector2 at, float yaw, float width, float length)
        {
            var arena = _session.Arena;
            var sheet = FallbackVisuals.Primitive(PrimitiveType.Quad, "Ice", _freezeMaterial).transform;
            sheet.SetParent(_freezeRoot, false);
            sheet.SetPositionAndRotation(arena.ToWorld(at, 0.07f), Quaternion.Euler(90f, yaw + arena.FacingRotation(Team.Home).eulerAngles.y, 0f));
            sheet.localScale = new Vector3(width, length, 1f);
        }

        private void UpdateExtras(float now)
        {
            for (int i = _clouds.Count - 1; i >= 0; i--)
            {
                var c = _clouds[i];
                if (c.Cloud == null || !c.Cloud.gameObject.activeInHierarchy) { _clouds.RemoveAt(i); continue; }
                float t = Mathf.Clamp01((now - c.Start) / Mathf.Max(0.05f, cloudFlightSeconds));
                c.Cloud.localPosition = Vector3.Lerp(c.From, c.To, 1f - (1f - t) * (1f - t));
                if (t >= 1f) _clouds.RemoveAt(i);
            }

            for (int i = _drainLines.Count - 1; i >= 0; i--)
            {
                var d = _drainLines[i];
                float left = d.End - now;
                if (left <= 0f)
                {
                    d.Line.gameObject.SetActive(false);
                    _drainLines.RemoveAt(i);
                    continue;
                }
                d.Line.widthMultiplier = 0.06f * Mathf.Clamp01(left / 0.25f); // 끝날 때 가늘어지며 사라진다
            }

            UpdateDarkness(now);

            for (int i = _beamBolts.Count - 1; i >= 0; i--)
            {
                var b = _beamBolts[i];
                if (now >= b.End) { _beamBolts.RemoveAt(i); continue; }
                while (b.Next <= now)
                {
                    b.Next += Mathf.Max(0.05f, laserBoltInterval);
                    Vector3 at = b.Center + b.Dir * Random.Range(-b.HalfLength, b.HalfLength);
                    at.y = _session.Arena.GroundY;
                    PlayScaled("VFX_S06_Lightning_Impact", at, Quaternion.LookRotation(b.Dir), 1f, PartScale(SpellIds.S06_Lightning, VfxPart.Impact));
                }
                _beamBolts[i] = b;
            }

            float dt = Time.deltaTime;
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var sp = _sparks[i];
                if (sp.Go == null || now >= sp.End || sp.Go.transform.position.y < _session.Arena.GroundY)
                {
                    if (sp.Go != null) sp.Go.SetActive(false);
                    _sparks.RemoveAt(i);
                    continue;
                }
                sp.Velocity += Vector3.down * 9.8f * dt;
                sp.Go.transform.SetPositionAndRotation(sp.Go.transform.position + sp.Velocity * dt, Quaternion.LookRotation(sp.Velocity));
                _sparks[i] = sp;
            }

            if (_freezeStart >= 0f && _freezeRoot != null)
            {
                // 경계가 물결보다 번짐 폭만큼 앞서 가야 얼어붙는 순간 이미 파랗다
                _freezeMaterial.SetFloat("_Radius", (now - _freezeStart) * FreezeWave.Speed + freezeEdgeSoftness);
                var col = freezeCoverColor;
                col.a *= Mathf.Clamp01(_freezeEnd - now); // 마지막 1초에 녹는다
                _freezeMaterial.color = col;
                if (now >= _freezeEnd)
                {
                    _freezeStart = -1f;
                    _freezeRoot.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>H03: 해 · 주변광을 잠깐 낮췄다가 되돌린다</summary>
        private void UpdateDarkness(float now)
        {
            float target = now < _darkUntil ? drainDarkness : 1f;
            if (Mathf.Approximately(_darkness, target) && Mathf.Approximately(target, 1f)) return;
            if (_sunIntensity < 0f)
            {
                _sun = RenderSettings.sun;
                if (_sun == null)
                    foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.type == LightType.Directional) { _sun = l; break; }
                _sunIntensity = _sun != null ? _sun.intensity : 1f;
                _ambient = RenderSettings.ambientLight;
            }
            _darkness = Mathf.MoveTowards(_darkness, target, Time.deltaTime * (target < 1f ? 8f : 2.5f)); // 갑자기 어두워지고 천천히 밝아진다
            if (_sun != null) _sun.intensity = _sunIntensity * _darkness;
            RenderSettings.ambientLight = _ambient * _darkness;
        }

        private void Update()
        {
            float now = Time.time;
            UpdateExtras(now);
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
