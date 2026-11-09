// 서버 상태의 월드 표시 (§16 "클라이언트는 위치 보간 · 애니메이션 · VFX", §21.4)
//   · 미니언 · 소환수 · 설치물 · 투사체 · 구조물 · 상대 플레이어 아바타 · 타워 총알을 스냅샷에서 읽어 그린다 (표시 전용, 판정 없음).
//   · Commander 발판 · 구조물은 씬 아트가 담당한다. 아군 / 적군 색은 보는 사람 기준 (TeamArt — 진영 배정 시 다시 적용).
//   · 타워 총알: VFX_Tower_Ally_Projectile / VFX_Tower_Enemy_Projectile (아군 · 적 타워), 없으면 작은 발광 구체.
//   · 유닛 위치는 매 프레임 목표 위치로 보간한다. 공격 중에는 대상을 바라본다. 드래곤은 공중(비행 높이)에 그린다.
//   · 유닛 상태 표시: 프리즈(H01) = 얼음색 + 애니메이션 정지 · 토네이도(S05) = 붙잡혀 회전 · 분노(H05) = 붉은색 + 빠른 애니메이션
//   · 공격 연출 (UnitSnap.AttackCount가 늘 때): 오크 도끼 베기 · 드래곤 화염 · 터렛 궁수 화살 · 소환탑 해골 소환
//   · 소환수 · 설치물의 등장 · 사라짐 연출 (VFX_Unit_Spawn / VFX_Unit_Death)
//   · 상태 효과 표시: S08 쉴드(구) · H04 반사 결계(육각 판) · S10 집중 · S01 연막(상대 머리) · S09 타워 버프(상자) · H01 타워 빙결
//     내 쉴드 · 결계는 눈높이 아래에만 두어 시야를 가리지 않는다.
//   · 아트 프리팹(MDL_* / VFX_*)이 카탈로그에 있으면 쓰고, 없으면 캡슐 · 큐브 대체 비주얼을 쓴다 (§25.4).
//   · 전투 중 생성 · 파괴 대신 풀을 쓴다 (§17).
//   · S11 도끼 전사: 전장에서 폭발과 함께 솟아나고 충격파 + 돌 파편 10개 (3초 뒤 삭제) · S12 드래곤: 내려앉으면 바람이 인다
//   · S03 파이어볼: 용암 바위 구체(Lavabrick)가 앞장서 떨어지고 그 뒤로 이펙트 팩 불길(짧게) + 튀는 불씨
//   · S13 터렛: StoneKeep Tower_c 탑 (예전 탑의 1.5배 높이) 위에 궁수 · S04 화살은 전장 바닥에 꽂힌다 · S05 토네이도 = EzTornado

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Audio;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Pooling;
using SpellboundVR.Spells;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class ArenaStateView : MonoBehaviour
    {
        private sealed class UnitView
        {
            public GameObject Go;
            public Transform HpFill;
            public Vector3 Target;
            public int SeenFrame;
            /// <summary>모델 프리팹의 Animator (int 파라미터 "State"가 있을 때만)</summary>
            public Animator Animator;
            public int LastState = -1;
            public MinionKind Kind;
            public Team Team;
            public byte LastAttackCount;
            public int LastTint = -1;
            public float Spin;
            public Renderer[] ModelRenderers;
            /// <summary>솟아나는 등장 연출 시작 시각 (-1 = 없음)</summary>
            public float PopStart = -1f;
        }

        /// <summary>날아가는 돌 파편 · 퍼지는 충격파 · 바닥에 꽂힌 화살 (표시 전용)</summary>
        private sealed class Debris
        {
            public Transform T;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Start;
            public float End;
            /// <summary>충격파 고리 (null 이면 파편 · 화살)</summary>
            public Material Wave;
            /// <summary>바닥에 꽂힌 화살 — 움직이지 않고 시간이 되면 풀로 돌려보낸다</summary>
            public bool Stuck;
        }

        /// <summary>도끼 전사 등장 파편에 쓰는 AEGIS 돌 (VFXCatalog)</summary>
        private static readonly string[] DebrisRocks = { "Small Rock1", "Small Rock4", "Small Rock5", "Small Rock7", "Rock1", "Rock2" };

        /// <summary>Animator int 파라미터: 0 이동 · 1 공격 · 2 돌파(이동) · 3 빙결 · 4 회오리 (UnitVisualState)</summary>
        private static readonly int AnimStateHash = Animator.StringToHash("State");

        private sealed class StructureView
        {
            public Transform Root;
            public Transform HpFill;
            public bool WasAlive = true;
            public Team Team;
            /// <summary>파괴 후 남는 잔해 (평소엔 꺼둠)</summary>
            public GameObject Rubble;
            /// <summary>총알이 나가는 높이 (비주얼 윗면의 80%)</summary>
            public float MuzzleHeight;
            public Vector3 Size;
            public int LastShotCount = -1;
            public GameObject BuffFx;
            public GameObject FreezeFx;
            public bool WasFrozen;
        }

        /// <summary>날아가는 표시용 탄 (타워 총알 · 터렛 화살) — 피해는 서버가 발사 순간 이미 줬다</summary>
        private sealed class TowerShot
        {
            public GameObject Go;
            public ObjectPool Pool;
            public Vector3 From;
            public Vector3 To;
            public int TargetId;
            public float Elapsed;
            public float Duration;
            public float Arc;
        }

        public float unitLerpSpeed = 10f;
        public int meleePrewarm = 24;
        public int rangedPrewarm = 20;
        public int brutePrewarm = 4;
        public int summonPrewarm = 2;
        public int skeletonPrewarm = 8;
        public int projectilePrewarm = 24;
        [Tooltip("플레이어 크기 배율 — 상대 아바타 · 쉴드 (눈높이는 OVRCameraRig 높이)")]
        public float playerScale = 2f;
        [Tooltip("타워 총알 속도 (m/s) — 마법 불꽃이 날아가는 게 보이도록 느리게 (사거리 약 10m → 약 0.8초)")]
        public float towerShotSpeed = 12f;
        [Tooltip("드래곤 비행 높이 (m)")]
        public float dragonFlightHeight = 5f;
        [Tooltip("S05 토네이도 (EzTornado 프리팹) 크기 배율 — 원본은 수십 m 크기")]
        public float tornadoScale = 0.1f;
        [Tooltip("S07 총난사 탄 크기 배율")]
        public float gatlingShotScale = 2f;

        [Header("소환수 · 설치물 모델 크기 배율 (MDL_* 프리팹 기준)")]
        [Tooltip("S11 도끼 전사 — 미니언 사이에서 눈에 띄게 크게")]
        public float axeOrcScale = 2.25f;
        [Tooltip("S12 드래곤 — 날개 폭 6m 프리팹 기준")]
        public float dragonScale = 1.65f;
        [Tooltip("S13 터렛")]
        public float turretScale = 0.6f;
        [Tooltip("H06 소환탑")]
        public float summonHutScale = 0.6f;

        [Header("타워 · 넥서스 파괴")]
        [Tooltip("파괴 폭발 연출 크기 배율")]
        public float destroyFxScale = 3f;
        [Tooltip("내 쉴드 · 결계 윗면을 눈높이보다 이만큼 아래(가슴 높이)에 둔다 (전장을 내려다보는 시야 방해 금지)")]
        public float selfBarrierBelowEyes = 0.65f;
        [Tooltip("내 쉴드 · 결계 육각 막의 밝기 (상대에게 보이는 것보다 옅게)")]
        public float selfBarrierGlow = 0.3f;

        private ClientSession _session;
        private VFXCatalog _catalog;
        private VFXPlayer _vfx;
        private Transform _head;
        private ArenaGeometry _arena;
        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>(64);
        private readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>(64);
        private readonly Dictionary<GameObject, Renderer[]> _modelRenderers = new Dictionary<GameObject, Renderer[]>(128);
        private readonly List<int> _removeBuffer = new List<int>(64);
        private readonly List<Debris> _debris = new List<Debris>(32);
        /// <summary>바닥에 꽂힐 화살 투사체 Id → 마지막 서버 위치</summary>
        private readonly Dictionary<int, Vector3> _arrows = new Dictionary<int, Vector3>(16);
        private readonly ObjectPool[,] _unitPools = new ObjectPool[TeamUtil.TeamCount, MinionDefinition.KindCount];
        private readonly Dictionary<int, ObjectPool> _projectilePools = new Dictionary<int, ObjectPool>();
        private Transform _poolRoot;
        private readonly StructureView[] _structures = new StructureView[MatchSnapshot.StructureCount];
        /// <summary>[0] = 아군 타워 총알, [1] = 적 타워 총알 (보는 사람 기준)</summary>
        private readonly ObjectPool[] _shotPools = new ObjectPool[2];
        private ObjectPool _arrowShotPool;
        private readonly List<TowerShot> _shots = new List<TowerShot>(16);
        /// <summary>진영마다 아바타 하나 — 로컬 플레이어 것만 끈다 (1:1 은 2개, 개인전은 4개)</summary>
        private sealed class AvatarView
        {
            public Team Team;
            public Transform Root;
            public GameObject Shield, Reflect, Focus, Blind;
        }

        private readonly List<AvatarView> _avatars = new List<AvatarView>(4);
        /// <summary>관전 중 카메라가 들어가 있는 사람의 아바타는 끈다 (-1 = 없음)</summary>
        private int _hiddenAvatar = -1;
        private GameObject _selfShield, _selfReflect, _selfFocus;
        private Transform _selfShieldRing;
        private MaterialPropertyBlock _block;
        private int _frame;
        private int _lastMatchSerial = -1;

        /// <summary>구조물 · 아바타 크기 배율 (개인전은 구조물이 가까워서 줄인다)</summary>
        private float ArtScale => _arena.IsDuel || _session.Layout == null ? 1f : _session.Layout.ffaModelScale;

        private float AvatarScale => playerScale * ArtScale;

        /// <summary>Commander 발판 윗면 높이 (ArenaLayout.commanderPlatformHeight)</summary>
        private float PlatformHeight => _session.Layout != null ? _session.Layout.commanderPlatformHeight : 0f;

        private static string ModelName(MinionKind kind)
        {
            switch (kind)
            {
                case MinionKind.Ranged: return "MDL_Minion_Ranged";
                case MinionKind.Brute: return "MDL_Minion_Brute";
                case MinionKind.AxeOrc: return "MDL_Summon_AxeOrc";
                case MinionKind.Dragon: return "MDL_Summon_Dragon";
                case MinionKind.Skeleton: return "MDL_Summon_Skeleton";
                case MinionKind.Turret: return "MDL_Structure_Turret";
                case MinionKind.SummonHut: return "MDL_Structure_SummonHut";
                default: return "MDL_Minion_Melee";
            }
        }

        private static bool IsStructureKind(MinionKind kind) => kind == MinionKind.Turret || kind == MinionKind.SummonHut;

        private static bool IsSummonKind(MinionKind kind) => (int)kind >= (int)MinionKind.AxeOrc;

        private static bool IsFlyingSummon(SpellDefinition def) => def != null && def.FromOffMap && def.Executor == SpellExecutorType.Summon;

        /// <summary>모델 크기 배율 (인스펙터 값)</summary>
        private float ModelScale(MinionKind kind)
        {
            switch (kind)
            {
                case MinionKind.AxeOrc: return axeOrcScale;
                case MinionKind.Dragon: return dragonScale;
                case MinionKind.Turret: return turretScale;
                case MinionKind.SummonHut: return summonHutScale;
                default: return 1f;
            }
        }

        /// <summary>HP바 높이 — 모델 크기 배율 적용</summary>
        private float ScaledHpBarHeight(MinionKind kind) => HpBarHeight(kind) * ModelScale(kind);

        /// <summary>HP바 높이 (배율 1 기준 모델 키 + 여유)</summary>
        private static float HpBarHeight(MinionKind kind)
        {
            switch (kind)
            {
                case MinionKind.Brute: return 2.4f;
                case MinionKind.AxeOrc: return 2.9f;
                case MinionKind.Dragon: return 2.6f;
                case MinionKind.Skeleton: return 1.3f;
                case MinionKind.Turret: return 8.2f; // Tower_c 탑 (예전 탑의 1.5배)
                case MinionKind.SummonHut: return 4.4f;
                default: return 1.4f;
            }
        }

        public void Initialize(ClientSession session, VFXCatalog catalog, Transform head)
        {
            _session = session;
            _catalog = catalog;
            _head = head;
            _arena = session.Arena;
            _block = new MaterialPropertyBlock();
            _vfx = GetComponent<VFXPlayer>();

            var poolRoot = new GameObject("[Unit Pools]").transform;
            poolRoot.SetParent(transform, false);
            int divide = _arena.TeamCount / 2; // 4개 진영이면 진영당 미리 만드는 수를 줄인다 (모자라면 풀이 늘린다)
            for (int t = 0; t < _arena.TeamCount; t++)
            {
                var team = (Team)t;
                for (int k = 0; k < MinionDefinition.KindCount; k++)
                {
                    var kind = (MinionKind)k;
                    int prewarm = kind == MinionKind.Melee ? meleePrewarm / divide
                                : kind == MinionKind.Ranged ? rangedPrewarm / divide
                                : kind == MinionKind.Brute ? brutePrewarm / divide
                                : kind == MinionKind.Skeleton ? skeletonPrewarm / divide
                                : IsStructureKind(kind) ? 1 : summonPrewarm;
                    _unitPools[t, k] = new ObjectPool(() => CreateUnitVisual(team, kind), poolRoot, prewarm, "Unit_" + team + "_" + kind);
                }
            }
            _poolRoot = poolRoot;
            for (int i = 0; i < 2; i++)
            {
                bool ally = i == 0;
                _shotPools[i] = new ObjectPool(() => CreateTowerShotVisual(ally), poolRoot, 6, ally ? "TowerShot_Ally" : "TowerShot_Enemy");
            }
            _arrowShotPool = new ObjectPool(CreateArrowShotVisual, poolRoot, 4, "TurretArrow");

            BuildStructures(); // 개인전 2 · 4번 구조물은 여기서 복제되므로 팀 아트 적용보다 먼저
            TeamArt.ApplyAll(LocalTeam);
            _session.Events.OnLocalTeamAssigned += TeamArt.ApplyAll;
            for (int t = 0; t < _arena.TeamCount; t++) _avatars.Add(BuildAvatar((Team)t));
            BuildSelfStatusVisuals();
        }

        // ── 생성 ─────────────────────────────────────────────

        private GameObject CreateUnitVisual(Team team, MinionKind kind)
        {
            string modelName = ModelName(kind);
            GameObject go;
            if (_catalog != null && _catalog.TryGet(modelName, out var prefab))
            {
                go = Instantiate(prefab);
            }
            else
            {
                go = new GameObject(modelName);
                PrimitiveType shape;
                Vector3 size;
                switch (kind)
                {
                    case MinionKind.Ranged: shape = PrimitiveType.Cylinder; size = new Vector3(0.45f, 0.27f, 0.45f); break;
                    case MinionKind.Brute: shape = PrimitiveType.Capsule; size = new Vector3(1f, 0.75f, 1f); break;
                    case MinionKind.AxeOrc: shape = PrimitiveType.Capsule; size = new Vector3(1.1f, 1.1f, 1.1f); break;
                    case MinionKind.Dragon: shape = PrimitiveType.Sphere; size = new Vector3(2f, 0.8f, 2.4f); break;
                    case MinionKind.Skeleton: shape = PrimitiveType.Capsule; size = new Vector3(0.4f, 0.45f, 0.4f); break;
                    case MinionKind.Turret: shape = PrimitiveType.Cylinder; size = new Vector3(1.6f, 2.2f, 1.6f); break;
                    case MinionKind.SummonHut: shape = PrimitiveType.Cube; size = new Vector3(2.2f, 3.2f, 2.2f); break;
                    default: shape = PrimitiveType.Capsule; size = new Vector3(0.55f, 0.41f, 0.55f); break;
                }
                var body = FallbackVisuals.Primitive(shape, "Body", FallbackVisuals.Opaque(FallbackVisuals.TeamColor(team)));
                body.transform.SetParent(go.transform, false);
                body.transform.localScale = size;
                body.transform.localPosition = new Vector3(0f, shape == PrimitiveType.Cube ? size.y * 0.5f : size.y, 0f);
            }
            if (kind == MinionKind.Turret) RebuildTurretTower(go.transform);
            // 모델 크기 배율: 자식(모델)만 키우고 줄인다 — HP바 두께 · 폭은 그대로
            float scale = ModelScale(kind);
            if (!Mathf.Approximately(scale, 1f))
            {
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = go.transform.GetChild(i);
                    child.localPosition *= scale;
                    child.localScale *= scale;
                }
            }
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            _modelRenderers[go] = renderers;
            ApplyTint(renderers, BaseTint(team, kind));
            AttachHpBar(go.transform, ScaledHpBarHeight(kind), IsStructureKind(kind) ? 1.4f : 0.6f);
            return go;
        }

        /// <summary>
        /// S13 터렛 탑을 StoneKeep Tower_c 로 바꾼다 — 예전 탑(StoneTower, 꼭대기 17 × 0.23)보다 1.5배 높게, 궁수(ArcherMount)는 새 꼭대기로.
        /// Tower_c 가 카탈로그에 없으면 예전 탑 그대로.
        /// </summary>
        private void RebuildTurretTower(Transform model)
        {
            const float sink = -0.46f;          // 예전 탑과 같이 바닥에 살짝 묻는다
            const float oldTop = 17f * 0.23f + sink;
            const float newTopLocal = 22f;      // Tower_c 의 Top_b 높이 (원본 단위)
            var oldTower = model.Find("StoneTower");
            var archer = model.Find("ArcherMount");
            if (oldTower == null || archer == null || _catalog == null || !_catalog.TryGet("Tower_c", out var towerPrefab)) return;
            float top = oldTop * 1.5f;
            var tower = Instantiate(towerPrefab, model, false).transform;
            tower.name = "StoneTower";
            tower.localPosition = new Vector3(0f, sink, 0f);
            tower.localScale = Vector3.one * ((top - sink) / newTopLocal);
            foreach (var t in tower.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Rubble")) t.gameObject.SetActive(false); // 무너진 돌무더기 장식은 빼고
            foreach (var c in tower.GetComponentsInChildren<Collider>(true)) Destroy(c);
            oldTower.SetParent(null, false); // 렌더러 목록 · 크기 배율에서 빠지게
            Destroy(oldTower.gameObject);
            archer.localPosition = new Vector3(archer.localPosition.x, top, archer.localPosition.z);
        }

        /// <summary>진영색 틴트 — 소환수 · 설치물은 원래 텍스처가 살도록 더 연하게</summary>
        private static Color BaseTint(Team team, MinionKind kind)
        {
            float k = (int)kind <= (int)MinionKind.Brute ? 0.45f : 0.2f;
            return Color.Lerp(Color.white, FallbackVisuals.TeamColor(team), k);
        }

        private void ApplyTint(Renderer[] renderers, Color color)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || renderers[i] is ParticleSystemRenderer) continue;
                renderers[i].GetPropertyBlock(_block);
                _block.SetColor("_Color", color);
                _block.SetColor("_BaseColor", color);
                _block.SetColor("_TeamColor", color);
                renderers[i].SetPropertyBlock(_block);
            }
        }

        private static Animator FindStateAnimator(GameObject go)
        {
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null) return null;
            var ps = anim.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].nameHash == AnimStateHash && ps[i].type == AnimatorControllerParameterType.Int) return anim;
            return null;
        }

        /// <summary>스킬별 비행 탄환: VFX_{SkillId}_{Name}_Projectile 프리팹(+Z = 진행 방향), 없으면 테마색 구체</summary>
        private ObjectPool GetProjectilePool(int spellId)
        {
            if (_projectilePools.TryGetValue(spellId, out var pool)) return pool;
            var def = _session.Catalog.Get(spellId);
            GameObject prefab = null;
            bool hasPrefab = def != null && _catalog != null &&
                             (_catalog.TryGet(def.GetVfxPrefabName(VfxPart.Projectile), out prefab) || // S05 토네이도 = EzTornado (카탈로그 별칭)
                              // 맵 밖에서 날아오는 소환수: 유닛 모델이 그대로 날아온다
                              (IsFlyingSummon(def) && _catalog.TryGet(ModelName(def.SummonKind), out prefab)));
            if (hasPrefab && def.SpellId == SpellIds.S03_Fireball)
            {
                var source = prefab;
                pool = new ObjectPool(() => CreateFireball(source), _poolRoot, 2, "Projectile_" + spellId);
            }
            else if (hasPrefab && def.Behavior == SpellBehaviorType.TornadoLane && !prefab.name.StartsWith("VFX_"))
            {
                // EzTornado: 월드 공간 · 수십 m 크기로 만든 파티클 — 로컬 · 계층 스케일로 바꾸고 줄여서 부모째 움직인다
                var source = prefab;
                pool = new ObjectPool(() =>
                {
                    var root = new GameObject("Tornado");
                    var inner = Instantiate(source, root.transform, false);
                    inner.transform.localScale = Vector3.one * tornadoScale;
                    VFXPlayer.MakeScalable(inner);
                    return root;
                }, _poolRoot, 2, "Projectile_" + spellId);
            }
            else if (hasPrefab)
            {
                pool = new ObjectPool(prefab, _poolRoot, def.ShotCount > 4 ? projectilePrewarm : 4);
            }
            else
            {
                var color = def != null ? def.ThemeColor : new Color(1f, 0.9f, 0.3f);
                pool = new ObjectPool(() =>
                {
                    var go = CreateProjectileVisual();
                    go.GetComponent<Renderer>().sharedMaterial = FallbackVisuals.Opaque(color);
                    return go;
                }, _poolRoot, projectilePrewarm, "Projectile_" + spellId);
            }
            _projectilePools.Add(spellId, pool);
            return pool;
        }

        /// <summary>S03 파이어볼 불길(월드 공간 파티클)이 뒤로 남는 길이 비율 — 길이 = 낙하 속도 × 파티클 수명</summary>
        private const float FireballTrailRatio = 0.25f;

        /// <summary>파이어볼 용암 구체 지름 · 진행 방향으로 앞선 거리 (프리팹 단위 — 불꽃 입자 1.5 기준, 경기에서는 OffMapVisualScale 배)</summary>
        private const float FireballCoreSize = 0.6f;
        private const float FireballCoreLead = 0.35f;
        /// <summary>이펙트 팩 불길 묶음을 탄 중심에서 뒤로 미는 거리 (프리팹 단위) — 구체 뒤에서 타오르게</summary>
        private const float FireballFlameBack = 0.6f;
        private static Material s_fireballEmber;

        /// <summary>
        /// S03 파이어볼 탄 — 용암 바위 구체(MDL_Fireball_Core = Lavabrick 재질)가 진행 방향 앞에 서고, 이펙트 팩 불길 묶음은 그 뒤로 민다.
        /// 구체는 불투명이라 뒤쪽 불길을 가린다. 크게 키우면 불길이 길게 늘어지므로 뒤에 남는 불의 수명을 줄인다. 둘레에 불씨가 튄다.
        /// </summary>
        private GameObject CreateFireball(GameObject prefab)
        {
            var go = new GameObject("Fireball");
            var flames = Instantiate(prefab, go.transform, false);
            flames.transform.localPosition = new Vector3(0f, 0f, -FireballFlameBack); // 탄은 진행 방향(+Z)을 본다 — 불길은 뒤
            foreach (var ps in flames.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (main.simulationSpace == ParticleSystemSimulationSpace.World) main.startLifetimeMultiplier *= FireballTrailRatio;
                // 예전엔 불길 프리팹이 탄 루트라 자기 스케일로 키워졌다 — 자식이 된 루트 입자만 부모 크기를 따르게 (나머지는 예전과 같은 크기)
                if (ps.gameObject == flames) main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            if (_catalog != null && _catalog.TryGet("MDL_Fireball_Core", out var rock))
            {
                var core = Instantiate(rock, go.transform, false);
                core.name = "Core";
                core.transform.localPosition = new Vector3(0f, 0f, FireballCoreLead); // 탄은 진행 방향(+Z)을 본다 — 구체가 앞, 불길이 뒤
                core.transform.localRotation = Random.rotation;                        // 탄마다 다른 균열 무늬
                core.transform.localScale = Vector3.one * FireballCoreSize;
            }
            AddEmbers(go.transform);
            return go;
        }

        /// <summary>구체 둘레에서 튀어 날아가는 자리 뒤에 흩어지는 작은 불씨</summary>
        private static void AddEmbers(Transform parent)
        {
            if (s_fireballEmber == null)
            {
                const int n = 32;
                var dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FireballEmber" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                        dot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                dot.Apply();
                s_fireballEmber = new Material(FallbackVisuals.Transparent(Color.white)) { mainTexture = dot, name = "FireballEmber" };
            }
            var embers = new GameObject("Embers").AddComponent<ParticleSystem>();
            embers.transform.SetParent(parent, false);
            embers.transform.localPosition = new Vector3(0f, 0f, FireballCoreLead);
            embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var em = embers.main;
            em.loop = true;
            em.simulationSpace = ParticleSystemSimulationSpace.World;   // 날아가며 뒤에 흩뿌려진다
            em.scalingMode = ParticleSystemScalingMode.Hierarchy;
            em.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            em.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.4f, 0.05f));
            em.maxParticles = 60;
            var rate = embers.emission;
            rate.rateOverTime = 30f;
            var shape = embers.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = FireballCoreSize * 0.5f;
            var fade = embers.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var r = embers.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_fireballEmber;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            embers.Play(true);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.Events.OnLocalTeamAssigned -= TeamArt.ApplyAll;
        }

        /// <summary>진영 배정 전(오프라인 · 서버 화면)에는 Home 시점</summary>
        private Team LocalTeam => _session.Events.HasLocalTeam ? _session.Events.LocalTeam : Team.Home;

        /// <summary>지금 화면에 보이는 그 진영의 아바타 (내 아바타 · 관전 중인 사람 · 빈자리는 null)</summary>
        public Transform VisibleAvatar(Team team)
        {
            for (int i = 0; i < _avatars.Count; i++)
                if (_avatars[i].Team == team) return _avatars[i].Root.gameObject.activeSelf ? _avatars[i].Root : null;
            return null;
        }

        /// <summary>아바타 크기 배율 (플레이어 크기 × 경기 종류별 배율)</summary>
        public float AvatarWorldScale => AvatarScale;

        /// <summary>관전 시점을 옮긴다 — 그 사람 아바타를 끄고 아군 · 적군 색도 그 사람 기준으로 (null = 내 시점)</summary>
        public void SetViewpoint(Team? team)
        {
            _hiddenAvatar = team.HasValue ? (int)team.Value : -1;
            TeamArt.ApplyAll(team ?? LocalTeam);
        }

        /// <summary>타워 총알: 카탈로그 VFX(마법 불꽃)가 있으면 쓰고, 없으면 작은 발광 구체 + 짧은 꼬리 (진영 색을 연하게)</summary>
        private GameObject CreateTowerShotVisual(bool ally)
        {
            string vfxName = ally ? "VFX_Tower_Ally_Projectile" : "VFX_Tower_Enemy_Projectile";
            if (_catalog != null && _catalog.TryGet(vfxName, out var prefab)) return Instantiate(prefab);

            Color c = Color.Lerp(FallbackVisuals.LightTeamColor(ally ? Team.Home : Team.Away), new Color(1f, 0.95f, 0.6f), 0.5f);
            var go = FallbackVisuals.Primitive(PrimitiveType.Sphere, "TowerShot", FallbackVisuals.Opaque(c));
            go.transform.localScale = Vector3.one * 0.16f;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.12f;
            trail.startWidth = 0.12f;
            trail.endWidth = 0f;
            trail.sharedMaterial = FallbackVisuals.Transparent(new Color(c.r, c.g, c.b, 0.6f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            return go;
        }

        /// <summary>터렛 궁수 화살 (S04 화살 모델 재사용), 없으면 가는 막대</summary>
        private GameObject CreateArrowShotVisual()
        {
            if (_catalog != null && _catalog.TryGet("VFX_Unit_Turret_Projectile", out var prefab)) return Instantiate(prefab);
            var go = FallbackVisuals.Primitive(PrimitiveType.Cube, "TurretArrow", FallbackVisuals.Opaque(new Color(0.6f, 0.45f, 0.3f)));
            go.transform.localScale = new Vector3(0.04f, 0.04f, 0.7f);
            return go;
        }

        private static GameObject CreateProjectileVisual()
        {
            var go = FallbackVisuals.Primitive(PrimitiveType.Sphere, "Projectile", FallbackVisuals.Opaque(new Color(1f, 0.9f, 0.3f)));
            go.transform.localScale = Vector3.one * 0.18f;
            return go;
        }

        private static Transform AttachHpBar(Transform parent, float height, float width)
        {
            var bar = new GameObject("HpBar").transform;
            bar.SetParent(parent, false);
            bar.localPosition = new Vector3(0f, height, 0f);
            var bg = FallbackVisuals.Primitive(PrimitiveType.Cube, "Bg", FallbackVisuals.Opaque(new Color(0.1f, 0.1f, 0.1f)));
            bg.transform.SetParent(bar, false);
            bg.transform.localScale = new Vector3(width, 0.06f, 0.02f);
            var fill = FallbackVisuals.Primitive(PrimitiveType.Cube, "Fill", FallbackVisuals.Opaque(new Color(0.3f, 1f, 0.3f)));
            fill.transform.SetParent(bar, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            fill.transform.localScale = new Vector3(width, 0.05f, 0.02f);
            return fill.transform;
        }

        /// <summary>상태 표시용 VFX 인스턴스 (카탈로그에 없으면 null)</summary>
        private GameObject CreateStatusFx(string prefabName, Transform parent)
        {
            if (_catalog == null || !_catalog.TryGet(prefabName, out var prefab)) return null;
            var go = Instantiate(prefab, parent, false);
            go.name = prefabName;
            go.SetActive(false);
            return go;
        }

        private void BuildStructures()
        {
            for (int t = 0; t < _arena.TeamCount; t++)
            {
                var team = (Team)t;
                BuildStructure(team, false, Column.Center);
                BuildStructure(team, true, Column.Center);
                if (_arena.IsDuel) continue;
                BuildStructure(team, false, Column.Left);
                BuildStructure(team, false, Column.Right);
            }
        }

        /// <summary>타워는 레인마다 하나 (1:1 은 가운데만)</summary>
        private void BuildStructure(Team team, bool isNexus, Column column)
        {
            var layout = _session.Layout;
            Transform visual = layout != null && column == Column.Center ? layout.GetStructureVisual(team, isNexus) : null;
            Vector3 pos = _arena.ToWorld(isNexus ? _arena.StructurePosition(team, true) : _arena.TowerPosition(team, column));
            if (!_arena.IsDuel && layout != null) visual = PlaceFfaStructure(layout, team, isNexus, visual, pos);
            if (visual == null)
            {
                string modelName = isNexus ? "MDL_Structure_Nexus" : "MDL_Structure_Tower";
                GameObject go;
                if (_catalog != null && _catalog.TryGet(modelName, out var prefab)) go = Instantiate(prefab);
                else
                {
                    go = new GameObject(modelName + "_" + team);
                    var body = FallbackVisuals.Primitive(isNexus ? PrimitiveType.Cylinder : PrimitiveType.Cube, "Body",
                                                         FallbackVisuals.Opaque(FallbackVisuals.TeamColor(team) * (isNexus ? 0.8f : 0.6f)));
                    body.transform.SetParent(go.transform, false);
                    body.transform.localScale = isNexus ? new Vector3(2f, 1.2f, 2f) : new Vector3(1.4f, 3f, 1.4f);
                    body.transform.localPosition = new Vector3(0f, isNexus ? 1.2f : 1.5f, 0f);
                }
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(pos, _arena.FacingRotation(team));
                visual = go.transform;
            }
            if (!_arena.IsDuel) visual.localScale = Vector3.one * ArtScale; // 1번 구조물을 복제하므로 곱하지 않고 값을 정한다
            // HP바는 비주얼의 스케일을 상속하지 않도록 별도 홀더에 붙인다
            var holder = new GameObject("StructureHp_" + team + (isNexus ? "_Nexus" : "_Tower_" + column)).transform;
            holder.SetParent(transform, false);
            holder.position = new Vector3(visual.position.x, pos.y, visual.position.z);
            var size = VisualSize(visual);
            var view = new StructureView
            {
                Root = visual,
                HpFill = AttachHpBar(holder, (isNexus ? 3.2f : 5.4f) * ArtScale, 1.6f), // 아트 높이: 넥서스 2.4m · 타워 4.8m
                Team = team,
                MuzzleHeight = size.y * 0.8f,
                Size = size,
                Rubble = CreateRubble(holder, size),
            };
            if (!isNexus)
            {
                // S09 타워 버프 상자 · H01 빙결 — 타워 크기에 맞춤 (프리팹은 1m 상자 · 반경 1m 기준)
                view.BuffFx = CreateStatusFx("VFX_S09_TowerBuff_Loop", holder);
                if (view.BuffFx != null) view.BuffFx.transform.localScale = new Vector3(Mathf.Max(2f, size.x * 1.25f), size.y * 1.08f, Mathf.Max(2f, size.z * 1.25f));
                view.FreezeFx = CreateStatusFx("VFX_H01_Freeze_Loop", holder);
                if (view.FreezeFx != null) view.FreezeFx.transform.localScale = Vector3.one * Mathf.Max(1.5f, Mathf.Max(size.x, size.z) * 0.8f);
            }
            _structures[isNexus ? MatchSnapshot.StructureIndex(team, true) : MatchSnapshot.TowerIndex(team, column)] = view;
        }

        /// <summary>개인전: 씬의 1 · 3번 구조물은 개인전 깊이로 옮기고, 그 밖의 구조물(2 · 4번 · 옆 레인 타워)은 1번 구조물을 복제해 세운다</summary>
        private Transform PlaceFfaStructure(ArenaLayout layout, Team team, bool isNexus, Transform visual, Vector3 pos)
        {
            if (visual == null)
            {
                var source = layout.GetStructureVisual(Team.Home, isNexus);
                if (source == null) return null;
                visual = Instantiate(source.gameObject, transform).transform;
                visual.name = (isNexus ? "Nexus_" : "Tower_") + team;
                foreach (var art in visual.GetComponentsInChildren<TeamArt>(true)) art.team = team;
            }
            visual.SetPositionAndRotation(pos, _arena.FacingRotation(team));
            return visual;
        }

        /// <summary>파괴된 타워 · 넥서스 자리에 남는 돌무더기 (구조물 크기에 맞춤, 회색 돌)</summary>
        private static GameObject CreateRubble(Transform parent, Vector3 size)
        {
            var root = new GameObject("Rubble");
            root.transform.SetParent(parent, false);
            var mat = FallbackVisuals.Opaque(new Color(0.38f, 0.36f, 0.34f));
            float w = Mathf.Clamp(Mathf.Max(size.x, size.z), 1.5f, 6f);
            const int pieces = 9;
            for (int i = 0; i < pieces; i++)
            {
                float a = i * 2.39996f; // 황금각 — 고르게 흩어지게
                float r = w * 0.45f * Mathf.Sqrt((i + 0.5f) / pieces);
                float s = w * (0.32f - 0.02f * i);
                var piece = FallbackVisuals.Primitive(PrimitiveType.Cube, "Stone" + i, mat);
                piece.transform.SetParent(root.transform, false);
                piece.transform.localScale = new Vector3(s, s * 0.6f, s * 0.8f);
                piece.transform.localPosition = new Vector3(Mathf.Cos(a) * r, s * 0.25f, Mathf.Sin(a) * r);
                piece.transform.localRotation = Quaternion.Euler(i * 23f % 40f - 20f, i * 47f, i * 31f % 30f - 15f);
            }
            root.SetActive(false);
            return root;
        }

        /// <summary>비주얼 피벗(바닥) 기준 크기 (가로 · 높이 · 세로). 렌더러가 없으면 2m 상자</summary>
        private static Vector3 VisualSize(Transform visual)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Vector3(2f, 2f, 2f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return new Vector3(b.size.x, Mathf.Max(0.5f, b.max.y - visual.position.y), b.size.z);
        }

        private AvatarView BuildAvatar(Team team)
        {
            GameObject go;
            if (_catalog != null && _catalog.TryGet("MDL_Avatar_Wizard", out var prefab))
            {
                go = Instantiate(prefab);
                var hand = FindDeep(go.transform, "PT_Right_Hand_Weapon_slot");
                if (hand != null && _catalog.TryGet("MDL_Avatar_Staff", out var staff)) Instantiate(staff, hand, false);
                ApplyTint(go.GetComponentsInChildren<Renderer>(true), Color.Lerp(Color.white, FallbackVisuals.TeamColor(team), 0.3f));
            }
            else
            {
                go = new GameObject("OpponentAvatar");
                var body = FallbackVisuals.Primitive(PrimitiveType.Capsule, "Body", FallbackVisuals.Opaque(Color.white));
                body.transform.SetParent(go.transform, false);
                body.transform.localPosition = new Vector3(0f, 1f, 0f);
                body.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
                var hat = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "Hat", FallbackVisuals.Opaque(new Color(0.2f, 0.1f, 0.35f)));
                hat.transform.SetParent(go.transform, false);
                hat.transform.localPosition = new Vector3(0f, 2.15f, 0f);
                hat.transform.localScale = new Vector3(0.5f, 0.25f, 0.5f);
            }
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * AvatarScale;
            go.transform.position = _arena.ToWorld(_arena.PlayerPosition(team, 1), PlatformHeight);
            go.SetActive(false);
            var avatar = new AvatarView { Team = team, Root = go.transform };

            // S08 쉴드 (구, 반경 1 프리팹 → 아바타 기준 1.3)
            avatar.Shield = CreateStatusFx("VFX_S08_Shield_Loop", avatar.Root);
            if (avatar.Shield == null)
            {
                avatar.Shield = FallbackVisuals.Primitive(PrimitiveType.Sphere, "OpponentShield",
                                                          FallbackVisuals.Transparent(new Color(0.35f, 0.75f, 1f, 0.25f)));
                avatar.Shield.transform.SetParent(avatar.Root, false);
                avatar.Shield.SetActive(false);
            }
            avatar.Shield.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            avatar.Shield.transform.localScale = Vector3.one * 1.3f;

            // H04 반사 결계 (반경 1 · 높이 2 프리팹)
            avatar.Reflect = CreateStatusFx("VFX_H04_ReflectBarrier_Loop", avatar.Root);
            if (avatar.Reflect != null) avatar.Reflect.transform.localScale = new Vector3(1.4f, 1.25f, 1.4f);
            // S10 집중 (발밑)
            avatar.Focus = CreateStatusFx("VFX_S10_Focus_Loop", avatar.Root);
            // S01 연막에 맞은 상대 — 머리 주변 검은 연기
            avatar.Blind = CreateStatusFx("VFX_S01_Smoke_Loop", avatar.Root);
            if (avatar.Blind != null) avatar.Blind.transform.localPosition = new Vector3(0f, 2.0f, 0f);
            return avatar;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private void BuildSelfStatusVisuals()
        {
            _selfShield = CreateStatusFx("VFX_S08_Shield_Loop", transform);
            _selfReflect = CreateStatusFx("VFX_H04_ReflectBarrier_Loop", transform);
            DimBarrier(_selfShield);
            DimBarrier(_selfReflect);
            _selfFocus = CreateStatusFx("VFX_S10_Focus_Loop", transform);
            if (_selfShield == null)
            {
                _selfShieldRing = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "SelfShieldRing",
                                                            FallbackVisuals.Transparent(new Color(0.35f, 0.75f, 1f, 0.35f))).transform;
                _selfShieldRing.SetParent(transform, false);
                _selfShieldRing.localScale = new Vector3(2.2f * playerScale, 0.01f, 2.2f * playerScale);
                _selfShieldRing.gameObject.SetActive(false);
            }
        }

        /// <summary>내 쪽 쉴드 막은 옅게 (HexShield _GlowIntensity), 머리 위로 솟는 왕관 이펙트는 끈다</summary>
        private void DimBarrier(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.GetPropertyBlock(_block);
                _block.SetFloat("_GlowIntensity", selfBarrierGlow);
                r.SetPropertyBlock(_block);
            }
            var crown = go.transform.Find("Crown");
            if (crown != null) crown.gameObject.SetActive(false);
        }

        // ── 갱신 ─────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_session == null) return;
            var s = _session.Events.Latest;
            if (s == null) return;
            _frame++;
            if (s.Header.MatchSerial != _lastMatchSerial)
            {
                _lastMatchSerial = s.Header.MatchSerial;
                for (int i = 0; i < _structures.Length; i++) if (_structures[i] != null) _structures[i].LastShotCount = -1;
            }

            UpdateUnits(s);
            UpdateProjectiles(s);
            UpdateStructures(s);
            UpdateTowerShots();
            UpdatePlayers(s);
            UpdateDebris();
        }

        private void UpdateUnits(MatchSnapshot s)
        {
            float dt = Time.deltaTime;
            float lerp = 1f - Mathf.Exp(-unitLerpSpeed * dt);
            for (int i = 0; i < s.UnitCount; i++)
            {
                ref var u = ref s.Units[i];
                var kind = (MinionKind)Mathf.Clamp(u.Kind, 0, MinionDefinition.KindCount - 1);
                float flight = kind == MinionKind.Dragon ? dragonFlightHeight : 0f;
                Vector3 target = _arena.ToWorld(u.U, u.V, flight);
                bool isNew = false;
                if (!_units.TryGetValue(u.Id, out var view))
                {
                    int team = Mathf.Clamp(u.Team, 0, TeamUtil.TeamCount - 1);
                    var go = _unitPools[team, (int)kind].Get(target, _arena.FacingRotation((Team)team));
                    _modelRenderers.TryGetValue(go, out var renderers);
                    view = new UnitView
                    {
                        Go = go, HpFill = go.transform.Find("HpBar/Fill"), Target = target, Animator = FindStateAnimator(go),
                        Kind = kind, Team = (Team)team, LastAttackCount = u.AttackCount, ModelRenderers = renderers,
                    };
                    _units.Add(u.Id, view);
                    isNew = true;
                    if (IsSummonKind(kind))
                    {
                        Vector3 ground = _arena.ToWorld(u.U, u.V, 0.05f);
                        PlayUnitFx("VFX_Unit_Spawn", ground, Quaternion.identity, 2f);
                        PlaySpawnSound(kind, (Team)team, target);
                        if (kind == MinionKind.AxeOrc)
                        {
                            AxeOrcEntrance(ground);
                            view.PopStart = Time.time;
                        }
                        else if (kind == MinionKind.Dragon) DragonWind(ground);
                    }
                }
                view.SeenFrame = _frame;

                var state = (UnitVisualState)u.State;
                bool raged = (u.Flags & UnitFlags.Raged) != 0;
                Vector3 prev = view.Go.transform.position;
                Vector3 next = isNew ? target : Vector3.Lerp(prev, target, lerp);

                // 회오리에 붙잡힘: 제자리에서 빙글빙글 + 살짝 떠오름
                if (state == UnitVisualState.Stunned)
                {
                    view.Spin += dt * 540f;
                    next.y = target.y + 0.35f + 0.15f * Mathf.Sin(Time.time * 9f + u.Id);
                }
                else
                {
                    view.Spin = 0f;
                }

                Quaternion rot = view.Go.transform.rotation;
                if (IsStructureKind(kind))
                {
                    rot = _arena.FacingRotation(view.Team);
                }
                else if (state == UnitVisualState.Attacking && u.TargetId != 0 && TryGetTargetPosition(u.TargetId, out Vector3 tp))
                {
                    Vector3 look = tp - next;
                    look.y = 0f;
                    if (look.sqrMagnitude > 1e-4f) rot = Quaternion.Slerp(rot, Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-12f * dt));
                }
                else
                {
                    Vector3 move = next - prev;
                    move.y = 0f;
                    if (move.sqrMagnitude > 1e-6f) rot = Quaternion.LookRotation(move, Vector3.up);
                }
                if (view.Spin > 0f) rot = Quaternion.Euler(0f, view.Spin, 0f);
                view.Go.transform.SetPositionAndRotation(next, rot);
                if (view.PopStart >= 0f) PopIn(view);

                SetFill(view.HpFill, u.MaxHp > 0 ? (float)u.Hp / u.MaxHp : 0f);
                Billboard(view.HpFill);

                if (view.Animator != null)
                {
                    if (view.LastState != u.State)
                    {
                        view.LastState = u.State;
                        view.Animator.SetInteger(AnimStateHash, u.State);
                    }
                    view.Animator.speed = state == UnitVisualState.Frozen ? 0f : (raged ? 1.4f : 1f);
                }

                // 상태 틴트: 0 기본 · 1 빙결 · 2 분노
                int tint = state == UnitVisualState.Frozen ? 1 : (raged ? 2 : 0);
                if (tint != view.LastTint && view.ModelRenderers != null)
                {
                    view.LastTint = tint;
                    Color c = tint == 1 ? FrozenTint : (tint == 2 ? new Color(1f, 0.45f, 0.4f) : BaseTint(view.Team, kind));
                    ApplyTint(view.ModelRenderers, c);
                }

                if (u.AttackCount != view.LastAttackCount)
                {
                    view.LastAttackCount = u.AttackCount;
                    PlayAttackFx(view, u.TargetId);
                }
            }

            _removeBuffer.Clear();
            foreach (var kv in _units)
            {
                if (kv.Value.SeenFrame != _frame) _removeBuffer.Add(kv.Key);
            }
            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                var view = _units[_removeBuffer[i]];
                if (view.Go != null)
                {
                    if (IsSummonKind(view.Kind))
                        PlayUnitFx("VFX_Unit_Death", view.Go.transform.position + Vector3.up * 0.5f, Quaternion.identity, 2f);
                    view.Go.SetActive(false);
                }
                _units.Remove(_removeBuffer[i]);
            }
        }

        private void PlayUnitFx(string prefabName, Vector3 position, Quaternion rotation, float lifetime, float scale = 1f)
        {
            if (_vfx == null) return;
            if (Mathf.Approximately(scale, 1f)) _vfx.PlayNamed(prefabName, position, rotation, lifetime);
            else _vfx.PlayScaled(prefabName, position, rotation, lifetime, scale);
        }

        private static readonly Color FrozenTint = new Color(0.3f, 0.55f, 1f);

        /// <summary>S11: 전장에서 폭발 → 전사가 솟아나며 충격파 + 돌 파편 10개 (3초 뒤 삭제)</summary>
        private void AxeOrcEntrance(Vector3 ground)
        {
            if (_vfx != null)
            {
                _vfx.PlayScaled("VFX_S03_Fireball_Impact", ground + Vector3.up * 0.4f, Quaternion.identity, 2.5f, 2f);
                _vfx.PlayScaled("VFX_Unit_Death", ground + Vector3.up * 0.2f, Quaternion.identity, 2f, 2.5f); // 흙먼지
            }
            SoundManager.Play("SFX_TowerDestroy", SoundVolume.Unit, ground, 0.3f);

            var wave = new Material(FallbackVisuals.Transparent(Color.white)) { name = "Shockwave" };
            var ring = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "Shockwave", wave);
            ring.transform.SetParent(transform, false);
            ring.transform.position = ground + Vector3.up * 0.05f;
            _debris.Add(new Debris { T = ring.transform, Wave = wave, Start = Time.time, End = Time.time + 0.6f });

            if (_catalog == null) return;
            for (int i = 0; i < 10; i++)
            {
                if (!_catalog.TryGet(DebrisRocks[i % DebrisRocks.Length], out var rockPrefab)) continue;
                var rock = Instantiate(rockPrefab, transform);
                foreach (var c in rock.GetComponentsInChildren<Collider>(true)) Destroy(c);
                // 돌 크기가 제각각이라 렌더러 크기로 0.35 ~ 0.8m 에 맞춘다
                float size = 1f;
                var renderers = rock.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
                    size = Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                }
                rock.transform.localScale = rock.transform.localScale * (Random.Range(0.35f, 0.8f) / size);
                float angle = (i + Random.Range(-0.3f, 0.3f)) / 10f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                rock.transform.position = ground + dir * 0.8f + Vector3.up * 0.6f;
                rock.transform.rotation = Random.rotation;
                _debris.Add(new Debris
                {
                    T = rock.transform,
                    Velocity = dir * Random.Range(4f, 9f) + Vector3.up * Random.Range(5f, 10f),
                    Spin = Random.insideUnitSphere * 540f,
                    Start = Time.time,
                    End = Time.time + 3f,
                });
            }
        }

        /// <summary>S12: 드래곤이 내려앉으면 날갯짓 바람이 바닥을 휩쓴다 (EzTornado 바람을 납작하게, 효과 없음)</summary>
        private void DragonWind(Vector3 ground)
        {
            if (_vfx == null) return;
            _vfx.PlayNamed("TornadoWithWindEfc", ground, Quaternion.identity, 1.8f, new Vector3(0.16f, 0.035f, 0.16f));
            _vfx.PlayScaled("VFX_Unit_Death", ground + Vector3.up * 0.2f, Quaternion.identity, 1.5f, 2f); // 흙먼지
        }

        /// <summary>솟아나는 등장 — 0.35초 동안 0 → 살짝 넘쳤다가 1</summary>
        private static void PopIn(UnitView view)
        {
            float t = (Time.time - view.PopStart) / 0.35f;
            if (t >= 1f)
            {
                view.PopStart = -1f;
                view.Go.transform.localScale = Vector3.one;
                return;
            }
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float k = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); // easeOutBack
            view.Go.transform.localScale = Vector3.one * Mathf.Max(0.01f, k);
        }

        private void UpdateDebris()
        {
            float now = Time.time, dt = Time.deltaTime, ground = _arena.GroundY;
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                var d = _debris[i];
                if (d.T == null) { _debris.RemoveAt(i); continue; }
                if (now >= d.End)
                {
                    if (d.Stuck) d.T.gameObject.SetActive(false); // 화살은 풀로
                    else
                    {
                        Destroy(d.T.gameObject);
                        if (d.Wave != null) Destroy(d.Wave);
                    }
                    _debris.RemoveAt(i);
                    continue;
                }
                if (d.Stuck) continue;
                if (d.Wave != null)
                {
                    float t = (now - d.Start) / (d.End - d.Start);
                    float r = Mathf.Lerp(1f, 10f, 1f - (1f - t) * (1f - t));
                    d.T.localScale = new Vector3(r * 2f, 0.01f, r * 2f);
                    d.Wave.color = new Color(0.85f, 0.75f, 0.55f, 0.55f * (1f - t));
                    continue;
                }
                d.Velocity += Vector3.down * 9.8f * dt;
                Vector3 p = d.T.position + d.Velocity * dt;
                if (p.y < ground + 0.1f)
                {
                    p.y = ground + 0.1f;
                    d.Velocity = new Vector3(d.Velocity.x * 0.5f, -d.Velocity.y * 0.3f, d.Velocity.z * 0.5f);
                    d.Spin *= 0.5f;
                }
                d.T.position = p;
                d.T.Rotate(d.Spin * dt, Space.World);
            }
        }

        /// <summary>소환 · 설치 소리 (스킬 소리 — 상대 것은 작게). 해골은 소환탑 공격 소리로 대신한다</summary>
        private void PlaySpawnSound(MinionKind kind, Team team, Vector3 at)
        {
            float v = team == LocalTeam ? SoundVolume.MySkill : SoundVolume.EnemySkill;
            switch (kind)
            {
                case MinionKind.AxeOrc:
                    if (SoundManager.Play("SFX_S11OrcSpawn", v, at, 0.3f)) SoundManager.PlayLater(0.5f, "SFX_S11OrcRoar", v * 0.8f, at);
                    break;
                case MinionKind.Dragon:
                    SoundManager.StartKeyed("dragon" + (int)team, "SFX_S12DragonWingLoop", v * 0.7f, at, 3f);
                    break;
                case MinionKind.Turret:
                    SoundManager.Play("SFX_S13TurretDeploy", v, at, 0.3f);
                    break;
                case MinionKind.SummonHut:
                    SoundManager.Play("SFX_H06SpawnTowerDeploy", v, at, 0.3f);
                    break;
            }
        }

        /// <summary>유닛 공격 소리 — 전장 유닛이라 아주 작게 (같은 소리는 0.3초에 한 번)</summary>
        private static string AttackSound(MinionKind kind)
        {
            switch (kind)
            {
                case MinionKind.AxeOrc: return "SFX_S11OrcAxeAttack";
                case MinionKind.Dragon: return "SFX_S12DragonFireBreath";
                case MinionKind.Turret: return "SFX_S13TurretArrowFire";
                case MinionKind.SummonHut: return "SFX_H06SkeletonBoneRattle";
                default: return null;
            }
        }

        /// <summary>유닛 공격 연출 (서버가 공격 횟수를 올렸을 때)</summary>
        private void PlayAttackFx(UnitView view, int targetId)
        {
            Transform t = view.Go.transform;
            string sound = AttackSound(view.Kind);
            if (sound != null) SoundManager.Play(sound, SoundVolume.Unit, t.position, 0.3f);
            float k = ModelScale(view.Kind);
            bool hasTarget = TryGetTargetPosition(targetId, out Vector3 targetPos);
            switch (view.Kind)
            {
                case MinionKind.AxeOrc:
                    PlayUnitFx("VFX_Unit_AxeOrc_Attack", t.position + (t.forward * 1.1f + Vector3.up * 1.1f) * k, t.rotation, 1.2f, k / 1.5f); // 모델과 같이 커진다
                    break;
                case MinionKind.Dragon:
                {
                    Vector3 mouth = t.position + (t.forward * 1.6f + Vector3.up * 0.6f) * k;
                    Vector3 dir = hasTarget ? (targetPos + Vector3.up * 0.5f - mouth) : t.forward;
                    if (dir.sqrMagnitude < 1e-4f) dir = t.forward;
                    PlayUnitFx("VFX_Unit_Dragon_Attack", mouth, Quaternion.LookRotation(dir, Vector3.up), 1.6f, 3f); // 브레스 3배
                    if (hasTarget) PlayUnitFx("VFX_Unit_Dragon_Impact", targetPos + Vector3.up * 0.1f, Quaternion.identity, 1.5f, 2f);
                    break;
                }
                case MinionKind.Turret:
                    if (hasTarget)
                    {
                        Vector3 from = t.position + Vector3.up * (HpBarHeight(MinionKind.Turret) - 0.9f) * k;
                        FireShot(_arrowShotPool, from, targetPos + Vector3.up * 0.6f, targetId, 1.2f, 18f);
                    }
                    break;
                case MinionKind.SummonHut:
                    PlayUnitFx("VFX_Unit_Spawn", t.position + t.forward * 1.8f * k, Quaternion.identity, 1.5f);
                    break;
            }
        }

        /// <summary>유닛 · 구조물 · 플레이어 Id → 현재 표시 위치 (발 높이)</summary>
        private bool TryGetTargetPosition(int targetId, out Vector3 position)
        {
            position = default;
            if (targetId == 0) return false;
            if (_units.TryGetValue(targetId, out var uv) && uv.Go != null)
            {
                position = uv.Go.transform.position;
                return true;
            }
            if (TeamUtil.IsStructureId(targetId))
            {
                var sv = _structures[targetId - TeamUtil.FirstStructureId];
                if (sv == null || sv.Root == null) return false;
                position = sv.Root.position;
                return true;
            }
            if (TeamUtil.IsPlayerId(targetId) && _session.Events.Latest != null)
            {
                var team = TeamUtil.TeamOfPlayerId(targetId);
                position = _arena.ToWorld(_arena.PlayerPosition(team, _session.Events.Latest.Players[(int)team].WorldColumn), PlatformHeight);
                return true;
            }
            return false;
        }

        private void UpdateProjectiles(MatchSnapshot s)
        {
            for (int i = 0; i < s.ProjectileCount; i++)
            {
                ref var p = ref s.Projectiles[i];
                Vector3 pos = _arena.ToWorld(p.U, p.V, p.Height);
                var def = _session.Catalog.Get(p.SpellId);
                bool summon = IsFlyingSummon(def);
                if (!_projectiles.TryGetValue(p.Id, out var go))
                {
                    go = GetProjectilePool(p.SpellId).Get(pos, Quaternion.identity);
                    var trail = go.GetComponentInChildren<TrailRenderer>();
                    if (trail != null) trail.Clear();
                    if (summon) go.transform.localScale = Vector3.one * ModelScale(def.SummonKind);
                    else if (def != null && def.SpellId == SpellIds.S07_Gatling) go.transform.localScale = Vector3.one * gatlingShotScale;
                    else if (def != null && def.FromOffMap && !Mathf.Approximately(def.OffMapVisualScale, 1f)) go.transform.localScale = Vector3.one * def.OffMapVisualScale;
                    _projectiles.Add(p.Id, go);
                }
                if (def != null && def.Behavior == SpellBehaviorType.ArrowVolley) _arrows[p.Id] = pos;
                Vector3 next = Vector3.Lerp(go.transform.position, pos, 0.6f);
                Vector3 dir = next - go.transform.position;
                if (summon) dir.y = 0f; // 머리부터 떨어지지 않게 — 서서 날아온다
                if (dir.sqrMagnitude > 1e-6f) go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                go.transform.position = next;
            }

            _removeBuffer.Clear();
            foreach (var kv in _projectiles)
            {
                bool alive = false;
                for (int i = 0; i < s.ProjectileCount; i++)
                {
                    if (s.Projectiles[i].Id == kv.Key)
                    {
                        alive = true;
                        break;
                    }
                }
                if (!alive) _removeBuffer.Add(kv.Key);
            }
            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                int id = _removeBuffer[i];
                var go = _projectiles[id];
                if (go != null)
                {
                    // 화살은 도착한 자리 바닥에 꽂혀 잠시 남는다 (보간이 늦어 공중에서 사라져 보이지 않게)
                    if (_arrows.TryGetValue(id, out Vector3 last))
                    {
                        last.y = _arena.GroundY + 0.25f;
                        go.transform.position = last;
                        _debris.Add(new Debris { T = go.transform, Stuck = true, Start = Time.time, End = Time.time + 1.2f });
                    }
                    else go.SetActive(false);
                }
                _arrows.Remove(id);
                _projectiles.Remove(id);
            }
        }

        private void UpdateStructures(MatchSnapshot s)
        {
            for (int i = 0; i < _structures.Length; i++)
            {
                var view = _structures[i];
                if (view == null || view.Root == null) continue;
                ref var st = ref s.Structures[i];
                bool alive = st.Alive != 0;
                SetFill(view.HpFill, st.MaxHp > 0 ? (float)st.Hp / st.MaxHp : 0f);
                // 타워 발사 횟수가 늘었으면 총알 표시 (첫 스냅샷 · 경기 재시작 때는 기록만)
                if (st.IsNexus == 0 && st.ShotCount != view.LastShotCount)
                {
                    if (view.LastShotCount >= 0 && st.ShotCount > view.LastShotCount && alive) FireTowerShot(view, st.ShotTargetId);
                    view.LastShotCount = st.ShotCount;
                }
                // HP 0 → 전장에서 없애고 폭발 + 돌무더기 (경기 재시작 때 다시 세운다)
                if (alive != view.WasAlive)
                {
                    view.WasAlive = alive;
                    if (!alive)
                    {
                        PlayStructureDestroyFx(view);
                        if (st.IsNexus != 0) SoundManager.Play("SFX_NexusDestroyRubble", SoundVolume.Flow, view.Root.position, 0.5f);
                        else SoundManager.Play("SFX_TowerDestroy", SoundVolume.Flow * 0.7f, view.Root.position, 0.3f);
                    }
                    view.Root.gameObject.SetActive(alive);
                    SetActive(view.Rubble, !alive);
                    if (view.HpFill != null) view.HpFill.parent.gameObject.SetActive(alive);
                }
                // 넥서스 Lock 표시 (§19): 잠긴 동안 HP바를 회색으로
                if (view.HpFill != null)
                {
                    var r = view.HpFill.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = st.Locked != 0 ? LockedFill : NormalFill;
                }
                Billboard(view.HpFill);

                // S09 버프 상자 · H01 빙결
                bool buffed = (st.Flags & StructureFlags.Buffed) != 0;
                bool frozen = (st.Flags & StructureFlags.Frozen) != 0;
                SetActive(view.BuffFx, buffed);
                SetActive(view.FreezeFx, frozen);
                if (frozen != view.WasFrozen)
                {
                    view.WasFrozen = frozen;
                    var renderers = view.Root.GetComponentsInChildren<Renderer>();
                    if (frozen) ApplyTint(renderers, FrozenTint);
                    else for (int k = 0; k < renderers.Length; k++) renderers[k].SetPropertyBlock(null);
                }
            }
        }

        private void PlayStructureDestroyFx(StructureView view)
        {
            if (_vfx == null) return;
            Vector3 basePos = view.Rubble != null ? view.Rubble.transform.position : view.Root.position;
            _vfx.PlayNamed("VFX_S03_Fireball_Impact", basePos + Vector3.up * view.Size.y * 0.4f, Quaternion.identity, 3f,
                           Vector3.one * destroyFxScale * 0.5f);
            _vfx.PlayNamed("VFX_Unit_Death", basePos + Vector3.up * 0.5f, Quaternion.identity, 3f, Vector3.one * destroyFxScale);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private void FireTowerShot(StructureView tower, int targetId)
        {
            if (!_units.TryGetValue(targetId, out var target) || target.Go == null) return;
            Vector3 from = tower.Root.position + Vector3.up * tower.MuzzleHeight;
            Vector3 to = target.Go.transform.position + Vector3.up * 0.6f;
            FireShot(_shotPools[tower.Team == LocalTeam ? 0 : 1], from, to, targetId, 0f, towerShotSpeed);
            SoundManager.Play("SFX_TowerAttack", SoundVolume.Unit, from, 0.3f);
        }

        private void FireShot(ObjectPool pool, Vector3 from, Vector3 to, int targetId, float arc, float speed)
        {
            var go = pool.Get(from, Quaternion.LookRotation(to - from));
            var trail = go.GetComponentInChildren<TrailRenderer>();
            if (trail != null) trail.Clear();
            var particles = go.GetComponent<ParticleSystem>();
            if (particles != null)
            {
                particles.Clear(true); // 풀에서 재사용 — 지난 발사의 불꽃이 남지 않게
                particles.Play(true);
            }
            _shots.Add(new TowerShot
            {
                Go = go,
                Pool = pool,
                From = from,
                To = to,
                TargetId = targetId,
                Arc = arc,
                Duration = Mathf.Max(0.05f, Vector3.Distance(from, to) / Mathf.Max(1f, speed)),
            });
        }

        /// <summary>총알 · 화살은 대상 유닛을 따라가다 도착하면 사라진다 (대상이 먼저 사라지면 마지막 위치까지)</summary>
        private void UpdateTowerShots()
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var shot = _shots[i];
                shot.Elapsed += Time.deltaTime;
                if (_units.TryGetValue(shot.TargetId, out var target) && target.Go != null)
                    shot.To = target.Go.transform.position + Vector3.up * 0.6f;
                float k = Mathf.Clamp01(shot.Elapsed / shot.Duration);
                Vector3 pos = Vector3.Lerp(shot.From, shot.To, k) + Vector3.up * (shot.Arc * 4f * k * (1f - k));
                Vector3 dir = shot.To - shot.From + Vector3.up * (shot.Arc * 4f * (1f - 2f * k));
                shot.Go.transform.SetPositionAndRotation(pos, dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : Quaternion.identity);
                if (k < 1f) continue;
                shot.Pool.Release(shot.Go);
                _shots.RemoveAt(i);
            }
        }

        private static Material LockedFill => FallbackVisuals.Opaque(new Color(0.55f, 0.55f, 0.6f));

        private static Material NormalFill => FallbackVisuals.Opaque(new Color(0.3f, 1f, 0.3f));

        private void Billboard(Transform fill)
        {
            if (fill == null || _head == null) return;
            var bar = fill.parent;
            Vector3 d = bar.position - _head.position;
            if (d.sqrMagnitude > 1e-6f) bar.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        private void UpdatePlayers(MatchSnapshot s)
        {
            if (!_session.Events.HasLocalTeam) return;
            Team local = _session.Events.LocalTeam;

            for (int i = 0; i < _avatars.Count; i++)
            {
                var a = _avatars[i];
                ref var o = ref s.Players[(int)a.Team];
                bool show = o.Present != 0 && a.Team != local && (int)a.Team != _hiddenAvatar;
                if (a.Root.gameObject.activeSelf != show) a.Root.gameObject.SetActive(show);
                if (!show) continue;
                Vector3 target = _arena.ToWorld(_arena.PlayerPosition(a.Team, o.WorldColumn), PlatformHeight);
                a.Root.position = Vector3.Lerp(a.Root.position, target, 1f - Mathf.Exp(-20f * Time.deltaTime));
                a.Root.rotation = _arena.FacingRotation(a.Team);
                bool dead = o.Alive == 0;
                a.Root.localScale = dead ? new Vector3(AvatarScale, AvatarScale * 0.2f, AvatarScale) : Vector3.one * AvatarScale;
                SetActive(a.Shield, o.Shield > 0 && !dead);
                SetActive(a.Reflect, (o.Flags & PlayerFlags.Reflect) != 0 && !dead);
                SetActive(a.Focus, (o.Flags & PlayerFlags.Focus) != 0 && !dead);
                SetActive(a.Blind, (o.Flags & PlayerFlags.Blind) != 0 && !dead);
            }

            ref var me = ref s.Players[(int)local];
            Vector3 feet = _arena.ToWorld(_arena.PlayerPosition(local, me.WorldColumn), PlatformHeight);
            bool alive = me.Alive != 0;
            bool shield = me.Shield > 0 && alive;
            bool reflect = (me.Flags & PlayerFlags.Reflect) != 0 && alive;
            bool focus = (me.Flags & PlayerFlags.Focus) != 0 && alive;

            // 내 쉴드 · 결계: 몸을 감싸되 윗면이 눈높이보다 낮게 (시야 방해 금지)
            float eyeY = _head != null ? _head.position.y : feet.y + 1.7f;
            float topY = eyeY - selfBarrierBelowEyes;
            if (_selfShield != null)
            {
                SetActive(_selfShield, shield);
                if (shield)
                {
                    float radius = Mathf.Clamp((topY - feet.y) * 0.5f, 0.45f, 0.9f);
                    _selfShield.transform.SetPositionAndRotation(new Vector3(feet.x, topY - radius, feet.z), Quaternion.identity);
                    _selfShield.transform.localScale = Vector3.one * radius;
                }
            }
            else if (_selfShieldRing != null)
            {
                if (_selfShieldRing.gameObject.activeSelf != shield) _selfShieldRing.gameObject.SetActive(shield);
                if (shield) _selfShieldRing.position = feet + Vector3.up * 0.05f;
            }
            if (_selfReflect != null)
            {
                SetActive(_selfReflect, reflect);
                if (reflect)
                {
                    _selfReflect.transform.SetPositionAndRotation(feet, _arena.FacingRotation(local));
                    float h = Mathf.Max(0.4f, topY - feet.y) * 0.5f; // 프리즘 높이 2m 기준
                    _selfReflect.transform.localScale = new Vector3(0.9f, h, 0.9f);
                }
            }
            if (_selfFocus != null)
            {
                SetActive(_selfFocus, focus);
                if (focus) _selfFocus.transform.SetPositionAndRotation(feet + Vector3.up * 0.03f, Quaternion.identity);
            }
        }

        private static void SetFill(Transform fill, float ratio)
        {
            if (fill == null) return;
            ratio = Mathf.Clamp01(ratio);
            var sc = fill.localScale;
            float full = fill.parent != null && fill.parent.childCount > 0 ? fill.parent.GetChild(0).localScale.x : 1f;
            sc.x = full * ratio;
            fill.localScale = sc;
            var p = fill.localPosition;
            p.x = -(full - sc.x) * 0.5f;
            fill.localPosition = p;
        }
    }
}
