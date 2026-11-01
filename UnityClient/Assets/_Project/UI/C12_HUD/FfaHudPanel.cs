// 4인 개인전 HUD (C12 안) — BattleHudView 가 개인전이면 "상대" 카드 대신 만든다.
//   · 세 상대 카드: 내 왼쪽 · 맞은편 · 오른쪽 순서 (= 왼쪽 · 가운데 · 오른쪽 레인으로 조준하면 맞는 상대).
//     번호 · 이름(AI 는 AI) · HP 막대 · 타워/넥서스 · 탈락 순위
//   · 전장 지도: 마름모 네 발판 · 레인 6개 · 유닛 점. 내 발판이 항상 아래, 내가 보는 쪽이 위
// 스냅샷만 읽어 그린다 (표시 전용, Rule 12). 초당 몇 번만 갱신한다.

using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class FfaHudPanel
    {
        private const float CardWidth = 158f;
        private const float MapRadius = 72f;
        private const int MaxDots = 64;
        private const float RefreshInterval = 0.2f;

        private struct EnemyCard
        {
            public Text Title;
            public Text Hp;
            public Text Status;
            public RectTransform Bar;
            public Image Tower;
            public Image Nexus;
            public Image Back;
        }

        private readonly ClientSession _session;
        private readonly EnemyCard[] _cards = new EnemyCard[TeamUtil.ColumnCount];
        private readonly Image[] _nodes = new Image[TeamUtil.TeamCount];
        private readonly Text[] _nodeLabels = new Text[TeamUtil.TeamCount];
        private readonly Image[] _edges = new Image[6];
        private readonly Image[] _dots = new Image[MaxDots];
        private RectTransform _map;
        private float _nextRefresh;
        private Team _builtFor = (Team)255;

        public FfaHudPanel(RectTransform hud, ClientSession session)
        {
            _session = session;
            for (int c = 0; c < _cards.Length; c++) _cards[c] = BuildCard(hud, c);
            BuildMap(hud);
        }

        private static EnemyCard BuildCard(RectTransform hud, int column)
        {
            // HUD 오른쪽 위 (1:1 의 상대 카드 · 상대 방어 자리)
            var pos = new Vector2(10f + (CardWidth + 8f) * (column + 0.5f), 232f);
            var card = new EnemyCard();
            var back = UiKit.Panel(hud, Color.white, null, "Enemy" + column);
            back.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0.45f), UiTheme.Alpha(UiTheme.Enemy, 0.45f), 0, 2);
            back.type = Image.Type.Sliced;
            Place(back.rectTransform, pos, new Vector2(CardWidth, 134f));
            card.Back = back;
            var rt = back.rectTransform;

            card.Title = Label(rt, new Vector2(0f, 46f), new Vector2(CardWidth - 12f, 26f), 15, TextAnchor.MiddleLeft, UiTheme.Hex(0xf87171));
            card.Hp = Label(rt, new Vector2(0f, 20f), new Vector2(CardWidth - 12f, 24f), 19, TextAnchor.MiddleLeft, UiTheme.Text);

            var track = UiKit.Panel(rt, UiTheme.Alpha(UiTheme.Enemy, 0.15f), null, "Track");
            Place(track.rectTransform, new Vector2(0f, -2f), new Vector2(CardWidth - 16f, 10f));
            var fill = UiKit.Panel(track.rectTransform, UiTheme.Enemy, null, "Fill").rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            card.Bar = fill;

            card.Tower = Pill(rt, new Vector2(-36f, -26f), "타워");
            card.Nexus = Pill(rt, new Vector2(36f, -26f), "넥서스");
            card.Status = Label(rt, new Vector2(0f, -50f), new Vector2(CardWidth - 12f, 22f), 14, TextAnchor.MiddleCenter, UiTheme.Text2);
            return card;
        }

        private static Image Pill(RectTransform parent, Vector2 pos, string text)
        {
            var img = UiKit.Panel(parent, Color.white, null, "Pill");
            img.sprite = UiTheme.Cut(new Color(1f, 1f, 1f, 0.16f), Color.white, 0, 2);
            img.type = Image.Type.Sliced;
            Place(img.rectTransform, pos, new Vector2(66f, 22f));
            var t = Label(img.rectTransform, Vector2.zero, new Vector2(66f, 22f), 13, TextAnchor.MiddleCenter, Color.white);
            t.text = text;
            return img;
        }

        private void BuildMap(RectTransform hud)
        {
            var back = UiKit.Panel(hud, Color.white, null, "Map");
            back.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0.5f), UiTheme.Alpha(UiTheme.Purple, 0.4f), 8, 2);
            back.type = Image.Type.Sliced;
            Place(back.rectTransform, new Vector2(-395f, -218f), new Vector2(190f, 170f));
            _map = back.rectTransform;
            for (int i = 0; i < _edges.Length; i++)
            {
                _edges[i] = UiKit.Panel(_map, UiTheme.Alpha(UiTheme.PurpleLight, 0.55f), null, "Lane" + i);
                _edges[i].rectTransform.pivot = new Vector2(0f, 0.5f);
                _edges[i].rectTransform.anchorMin = _edges[i].rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
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
                _nodes[t] = UiKit.Panel(_map, FallbackVisuals.TeamColor((Team)t), null, "Seat" + t);
                _nodes[t].sprite = UiTheme.Circle();
                Place(_nodes[t].rectTransform, Vector2.zero, new Vector2(24f, 24f));
                _nodeLabels[t] = Label(_nodes[t].rectTransform, Vector2.zero, new Vector2(24f, 24f), 15, TextAnchor.MiddleCenter, Color.black);
                _nodeLabels[t].text = TeamUtil.SeatNumber((Team)t).ToString();
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

        /// <summary>내 시점 전장 지도 좌표 — 내 발판이 아래, 내가 보는 쪽이 위</summary>
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
            for (int lane = 0; lane < _edges.Length; lane++)
            {
                var frame = arena.GetLane(Team.Home, lane);
                Vector2 a = MapPoint(frame.Origin, me);
                Vector2 b = MapPoint(frame.Point(frame.Length), me);
                var rt = _edges[lane].rectTransform;
                rt.anchoredPosition = a;
                rt.sizeDelta = new Vector2((b - a).magnitude, 2f);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            }
            for (int t = 0; t < _nodes.Length; t++)
                _nodes[t].rectTransform.anchoredPosition = MapPoint(arena.BasePosition((Team)t), me);
        }

        public void Tick(MatchSnapshot s, Team me)
        {
            if (s == null || Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + RefreshInterval;
            if (_builtFor != me)
            {
                _builtFor = me;
                LayoutMap(me);
            }
            for (int c = 0; c < _cards.Length; c++) PaintCard(ref _cards[c], s, _session.Arena.ColumnTarget(me, (Column)c), c);

            for (int t = 0; t < _nodes.Length; t++)
            {
                bool out_ = s.Players[t].Place != 0 && (MatchPhase)s.Header.Phase == MatchPhase.Playing;
                Color c = FallbackVisuals.TeamColor((Team)t);
                _nodes[t].color = out_ ? UiTheme.Alpha(c, 0.3f) : c;
                _nodes[t].rectTransform.localScale = Vector3.one * ((Team)t == me ? 1.25f : 1f);
            }

            int shown = 0;
            for (int i = 0; i < s.UnitCount && shown < MaxDots; i++)
            {
                ref var u = ref s.Units[i];
                var dot = _dots[shown++];
                dot.rectTransform.anchoredPosition = MapPoint(new Vector2(u.U, u.V), me);
                dot.color = FallbackVisuals.TeamColor((Team)Mathf.Clamp(u.Team, 0, TeamUtil.TeamCount - 1));
                if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
            }
            for (int i = shown; i < MaxDots; i++)
                if (_dots[i].gameObject.activeSelf) _dots[i].gameObject.SetActive(false);
        }

        private static void PaintCard(ref EnemyCard card, MatchSnapshot s, Team enemy, int column)
        {
            ref var p = ref s.Players[(int)enemy];
            string name = !string.IsNullOrEmpty(s.Names[(int)enemy]) ? s.Names[(int)enemy] : p.Present != 0 && p.IsBot != 0 ? "AI" : "빈자리";
            string side = column == 0 ? "◀ " : column == 1 ? "▲ " : "▶ ";
            card.Title.text = side + TeamUtil.SeatNumber(enemy) + "번 " + name;
            card.Title.color = FallbackVisuals.TeamColor(enemy);
            card.Hp.text = p.Hp + " / " + p.MaxHp;
            card.Bar.anchorMax = new Vector2(p.MaxHp > 0 ? Mathf.Clamp01((float)p.Hp / p.MaxHp) : 0f, 1f);
            card.Bar.offsetMax = Vector2.zero;
            bool tower = s.Structure(enemy, false).Alive != 0;
            bool nexus = s.Structure(enemy, true).Alive != 0;
            card.Tower.color = tower ? UiTheme.Cyan : UiTheme.Enemy;
            card.Nexus.color = nexus ? UiTheme.Cyan : UiTheme.Enemy;
            bool gone = p.Place != 0;
            card.Back.color = gone ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            card.Status.text = gone ? "탈락 · " + p.Place + "위" : !tower ? "<color=" + UiKit.Hex(UiTheme.Amber) + ">피해 100%</color>" : "";
        }
    }
}
