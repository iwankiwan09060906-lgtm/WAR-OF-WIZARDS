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

using System.Collections.Generic;
using SpellboundVR.Arena;
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
        }

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
        public float dragonFlightHeight = 3f;

        [Header("소환수 · 설치물 모델 크기 배율 (MDL_* 프리팹 기준)")]
        [Tooltip("S11 도끼 전사 — 미니언 사이에서 눈에 띄게 크게")]
        public float axeOrcScale = 1.5f;
        [Tooltip("S12 드래곤 — 날개 폭 6m 프리팹을 줄여서")]
        public float dragonScale = 0.55f;
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
        private readonly ObjectPool[,] _unitPools = new ObjectPool[2, MinionDefinition.KindCount];
        private readonly Dictionary<int, ObjectPool> _projectilePools = new Dictionary<int, ObjectPool>();
        private Transform _poolRoot;
        private readonly StructureView[] _structures = new StructureView[4];
        /// <summary>[0] = 아군 타워 총알, [1] = 적 타워 총알 (보는 사람 기준)</summary>
        private readonly ObjectPool[] _shotPools = new ObjectPool[2];
        private ObjectPool _arrowShotPool;
        private readonly List<TowerShot> _shots = new List<TowerShot>(16);
        private Transform _opponentAvatar;
        private GameObject _oppShield, _oppReflect, _oppFocus, _oppBlind;
        private GameObject _selfShield, _selfReflect, _selfFocus;
        private Transform _selfShieldRing;
        private MaterialPropertyBlock _block;
        private int _frame;
        private int _lastMatchSerial = -1;

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
                case MinionKind.Turret: return 5.6f;
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
            for (int t = 0; t < 2; t++)
            {
                var team = (Team)t;
                for (int k = 0; k < MinionDefinition.KindCount; k++)
                {
                    var kind = (MinionKind)k;
                    int prewarm = kind == MinionKind.Melee ? meleePrewarm
                                : kind == MinionKind.Ranged ? rangedPrewarm
                                : kind == MinionKind.Brute ? brutePrewarm
                                : kind == MinionKind.Skeleton ? skeletonPrewarm
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

            TeamArt.ApplyAll(LocalTeam);
            _session.Events.OnLocalTeamAssigned += TeamArt.ApplyAll;
            BuildStructures();
            BuildOpponentAvatar();
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
                             (_catalog.TryGet(def.GetVfxPrefabName(VfxPart.Projectile), out prefab) ||
                              // S05 토네이도: 레인을 따라 움직이는 회오리 = 장판(Field) 프리팹을 투사체처럼 이동
                              (def.Behavior == SpellBehaviorType.TornadoLane && _catalog.TryGet(def.GetVfxPrefabName(VfxPart.Field), out prefab)));
            if (hasPrefab)
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

        private void OnDestroy()
        {
            if (_session != null) _session.Events.OnLocalTeamAssigned -= TeamArt.ApplyAll;
        }

        /// <summary>진영 배정 전(오프라인 · 서버 화면)에는 Home 시점</summary>
        private Team LocalTeam => _session.Events.HasLocalTeam ? _session.Events.LocalTeam : Team.Home;

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
            var layout = _session.Layout;
            for (int t = 0; t < 2; t++)
            {
                var team = (Team)t;
                for (int n = 0; n < 2; n++)
                {
                    bool isNexus = n == 1;
                    Transform visual = layout != null ? layout.GetStructureVisual(team, isNexus) : null;
                    Vector3 pos = _arena.ToWorld(_arena.StructurePosition(team, isNexus));
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
                    // HP바는 비주얼의 스케일을 상속하지 않도록 별도 홀더에 붙인다
                    var holder = new GameObject("StructureHp_" + team + (isNexus ? "_Nexus" : "_Tower")).transform;
                    holder.SetParent(transform, false);
                    holder.position = new Vector3(visual.position.x, pos.y, visual.position.z);
                    var size = VisualSize(visual);
                    var view = new StructureView
                    {
                        Root = visual,
                        HpFill = AttachHpBar(holder, isNexus ? 3.2f : 5.4f, 1.6f), // 아트 높이: 넥서스 2.4m · 타워 4.8m
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
                    _structures[MatchSnapshot.StructureIndex(team, isNexus)] = view;
                }
            }
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

        private void BuildOpponentAvatar()
        {
            GameObject go;
            if (_catalog != null && _catalog.TryGet("MDL_Avatar_Wizard", out var prefab)) go = Instantiate(prefab);
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
            go.transform.localScale = Vector3.one * playerScale;
            _opponentAvatar = go.transform;

            // S08 쉴드 (구, 반경 1 프리팹 → 아바타 기준 1.3)
            _oppShield = CreateStatusFx("VFX_S08_Shield_Loop", _opponentAvatar);
            if (_oppShield == null)
            {
                _oppShield = FallbackVisuals.Primitive(PrimitiveType.Sphere, "OpponentShield",
                                                       FallbackVisuals.Transparent(new Color(0.35f, 0.75f, 1f, 0.25f)));
                _oppShield.transform.SetParent(_opponentAvatar, false);
                _oppShield.SetActive(false);
            }
            _oppShield.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            _oppShield.transform.localScale = Vector3.one * 1.3f;

            // H04 반사 결계 (반경 1 · 높이 2 프리팹)
            _oppReflect = CreateStatusFx("VFX_H04_ReflectBarrier_Loop", _opponentAvatar);
            if (_oppReflect != null) _oppReflect.transform.localScale = new Vector3(1.4f, 1.25f, 1.4f);
            // S10 집중 (발밑)
            _oppFocus = CreateStatusFx("VFX_S10_Focus_Loop", _opponentAvatar);
            // S01 연막에 맞은 상대 — 머리 주변 검은 연기
            _oppBlind = CreateStatusFx("VFX_S01_Smoke_Loop", _opponentAvatar);
            if (_oppBlind != null) _oppBlind.transform.localPosition = new Vector3(0f, 2.0f, 0f);
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
                    int team = Mathf.Clamp(u.Team, 0, 1);
                    var go = _unitPools[team, (int)kind].Get(target, _arena.FacingRotation((Team)team));
                    _modelRenderers.TryGetValue(go, out var renderers);
                    view = new UnitView
                    {
                        Go = go, HpFill = go.transform.Find("HpBar/Fill"), Target = target, Animator = FindStateAnimator(go),
                        Kind = kind, Team = (Team)team, LastAttackCount = u.AttackCount, ModelRenderers = renderers,
                    };
                    _units.Add(u.Id, view);
                    isNew = true;
                    if (IsSummonKind(kind)) PlayUnitFx("VFX_Unit_Spawn", _arena.ToWorld(u.U, u.V, 0.05f), Quaternion.identity, 2f);
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
                    Color c = tint == 1 ? new Color(0.55f, 0.85f, 1f) : (tint == 2 ? new Color(1f, 0.45f, 0.4f) : BaseTint(view.Team, kind));
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

        private void PlayUnitFx(string prefabName, Vector3 position, Quaternion rotation, float lifetime)
        {
            if (_vfx != null) _vfx.PlayNamed(prefabName, position, rotation, lifetime);
        }

        /// <summary>유닛 공격 연출 (서버가 공격 횟수를 올렸을 때)</summary>
        private void PlayAttackFx(UnitView view, int targetId)
        {
            Transform t = view.Go.transform;
            float k = ModelScale(view.Kind);
            bool hasTarget = TryGetTargetPosition(targetId, out Vector3 targetPos);
            switch (view.Kind)
            {
                case MinionKind.AxeOrc:
                    PlayUnitFx("VFX_Unit_AxeOrc_Attack", t.position + (t.forward * 1.1f + Vector3.up * 1.1f) * k, t.rotation, 1.2f);
                    break;
                case MinionKind.Dragon:
                {
                    Vector3 mouth = t.position + (t.forward * 1.6f + Vector3.up * 0.6f) * k;
                    Vector3 dir = hasTarget ? (targetPos + Vector3.up * 0.5f - mouth) : t.forward;
                    if (dir.sqrMagnitude < 1e-4f) dir = t.forward;
                    PlayUnitFx("VFX_Unit_Dragon_Attack", mouth, Quaternion.LookRotation(dir, Vector3.up), 1.6f);
                    if (hasTarget) PlayUnitFx("VFX_Unit_Dragon_Impact", targetPos + Vector3.up * 0.1f, Quaternion.identity, 1.5f);
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
                var team = targetId >= 12 ? Team.Away : Team.Home;
                bool nexus = (targetId - 10) % 2 == 1;
                var sv = _structures[MatchSnapshot.StructureIndex(team, nexus)];
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
                if (!_projectiles.TryGetValue(p.Id, out var go))
                {
                    go = GetProjectilePool(p.SpellId).Get(pos, Quaternion.identity);
                    var trail = go.GetComponentInChildren<TrailRenderer>();
                    if (trail != null) trail.Clear();
                    _projectiles.Add(p.Id, go);
                }
                Vector3 next = Vector3.Lerp(go.transform.position, pos, 0.6f);
                Vector3 dir = next - go.transform.position;
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
                var go = _projectiles[_removeBuffer[i]];
                if (go != null) go.SetActive(false);
                _projectiles.Remove(_removeBuffer[i]);
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
                    if (!alive) PlayStructureDestroyFx(view);
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
                    if (frozen) ApplyTint(renderers, new Color(0.6f, 0.85f, 1f));
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
            Team opp = TeamUtil.Opponent(local);

            ref var o = ref s.Players[(int)opp];
            bool show = o.Present != 0;
            if (_opponentAvatar.gameObject.activeSelf != show) _opponentAvatar.gameObject.SetActive(show);
            if (show)
            {
                Vector3 target = _arena.ToWorld(_arena.PlayerPosition(opp, o.WorldColumn), PlatformHeight);
                _opponentAvatar.position = Vector3.Lerp(_opponentAvatar.position, target, 1f - Mathf.Exp(-20f * Time.deltaTime));
                _opponentAvatar.rotation = _arena.FacingRotation(opp);
                bool dead = o.Alive == 0;
                _opponentAvatar.localScale = dead ? new Vector3(playerScale, playerScale * 0.2f, playerScale) : Vector3.one * playerScale;
                SetActive(_oppShield, o.Shield > 0 && !dead);
                SetActive(_oppReflect, (o.Flags & PlayerFlags.Reflect) != 0 && !dead);
                SetActive(_oppFocus, (o.Flags & PlayerFlags.Focus) != 0 && !dead);
                SetActive(_oppBlind, (o.Flags & PlayerFlags.Blind) != 0 && !dead);
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
