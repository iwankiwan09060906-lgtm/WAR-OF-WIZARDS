// 4인 개인전 전장 표시 — 씬 아트는 1:1 직사각형 전장이라, 개인전이면 실행 중에 마름모 전장으로 바꾼다.
//   · 씬의 벽 · 바닥 · 절벽 · 장식 · 1:1 레인을 끄고 (1번 · 3번 발판과 구조물은 그대로 쓴다)
//   · 2번 · 4번 발판을 1번 발판을 복제해 세운다 (TeamArt 가 보는 사람 기준 색을 입힌다)
//   · 바닥 원판 · 레인 6개(이중선 테두리) · 발판 번호판을 만든다
//   · 마나 폭풍이 시작되면 전장 가장자리 고리가 보라색으로 빛난다
// 레인 · 발판 위치는 ArenaGeometry 를 그대로 쓰므로 판정과 어긋나지 않는다 (표시 전용).

using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class FfaArenaView : MonoBehaviour
    {
        /// <summary>끌 씬 오브젝트 — 1:1 직사각형 전장 전용</summary>
        private static readonly string[] DuelOnly = { "Art_Walls", "Art_Ground", "Art_FarCliffs", "Art_Decor", "Art_Bridge", "Lanes", "Ground" };

        private static readonly Color Floor = new Color(0.16f, 0.15f, 0.22f);
        private static readonly Color Lane = new Color(0.30f, 0.28f, 0.40f);
        private static readonly Color Edge = new Color(0.55f, 0.45f, 0.95f);
        private static readonly Color StormColor = new Color(0.65f, 0.2f, 0.95f);

        private ClientSession _session;
        private ArenaGeometry _arena;
        private Transform _root;
        private Renderer _ring;
        private bool _stormShown;

        public void Initialize(ClientSession session)
        {
            _session = session;
            _arena = session.Arena;
            _root = new GameObject("[FFA Arena]").transform;
            _root.SetParent(transform, false);

            HideDuelArt();
            BuildFloor();
            for (int lane = 0; lane < _arena.LaneCount; lane++) BuildLane(lane);
            ClonePlatforms();
            for (int t = 0; t < _arena.TeamCount; t++) BuildSeatSign((Team)t);
        }

        private void HideDuelArt()
        {
            var arena = GameObject.Find("/Arena");
            if (arena == null) return;
            foreach (var name in DuelOnly)
            {
                var t = arena.transform.Find(name);
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        private void BuildFloor()
        {
            Vector3 center = _arena.ToWorld(_arena.Center, -0.06f);
            float radius = _arena.FieldLength * 0.5f + _arena.ColumnSpacing * 2.5f;

            var disc = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "Floor", FallbackVisuals.Opaque(Floor));
            disc.transform.SetParent(_root, false);
            disc.transform.position = center;
            disc.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);

            var ring = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "EdgeRing", FallbackVisuals.Transparent(new Color(Edge.r, Edge.g, Edge.b, 0.25f)));
            ring.transform.SetParent(_root, false);
            ring.transform.position = center + Vector3.down * 0.02f;
            ring.transform.localScale = new Vector3((radius + 0.6f) * 2f, 0.05f, (radius + 0.6f) * 2f);
            _ring = ring.GetComponent<Renderer>();

            // 대각선 레인이 만나는 가운데 표시
            var mid = FallbackVisuals.Primitive(PrimitiveType.Cylinder, "CenterMark", FallbackVisuals.Transparent(new Color(Edge.r, Edge.g, Edge.b, 0.35f)));
            mid.transform.SetParent(_root, false);
            mid.transform.position = _arena.ToWorld(_arena.Center, 0.04f);
            mid.transform.localScale = new Vector3(5f, 0.02f, 5f);
        }

        /// <summary>레인 한 줄 — 바닥 띠 + 그림처럼 이중선 테두리</summary>
        private void BuildLane(int lane)
        {
            var frame = _arena.GetLane(Team.Home, lane);
            var rotation = Quaternion.LookRotation(_arena.ToWorldDirection(frame.Dir), Vector3.up);
            float half = _arena.LaneHalfWidth;

            Strip("Lane" + lane, frame.Point(frame.Length * 0.5f), rotation, half * 2f, frame.Length, 0.0f, Lane, false);
            Strip("Lane" + lane + "_L", frame.Point(frame.Length * 0.5f, -half), rotation, 0.14f, frame.Length, 0.02f, Edge, true);
            Strip("Lane" + lane + "_R", frame.Point(frame.Length * 0.5f, half), rotation, 0.14f, frame.Length, 0.02f, Edge, true);
        }

        private void Strip(string name, Vector2 at, Quaternion rotation, float width, float length, float height, Color color, bool glow)
        {
            var material = glow ? FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.75f)) : FallbackVisuals.Opaque(color);
            var strip = FallbackVisuals.Primitive(PrimitiveType.Cube, name, material);
            strip.transform.SetParent(_root, false);
            strip.transform.SetPositionAndRotation(_arena.ToWorld(at, height), rotation);
            strip.transform.localScale = new Vector3(width, 0.02f, length);
        }

        /// <summary>1번 발판 세 칸을 2 · 4번 자리에 복제한다 (씬 아트가 없으면 판 하나씩 — 위치만 맞으면 된다)</summary>
        private void ClonePlatforms()
        {
            for (int t = 0; t < _arena.TeamCount; t++)
            {
                var team = (Team)t;
                if (team == Team.Home || team == Team.Away) continue;
                for (int c = 0; c < TeamUtil.ColumnCount; c++)
                {
                    Vector3 pos = _arena.ToWorld(_arena.PlayerPosition(team, c));
                    var rot = _arena.FacingRotation(team);
                    var source = GameObject.Find("/Arena/Home/Home_Commander_" + "LCR"[c]);
                    GameObject go;
                    if (source != null)
                    {
                        go = Instantiate(source, _root);
                        foreach (var art in go.GetComponentsInChildren<TeamArt>(true)) art.team = team;
                        go.transform.SetPositionAndRotation(pos, rot);
                    }
                    else
                    {
                        go = FallbackVisuals.Primitive(PrimitiveType.Cube, "Commander_" + team + c, FallbackVisuals.Opaque(FallbackVisuals.TeamColor(team)));
                        go.transform.SetParent(_root, false);
                        go.transform.SetPositionAndRotation(pos, rot);
                        go.transform.localScale = new Vector3(_arena.ColumnSpacing * 0.9f, 0.3f, _arena.ColumnSpacing * 0.9f);
                    }
                    go.name = "Commander_" + team + "_" + c;
                }
            }
        }

        /// <summary>발판 뒤 공중에 떠 있는 번호판 — 어느 쪽이 몇 번인지 전장 어디서나 보이게</summary>
        private void BuildSeatSign(Team team)
        {
            var sign = new GameObject("Seat" + TeamUtil.SeatNumber(team));
            sign.transform.SetParent(_root, false);
            sign.transform.position = _arena.ToWorld(_arena.BasePosition(team) - _arena.SeatForward(team) * 3f, 8f);
            sign.transform.rotation = Quaternion.LookRotation(_arena.ToWorldDirection(-_arena.SeatForward(team)), Vector3.up);
            var text = sign.AddComponent<TextMesh>();
            text.text = TeamUtil.SeatNumber(team).ToString();
            text.font = FallbackVisuals.DefaultFont;
            text.fontSize = 96;
            text.characterSize = 0.35f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = FallbackVisuals.TeamColor(team);
            sign.GetComponent<MeshRenderer>().sharedMaterial = FallbackVisuals.DefaultFont.material;
        }

        private void Update()
        {
            var s = _session != null ? _session.Events.Latest : null;
            if (s == null || _ring == null) return;
            bool storm = s.Header.Phase == (byte)Network.MatchPhase.Playing && s.Header.StormIn == 0f;
            if (storm == _stormShown) return;
            _stormShown = storm;
            Color c = storm ? StormColor : Edge;
            _ring.sharedMaterial = FallbackVisuals.Transparent(new Color(c.r, c.g, c.b, storm ? 0.7f : 0.25f));
        }
    }
}
