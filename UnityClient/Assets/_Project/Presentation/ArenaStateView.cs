// 서버 상태의 월드 표시 (§16 "클라이언트는 위치 보간 · 애니메이션 · VFX", §21.4)
//   · 미니언 · 투사체 · 구조물 · 상대 플레이어 아바타 · 발판을 스냅샷에서 읽어 그린다 (표시 전용, 판정 없음).
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
        }

        private sealed class StructureView
        {
            public Transform Root;
            public Transform HpFill;
            public bool WasAlive = true;
            public Vector3 OriginalScale;
        }

        public float unitLerpSpeed = 10f;
        public int meleePrewarm = 24;
        public int rangedPrewarm = 12;
        public int brutePrewarm = 4;
        public int projectilePrewarm = 24;

        private ClientSession _session;
        private VFXCatalog _catalog;
        private Transform _head;
        private ArenaGeometry _arena;
        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>(64);
        private readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>(64);
        private readonly List<int> _removeBuffer = new List<int>(64);
        private readonly ObjectPool[,] _unitPools = new ObjectPool[2, 3];
        private ObjectPool _projectilePool;
        private readonly StructureView[] _structures = new StructureView[4];
        private Transform _opponentAvatar;
        private Transform _opponentShield;
        private Transform _selfShieldRing;
        private MaterialPropertyBlock _block;
        private int _frame;

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
            _projectilePool = new ObjectPool(CreateProjectileVisual, poolRoot, projectilePrewarm, "Projectile");

            BuildStructures();
            BuildPlatforms();
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
            FallbackVisuals.ApplyTint(go, FallbackVisuals.TeamColor(team), _block);
            AttachHpBar(go.transform, kind == MinionKind.Brute ? 2.0f : 1.4f, 0.6f);
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
                        HpFill = AttachHpBar(holder, isNexus ? 3.2f : 3.8f, 1.6f),
                        OriginalScale = visual.localScale,
                    };
                    _structures[MatchSnapshot.StructureIndex(team, isNexus)] = view;
                }
            }
        }

        private void BuildPlatforms()
        {
            var root = new GameObject("[Commander Platforms]").transform;
            root.SetParent(transform, false);
            for (int t = 0; t < 2; t++)
            {
                var team = (Team)t;
                for (int c = 0; c < 3; c++)
                {
                    var pad = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "Pad_" + team + "_" + c,
                                                        FallbackVisuals.Transparent(FallbackVisuals.TeamColor(team) * new Color(1f, 1f, 1f, 0.35f)));
                    pad.transform.SetParent(root, false);
                    pad.transform.position = _arena.ToWorld(_arena.PlayerPosition(team, c), 0.02f);
                    pad.transform.localScale = new Vector3(1.6f, 0.01f, 1.6f);
                }
            }
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
            _selfShieldRing.localScale = new Vector3(2.2f, 0.01f, 2.2f);
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
                    view = new UnitView { Go = go, HpFill = go.transform.Find("HpBar/Fill"), Target = target };
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
                    go = _projectilePool.Get(pos, Quaternion.identity);
                    _projectiles.Add(p.Id, go);
                    var def = _session.Catalog.Get(p.SpellId);
                    var r = go.GetComponent<Renderer>();
                    if (r != null && def != null) r.sharedMaterial = FallbackVisuals.Opaque(def.ThemeColor);
                }
                go.transform.position = Vector3.Lerp(go.transform.position, pos, 0.6f);
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
                Vector3 target = _arena.ToWorld(_arena.PlayerPosition(opp, o.WorldColumn));
                _opponentAvatar.position = Vector3.Lerp(_opponentAvatar.position, target, 1f - Mathf.Exp(-20f * Time.deltaTime));
                _opponentAvatar.rotation = _arena.FacingRotation(opp);
                _opponentShield.gameObject.SetActive(o.Shield > 0);
                bool dead = o.Alive == 0;
                _opponentAvatar.localScale = dead ? new Vector3(1f, 0.2f, 1f) : Vector3.one;
            }

            ref var me = ref s.Players[(int)local];
            bool shield = me.Shield > 0;
            if (_selfShieldRing.gameObject.activeSelf != shield) _selfShieldRing.gameObject.SetActive(shield);
            if (shield) _selfShieldRing.position = _arena.ToWorld(_arena.PlayerPosition(local, me.WorldColumn), 0.05f);
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
