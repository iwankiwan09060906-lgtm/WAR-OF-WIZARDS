// 다인전 HUD (C12 안) — BattleHudView 가 다인전이면 FfaHudPanel 대신 만든다.
//   · 상대 카드 최대 5장: 왼쪽 이웃 → 건너편 → 오른쪽 이웃 순서 (가리키는 방향 순서와 같다).
//     번호 · 이름(AI 는 AI) · HP 막대 · 넥서스 · "이웃"(소환할 수 있는 상대) · 탈락 순위
//   · 전장 지도: 지금 정N각형 — 변(이웃 레인)은 진하게, 대각선(직접 공격 길)은 옅게, 넥서스 · 유닛 점. 내 자리가 아래.
//     탈락으로 링이 바뀌면 다시 그린다. 2명이면 1:1 레인 3개.
// 스냅샷만 읽어 그린다 (표시 전용). 초당 몇 번만 갱신한다. 관전 중에는 보고 있는 사람 기준 (Tick 의 me).

using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class PolygonHudPanel
    {
        private const int MaxCards = TeamUtil.TeamCount - 1;
        private const float CardWidth = 98f;
        private const float MapRadius = 72f;
        private const int MaxDots = 64;
        private const int MaxLines = 18;
        private const float RefreshInterval = 0.2f;

        private struct EnemyCard
        {
            public GameObject Root;
            public Image Back;
            public Text Title;
            public Text Hp;
            public RectTransform Bar;
            public Image Nexus;
            public Text Status;
        }

        private readonly ClientSession _session;
        private readonly EnemyCard[] _cards = new EnemyCard[MaxCards];
        private readonly Image[] _nodes = new Image[TeamUtil.TeamCount];
        private readonly Image[] _nexus = new Image[TeamUtil.TeamCount];
        private readonly Image[] _lines = new Image[MaxLines];
        private readonly Image[] _dots = new Image[MaxDots];
        private RectTransform _map;
        private float _nextRefresh;
        private Team _builtFor = (Team)255;
        private int _builtMask = -1;

        public PolygonHudPanel(RectTransform hud, ClientSession session)
        {
            _session = session;
            for (int c = 0; c < _cards.Length; c++) _cards[c] = BuildCard(hud, c);
            BuildMap(hud);
        }

        private static EnemyCard BuildCard(RectTransform hud, int index)
        {
            // HUD 오른쪽 위 (1:1 의 상대 카드 · 상대 방어 자리)
            var pos = new Vector2(12f + (CardWidth + 6f) * (index + 0.5f), 232f);
            var card = new EnemyCard();
            var back = UiKit.Panel(hud, Color.white, null, "Enemy" + index);
            back.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0.45f), UiTheme.Alpha(UiTheme.Enemy, 0.45f), 0, 2);
            back.type = Image.Type.Sliced;
            Place(back.rectTransform, pos, new Vector2(CardWidth, 134f));
            card.Root = back.gameObject;
            card.Back = back;
            var rt = back.rectTransform;
            card.Title = Label(rt, new Vector2(0f, 48f), new Vector2(CardWidth - 8f, 24f), 13, TextAnchor.MiddleCenter, UiTheme.Hex(0xf87171));
            card.Hp = Label(rt, new Vector2(0f, 24f), new Vector2(CardWidth - 8f, 22f), 15, TextAnchor.MiddleCenter, UiTheme.Text);

            var track = UiKit.Panel(rt, UiTheme.Alpha(UiTheme.Enemy, 0.15f), null, "Track");
            Place(track.rectTransform, new Vector2(0f, 4f), new Vector2(CardWidth - 12f, 9f));
            var fill = UiKit.Panel(track.rectTransform, UiTheme.Enemy, null, "Fill").rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            card.Bar = fill;

            card.Nexus = UiKit.Panel(rt, Color.white, null, "Nexus");
            card.Nexus.sprite = UiTheme.Cut(new Color(1f, 1f, 1f, 0.16f), Color.white, 0, 2);
            card.Nexus.type = Image.Type.Sliced;
            Place(card.Nexus.rectTransform, new Vector2(0f, -20f), new Vector2(CardWidth - 16f, 22f));
            Label(card.Nexus.rectTransform, Vector2.zero, new Vector2(CardWidth - 16f, 22f), 12, TextAnchor.MiddleCenter, Color.white).text = "넥서스";
            card.Status = Label(rt, new Vector2(0f, -48f), new Vector2(CardWidth - 6f, 22f), 12, TextAnchor.MiddleCenter, UiTheme.Text2);
            return card;
        }

        private void BuildMap(RectTransform hud)
        {
            var back = UiKit.Panel(hud, Color.white, null, "Map");
            back.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0.5f), UiTheme.Alpha(UiTheme.Purple, 0.4f), 8, 2);
            back.type = Image.Type.Sliced;
            Place(back.rectTransform, new Vector2(-395f, -218f), new Vector2(190f, 170f));
            _map = back.rectTransform;
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i] = UiKit.Panel(_map, Color.white, null, "Lane" + i);
                _lines[i].rectTransform.pivot = new Vector2(0f, 0.5f);
                _lines[i].rectTransform.anchorMin = _lines[i].rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _lines[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < _dots.Length; i++)
            {
                _dots[i] = UiKit.Panel(_map, Color.white, null, "Dot");
                _dots[i].sprite = UiTheme.Circle();
                Place(_dots[i].rectTransform, Vector2.zero, new Vector2(6f, 6f));
                _dots[i].gameObject.SetActive(false);
            }
            for (int t = 0; t < _nodes.Length; t++)
            {
                _nexus[t] = UiKit.Panel(_map, Color.white, null, "Nexus" + t);
                Place(_nexus[t].rectTransform, Vector2.zero, new Vector2(9f, 9f));
                _nodes[t] = UiKit.Panel(_map, FallbackVisuals.TeamColor((Team)t), null, "Seat" + t);
                _nodes[t].sprite = UiTheme.Circle();
                Place(_nodes[t].rectTransform, Vector2.zero, new Vector2(24f, 24f));
                Label(_nodes[t].rectTransform, Vector2.zero, new Vector2(24f, 24f), 15, TextAnchor.MiddleCenter, Color.black).text =
                    TeamUtil.SeatNumber((Team)t).ToString();
            }
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static Text Label(RectTransform parent, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor, Color color)
        {
            var t = UiKit.Label(parent, "", fontSize, color, true, anchor);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(t.rectTransform, pos, size);
            return t;
        }

        /// <summary>내 시점 지도 좌표 — 내 자리가 아래, 내가 보는 쪽(가운데)이 위</summary>
        private Vector2 MapPoint(Vector2 arenaPoint, Team me)
        {
            var arena = _session.Arena;
            Vector2 d = arenaPoint - arena.Center;
            float scale = MapRadius / (arena.FieldLength * 0.5f);
            return new Vector2(Vector2.Dot(d, arena.SeatRight(me)), Vector2.Dot(d, arena.SeatForward(me))) * scale;
        }

        private void LayoutMap(Team me)
        {
            var arena = _session.Arena;
            int used = 0;
            for (int lane = 0; lane < arena.LaneCount && used < _lines.Length; lane++)
            {
                if (!arena.LaneInUse(lane)) continue;
                var frame = arena.GetLane(Team.Home, lane);
                Vector2 a = MapPoint(frame.Origin, me), b = MapPoint(frame.Point(frame.Length), me);
                bool edge = arena.IsSpawnLane(arena.LaneTarget(Team.Home, lane), lane);
                var line = _lines[used++];
                line.color = UiTheme.Alpha(edge ? UiTheme.PurpleLight : UiTheme.Cyan, edge ? 0.7f : 0.22f);
                var rt = line.rectTransform;
                rt.anchoredPosition = a;
                rt.sizeDelta = new Vector2((b - a).magnitude, edge ? 3f : 1.5f);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                line.gameObject.SetActive(true);
            }
            for (int i = used; i < _lines.Length; i++) _lines[i].gameObject.SetActive(false);
            for (int t = 0; t < _nodes.Length; t++)
            {
                var team = (Team)t;
                bool inRing = arena.HasTeam(team);
                _nodes[t].gameObject.SetActive(inRing);
                _nexus[t].gameObject.SetActive(inRing);
                if (!inRing) continue;
                _nodes[t].rectTransform.anchoredPosition = MapPoint(arena.BasePosition(team), me);
                _nexus[t].rectTransform.anchoredPosition = MapPoint(arena.StructurePosition(team, true), me);
            }
        }

        public void Tick(MatchSnapshot s, Team me)
        {
            if (s == null || Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + RefreshInterval;
            var arena = _session.Arena;
            if (_builtFor != me || _builtMask != arena.RingMask)
            {
                _builtFor = me;
                _builtMask = arena.RingMask;
                LayoutMap(me);
            }

            // 카드: 링에서 나부터 시계 방향 1 ~ N-1번째 = 왼쪽 이웃 → 오른쪽 이웃 (내가 링에 없으면 0번 자리 다음부터)
            int n = arena.RingCount, mine = arena.RingIndex(me), shown = 0;
            for (int step = 1; step < n && shown < _cards.Length; step++)
            {
                Team enemy = arena.RingTeam((mine < 0 ? 0 : mine) + step);
                if (enemy == me) continue;
                PaintCard(ref _cards[shown++], s, enemy, step == 1 || step == n - 1, step == 1 ? "◀ " : step == n - 1 ? "▶ " : "▲ ");
            }
            for (int i = shown; i < _cards.Length; i++)
                if (_cards[i].Root.activeSelf) _cards[i].Root.SetActive(false);

            for (int t = 0; t < _nodes.Length; t++)
            {
                if (!_nodes[t].gameObject.activeSelf) continue;
                Color c = FallbackVisuals.TeamColor((Team)t);
                _nodes[t].rectTransform.localScale = Vector3.one * ((Team)t == me ? 1.25f : 1f);
                _nexus[t].color = s.Structure((Team)t, true).Alive != 0 ? c : UiTheme.Alpha(Color.white, 0.12f);
            }

            int dots = 0;
            for (int i = 0; i < s.UnitCount && dots < MaxDots; i++)
            {
                ref var u = ref s.Units[i];
                var dot = _dots[dots++];
                dot.rectTransform.anchoredPosition = MapPoint(new Vector2(u.U, u.V), me);
                dot.color = FallbackVisuals.TeamColor((Team)Mathf.Clamp(u.Team, 0, TeamUtil.TeamCount - 1));
                if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
            }
            for (int i = dots; i < MaxDots; i++)
                if (_dots[i].gameObject.activeSelf) _dots[i].gameObject.SetActive(false);
        }

        private static void PaintCard(ref EnemyCard card, MatchSnapshot s, Team enemy, bool neighbor, string side)
        {
            if (!card.Root.activeSelf) card.Root.SetActive(true);
            ref var p = ref s.Players[(int)enemy];
            string name = !string.IsNullOrEmpty(s.Names[(int)enemy]) ? s.Names[(int)enemy] : p.IsBot != 0 ? "AI" : "";
            card.Title.text = side + TeamUtil.SeatNumber(enemy) + "번 " + name;
            card.Title.color = FallbackVisuals.TeamColor(enemy);
            card.Hp.text = p.Hp + " / " + p.MaxHp;
            card.Bar.anchorMax = new Vector2(p.MaxHp > 0 ? Mathf.Clamp01((float)p.Hp / p.MaxHp) : 0f, 1f);
            card.Bar.offsetMax = Vector2.zero;
            bool nexus = s.Structure(enemy, true).Alive != 0;
            card.Nexus.color = nexus ? UiTheme.Cyan : UiTheme.Enemy;
            card.Back.color = Color.white;
            // 이웃 = 소환 · 웨이브가 가는 상대. 넥서스가 무너졌으면 내 유닛이 본체를 친다
            card.Status.text = neighbor ? (nexus ? "이웃 · 소환 가능" : "<color=" + UiKit.Hex(UiTheme.Amber) + ">이웃 · 돌파!</color>")
                                        : "건너편 · 공격만";
        }
    }
}
