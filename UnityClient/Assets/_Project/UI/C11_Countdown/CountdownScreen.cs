// C11 카운트다운 — 웹 pages/countdown.js
//   나(청록, 덱 8장 공개) · 3 · 2 · 1 · START! · 상대(분홍빨강, 덱은 ?로 가림)
//   이 화면이 열리면 실제 경기를 시작한다 (MetaFlow.BeginBattle). 숫자는 서버(로컬 호스트)의 카운트다운을 그대로 보여주고,
//   경기가 시작되면 MetaFlow가 메뉴를 숨겨 C12 전투 HUD만 남긴다.
//   4인 개인전: 오른쪽에 자리 네 칸 (번호 · 이름 · 입장 상태). 빈자리는 첫 사람이 들어오고 30초가 지나면 AI 가 앉는다.

using SpellboundVR.Core;
using SpellboundVR.Network;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class CountdownScreen : MetaScreen
    {
        private Text _count, _sub;
        private GameObject _cancel;
        private int _lastShown = -99;
        private readonly Text[] _seats = new Text[TeamUtil.TeamCount];

        public override string Title => null;
        public override bool ShowBackToLobby => false;

        protected override void OnOpen()
        {
            _lastShown = -99;
            MetaFlow.Instance.BeginBattle();
        }

        protected override void Build(RectTransform body)
        {
            var ctx = MetaState.Match ?? new MatchContext();
            var row = UiKit.Stretch(UiKit.HBox(body, 40, 0, TextAnchor.MiddleCenter, "Vs"));
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;

            Side(row, false, "YOU", MetaState.Nickname, MetaState.Offline ? 0 : MetaState.Rating, true);

            var mid = UiKit.VBox(row, 10, 0, TextAnchor.MiddleCenter, "Center");
            UiKit.Size(mid, 420, -1);
            _count = UiKit.Label(mid, "", 200, UiTheme.Text, true, TextAnchor.MiddleCenter);
            _count.horizontalOverflow = HorizontalWrapMode.Overflow;
            _count.font = UiTheme.Display; // 프로토타입 display 글꼴 (Cinzel)
            var glow = _count.gameObject.AddComponent<Outline>();
            glow.effectColor = UiTheme.Alpha(UiTheme.Purple, 0.35f);
            glow.effectDistance = new Vector2(4f, 4f);
            UiKit.Size(_count, -1, 240);
            UiKit.Label(mid, MetaState.ModeName(ctx.Mode), UiTheme.FontTitle, UiTheme.Text2, false, TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(ctx.RoomCode)) UiKit.Label(mid, "게임 방 " + ctx.RoomCode, UiTheme.FontSmall, UiTheme.Text3, false, TextAnchor.MiddleCenter);
            _sub = UiKit.Label(mid, "손이 잘 보이도록 두 손을 앞으로 내밀어 주세요.", UiTheme.FontSmall, UiTheme.Text3, false, TextAnchor.MiddleCenter);
            // 상대 · 서버를 기다리는 동안에만 보이는 취소 (VR에서 이 화면에 갇히지 않게)
            _cancel = UiKit.Button(mid, "취소하고 로비로", BtnStyle.Ghost, () => MetaFlow.Instance.LeaveBattle(null), 300, 58).gameObject;

            if (ctx.IsFfa) SeatList(row);
            else Side(row, true, ctx.OpponentBot ? "ENEMY · BOT" : "ENEMY", ctx.OpponentName, ctx.OpponentRating, false);
            Refresh();
        }

        private void SeatList(Transform parent)
        {
            var col = UiKit.VBox(parent, 16, 0, TextAnchor.MiddleCenter, "Seats");
            UiKit.Size(col, -1, -1, 1);
            UiKit.Label(col, "4인 개인전", UiTheme.FontTitle, UiTheme.Enemy, true, TextAnchor.MiddleCenter);
            for (int i = 0; i < _seats.Length; i++)
            {
                _seats[i] = UiKit.Label(col, "", UiTheme.FontTitle - 2, UiTheme.Text2, true, TextAnchor.MiddleLeft);
                _seats[i].supportRichText = true;
                _seats[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        /// <summary>자리 표 — 번호 · 이름 · 입장 상태 (들어온 자리 수도 돌려준다)</summary>
        private int PaintSeats()
        {
            var flow = MetaFlow.Instance;
            var s = flow.Latest;
            if (s == null || _seats[0] == null) return 0;
            int present = 0;
            for (int t = 0; t < _seats.Length; t++)
            {
                var team = TeamUtil.TeamAtClock(t);
                ref var p = ref s.Players[(int)team];
                bool me = team == flow.LocalTeam && p.Present != 0;
                string name = !string.IsNullOrEmpty(s.Names[(int)team]) ? s.Names[(int)team] : p.IsBot != 0 ? "AI" : "";
                string state = p.Present == 0 ? "<color=" + UiKit.Hex(UiTheme.Text3) + ">기다리는 중…</color>"
                             : p.IsBot != 0 ? "<color=" + UiKit.Hex(UiTheme.Amber) + ">AI 입장</color>"
                             : "<color=" + UiKit.Hex(UiTheme.Cyan) + ">입장 완료</color>";
                if (p.Present != 0) present++;
                _seats[t].text = (t + 1) + "번  " + (me ? "나 · " : "") + name + "   " + state;
            }
            return present;
        }

        private static void Side(Transform parent, bool foe, string label, string name, int rating, bool showDeck)
        {
            var col = UiKit.VBox(parent, 10, 0, TextAnchor.MiddleCenter, foe ? "Foe" : "You");
            UiKit.Size(col, -1, -1, 1);
            UiKit.Label(col, label, UiTheme.FontTitle, foe ? UiTheme.Enemy : UiTheme.Cyan, true, TextAnchor.MiddleCenter);
            var fig = UiKit.HBox(col, 0, 0, TextAnchor.MiddleCenter, "Figure");
            UiKit.Wizard(fig, foe, 380);
            UiKit.Label(col, name, UiTheme.FontHeadline, UiTheme.Text, true, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Label(col, rating > 0 ? $"{MetaState.TierOf(rating)} · {rating}" : " ", UiTheme.FontBody, UiTheme.Text3, false, TextAnchor.MiddleCenter);
            var hand = UiKit.HBox(col, 8, 0, TextAnchor.MiddleCenter, "Hand");
            for (int i = 0; i < 8; i++)
            {
                if (showDeck && i < MetaState.Deck.Count) UiKit.SkillIcon(hand, MetaState.Deck[i], 54);
                else
                {
                    var cover = UiKit.Card(hand, 0, 0, "Cover", UiTheme.Bg2, UiTheme.Line2);
                    UiKit.Size(cover, 54, 54);
                    var q = UiKit.Label(cover, "?", UiTheme.FontTitle, UiTheme.Text3, true, TextAnchor.MiddleCenter);
                    UiKit.Size(q, 54, 54);
                }
            }
        }

        public override void Tick(float dt) => Refresh();

        private void Refresh()
        {
            if (_count == null) return;
            var flow = MetaFlow.Instance;
            var ctx = MetaState.Match;
            int present = ctx != null && ctx.IsFfa ? PaintSeats() : 0;
            switch (flow.Phase)
            {
                case MatchPhase.Countdown:
                {
                    if (_sub != null) _sub.text = "손이 잘 보이도록 두 손을 앞으로 내밀어 주세요.";
                    int n = Mathf.CeilToInt(flow.PhaseTimeLeft);
                    if (n != _lastShown)
                    {
                        _lastShown = n;
                        _count.text = n > 0 ? n.ToString() : "START!";
                        _count.color = n > 0 ? UiTheme.Text : UiTheme.PlayHi;
                        _count.fontSize = n > 0 ? 200 : 110;
                    }
                    break;
                }
                case MatchPhase.Playing:
                    _count.text = "START!";
                    _count.color = UiTheme.PlayHi;
                    _count.fontSize = 110;
                    break;
                default:
                    _count.text = "…";
                    _count.color = UiTheme.Text3;
                    if (_sub != null) _sub.text = flow.OnlineBattle ? flow.OnlineStatus : "경기를 준비하는 중…";
                    var s = flow.Latest;
                    if (_sub != null && s != null && ctx != null && ctx.IsFfa && s.Header.BotFillIn >= 0f)
                        _sub.text = $"입장 {present} / 4명 · {Mathf.CeilToInt(s.Header.BotFillIn)}초 뒤 빈자리는 AI 가 채웁니다";
                    break;
            }
            if (_cancel != null) _cancel.SetActive(flow.Phase != MatchPhase.Countdown && flow.Phase != MatchPhase.Playing);
        }
    }
}
