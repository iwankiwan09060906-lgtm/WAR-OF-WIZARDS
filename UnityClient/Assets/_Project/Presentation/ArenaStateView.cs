// 서버 상태의 월드 표시 (§16 "클라이언트는 위치 보간 · 애니메이션 · VFX", §21.4)
//   · 미니언 · 투사체 · 구조물 · 상대 플레이어 아바타 · 타워 총알을 스냅샷에서 읽어 그린다 (표시 전용, 판정 없음).
//   · Commander 발판은 씬 아트(scaffold)가 담당한다 — 코드가 그리던 팀 색 패드는 없앰.
//   · 유닛 위치는 매 프레임 목표 위치로 보간한다.
//   · 아트 프리팹(MDL_*)이 카탈로그에 있으면 쓰고, 없으면 캡슐 · 큐브 대체 비주얼을 쓴다 (§25.4).
//   · 전투 중 생성 · 파괴 대신 풀을 쓴다 (§17).

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Pooling;
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
        }

        /// <summary>Animator int 파라미터: 0 이동 · 1 공격 · 2 돌파(이동) · 3 빙결 (UnitVisualState)</summary>
        private static readonly int AnimStateHash = Animator.StringToHash("State");

        private sealed class StructureView
        {
            public Transform Root;
            public Transform HpFill;
            public bool WasAlive = true;
            public Vector3 OriginalScale;
            public Team Team;
            /// <summary>총알이 나가는 높이 (비주얼 윗면의 80%)</summary>
            public float MuzzleHeight;
            public int LastShotCount = -1;
        }

        /// <summary>타워 총알 (표시 전용 — 피해는 서버가 발사 순간 이미 줬다)</summary>
        private sealed class TowerShot
        {
            public GameObject Go;
            public ObjectPool Pool;
            public Vector3 From;
            public Vector3 To;
            public int TargetId;
            public float Elapsed;
            public float Duration;
        }

        public float unitLerpSpeed = 10f;
        public int meleePrewarm = 24;
        public int rangedPrewarm = 20;
        public int brutePrewarm = 4;
        public int projectilePrewarm = 24;
        [Tooltip("플레이어 크기 배율 — 상대 아바타 · 쉴드 링 (눈높이는 OVRCameraRig 높이)")]
        public float playerScale = 2f;
        [Tooltip("타워 총알 속도 (m/s)")]
        public float towerShotSpeed = 30f;

        private ClientSession _session;
        private VFXCatalog _catalog;
        private Transform _head;
        private ArenaGeometry _arena;
        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>(64);
        private readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>(64);
        private readonly List<int> _removeBuffer = new List<int>(64);
        private readonly ObjectPool[,] _unitPools = new ObjectPool[2, 3];
        private readonly Dictionary<int, ObjectPool> _projectilePools = new Dictionary<int, ObjectPool>();
        private Transform _poolRoot;
        private readonly StructureView[] _structures = new StructureView[4];
        private readonly ObjectPool[] _shotPools = new ObjectPool[2];
        private readonly List<TowerShot> _shots = new List<TowerShot>(16);
        private Transform _opponentAvatar;
        private Transform _opponentShield;
        private Transform _selfShieldRing;
        private MaterialPropertyBlock _block;
        private int _frame;

        /// <summary>Commander 발판 윗면 높이 (ArenaLayout.commanderPlatformHeight)</summary>
        private float PlatformHeight => _session.Layout != null ? _session.Layout.commanderPlatformHeight : 0f;

        public void Initialize(ClientSession session, VFXCatalog catalog, Transform head)
        {
            _session = session;
            _catalog = catalog;
            _head = head;
            _arena = session.Arena;
            _block = new MaterialPropertyBlock();

            var poolRoot = new GameObject("[Unit Pools]").transform;
            poolRoot.SetParent(transform, false);
            for (int t = 0; t < 2; t++)
            {
                var team = (Team)t;
                _unitPools[t, 0] = new ObjectPool(() => CreateUnitVisual(team, MinionKind.Melee), poolRoot, meleePrewarm, "Unit_" + team + "_Melee");
                _unitPools[t, 1] = new ObjectPool(() => CreateUnitVisual(team, MinionKind.Ranged), poolRoot, rangedPrewarm, "Unit_" + team + "_Ranged");
                _unitPools[t, 2] = new ObjectPool(() => CreateUnitVisual(team, MinionKind.Brute), poolRoot, brutePrewarm, "Unit_" + team + "_Brute");
            }
            _poolRoot = poolRoot;
            for (int t = 0; t < 2; t++)
            {
                var team = (Team)t;
                _shotPools[t] = new ObjectPool(() => CreateTowerShotVisual(team), poolRoot, 6, "TowerShot_" + team);
            }

            BuildStructures();
            BuildOpponentAvatar();
        }

        // ── 생성 ─────────────────────────────────────────────

        private GameObject CreateUnitVisual(Team team, MinionKind kind)
        {
            string modelName = kind == MinionKind.Brute ? "MDL_Minion_Brute" : (kind == MinionKind.Ranged ? "MDL_Minion_Ranged" : "MDL_Minion_Melee");
            GameObject go;
            if (_catalog != null && _catalog.TryGet(modelName, out var prefab))
            {
                go = Instantiate(prefab);
            }
            else
            {
                go = new GameObject(modelName);
                var body = FallbackVisuals.Primitive(kind == MinionKind.Ranged ? PrimitiveType.Cylinder : PrimitiveType.Capsule, "Body",
                                                     FallbackVisuals.Opaque(FallbackVisuals.TeamColor(team)));
                body.transform.SetParent(go.transform, false);
                float s = kind == MinionKind.Brute ? 1.0f : (kind == MinionKind.Ranged ? 0.45f : 0.55f);
                body.transform.localScale = new Vector3(s, s * (kind == MinionKind.Ranged ? 0.6f : 0.75f), s);
                body.transform.localPosition = new Vector3(0f, s * 0.75f, 0f);
            }
            FallbackVisuals.ApplyTint(go, FallbackVisuals.LightTeamColor(team), _block);
            AttachHpBar(go.transform, kind == MinionKind.Brute ? 2.4f : 1.4f, 0.6f);
            return go;
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
            if (def != null && _catalog != null && _catalog.TryGet(def.GetVfxPrefabName(VfxPart.Projectile), out var prefab))
            {
                pool = new ObjectPool(prefab, _poolRoot, projectilePrewarm);
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

        /// <summary>타워 총알: 작은 발광 구체 + 짧은 꼬리 (팀 색을 연하게)</summary>
        private static GameObject CreateTowerShotVisual(Team team)
        {
            Color c = Color.Lerp(FallbackVisuals.LightTeamColor(team), new Color(1f, 0.95f, 0.6f), 0.5f);
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
                    var view = new StructureView
                    {
                        Root = visual,
                        HpFill = AttachHpBar(holder, isNexus ? 3.2f : 5.4f, 1.6f), // 아트 높이: 넥서스 2.4m · 타워 4.8m
                        OriginalScale = visual.localScale,
                        Team = team,
                        MuzzleHeight = VisualHeight(visual) * 0.8f,
                    };
                    _structures[MatchSnapshot.StructureIndex(team, isNexus)] = view;
                }
            }
        }

        /// <summary>비주얼 피벗(바닥)에서 렌더러 윗면까지 높이. 렌더러가 없으면 2m</summary>
        private static float VisualHeight(Transform visual)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 2f;
            float top = float.MinValue;
            for (int i = 0; i < renderers.Length; i++) top = Mathf.Max(top, renderers[i].bounds.max.y);
            return Mathf.Max(0.5f, top - visual.position.y);
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

            _opponentShield = FallbackVisuals.Primitive(PrimitiveType.Sphere, "OpponentShield",
                                                        FallbackVisuals.Transparent(new Color(0.35f, 0.75f, 1f, 0.25f))).transform;
            _opponentShield.SetParent(_opponentAvatar, false);
            _opponentShield.localPosition = new Vector3(0f, 1.1f, 0f);
            _opponentShield.localScale = Vector3.one * 2.0f;
            _opponentShield.gameObject.SetActive(false);

            _selfShieldRing = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "SelfShieldRing",
                                                        FallbackVisuals.Transparent(new Color(0.35f, 0.75f, 1f, 0.35f))).transform;
            _selfShieldRing.SetParent(transform, false);
            _selfShieldRing.localScale = new Vector3(2.2f * playerScale, 0.01f, 2.2f * playerScale);
            _selfShieldRing.gameObject.SetActive(false);
        }

        // ── 갱신 ─────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_session == null) return;
            var s = _session.Events.Latest;
            if (s == null) return;
            _frame++;

            UpdateUnits(s);
            UpdateProjectiles(s);
            UpdateStructures(s);
            UpdateTowerShots();
            UpdatePlayers(s);
        }

        private void UpdateUnits(MatchSnapshot s)
        {
            float lerp = 1f - Mathf.Exp(-unitLerpSpeed * Time.deltaTime);
            for (int i = 0; i < s.UnitCount; i++)
            {
                ref var u = ref s.Units[i];
                Vector3 target = _arena.ToWorld(u.U, u.V);
                if (!_units.TryGetValue(u.Id, out var view))
                {
                    int team = Mathf.Clamp(u.Team, 0, 1);
                    int kind = Mathf.Clamp(u.Kind, 0, 2);
                    var go = _unitPools[team, kind].Get(target, _arena.FacingRotation((Team)team));
                    view = new UnitView { Go = go, HpFill = go.transform.Find("HpBar/Fill"), Target = target, Animator = FindStateAnimator(go) };
                    _units.Add(u.Id, view);
                }
                view.SeenFrame = _frame;
                Vector3 prev = view.Go.transform.position;
                Vector3 next = Vector3.Lerp(prev, target, lerp);
                Vector3 move = next - prev;
                move.y = 0f;
                if (move.sqrMagnitude > 1e-6f) view.Go.transform.rotation = Quaternion.LookRotation(move, Vector3.up);
                view.Go.transform.position = next;
                SetFill(view.HpFill, u.MaxHp > 0 ? (float)u.Hp / u.MaxHp : 0f);
                Billboard(view.HpFill);
                if (view.Animator != null && view.LastState != u.State)
                {
                    view.LastState = u.State;
                    view.Animator.SetInteger(AnimStateHash, u.State);
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
                if (view.Go != null) view.Go.SetActive(false);
                _units.Remove(_removeBuffer[i]);
            }
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
                if (alive != view.WasAlive)
                {
                    view.WasAlive = alive;
                    view.Root.localScale = alive ? view.OriginalScale : new Vector3(view.OriginalScale.x, view.OriginalScale.y * 0.15f, view.OriginalScale.z);
                    if (view.HpFill != null) view.HpFill.parent.gameObject.SetActive(alive);
                }
                // 넥서스 Lock 표시 (§19): 잠긴 동안 HP바를 회색으로
                if (view.HpFill != null)
                {
                    var r = view.HpFill.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = st.Locked != 0 ? LockedFill : NormalFill;
                }
                Billboard(view.HpFill);
            }
        }

        private void FireTowerShot(StructureView tower, int targetId)
        {
            if (!_units.TryGetValue(targetId, out var target) || target.Go == null) return;
            Vector3 from = tower.Root.position + Vector3.up * tower.MuzzleHeight;
            Vector3 to = target.Go.transform.position + Vector3.up * 0.6f;
            var pool = _shotPools[(int)tower.Team];
            var go = pool.Get(from, Quaternion.LookRotation(to - from));
            var trail = go.GetComponent<TrailRenderer>();
            if (trail != null) trail.Clear();
            _shots.Add(new TowerShot
            {
                Go = go,
                Pool = pool,
                From = from,
                To = to,
                TargetId = targetId,
                Duration = Mathf.Max(0.05f, Vector3.Distance(from, to) / Mathf.Max(1f, towerShotSpeed)),
            });
        }

        /// <summary>총알은 대상 유닛을 따라가다 도착하면 사라진다 (대상이 먼저 사라지면 마지막 위치까지)</summary>
        private void UpdateTowerShots()
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var shot = _shots[i];
                shot.Elapsed += Time.deltaTime;
                if (_units.TryGetValue(shot.TargetId, out var target) && target.Go != null)
                    shot.To = target.Go.transform.position + Vector3.up * 0.6f;
                float k = Mathf.Clamp01(shot.Elapsed / shot.Duration);
                Vector3 pos = Vector3.Lerp(shot.From, shot.To, k);
                Vector3 dir = shot.To - shot.From;
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
                _opponentShield.gameObject.SetActive(o.Shield > 0);
                bool dead = o.Alive == 0;
                _opponentAvatar.localScale = dead ? new Vector3(playerScale, playerScale * 0.2f, playerScale) : Vector3.one * playerScale;
            }

            ref var me = ref s.Players[(int)local];
            bool shield = me.Shield > 0;
            if (_selfShieldRing.gameObject.activeSelf != shield) _selfShieldRing.gameObject.SetActive(shield);
            if (shield) _selfShieldRing.position = _arena.ToWorld(_arena.PlayerPosition(local, me.WorldColumn), PlatformHeight + 0.05f);
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
