// C06 친선전 룸 — 웹 pages/room.js, 서버 쪽은 BackendSessionService (6자리 코드 방)
//   방이 없을 때: 룸 만들기 / 코드로 입장
//   방장: 코드 · 복사 · 닫기, 자리(HOST 나 vs GUEST), 봇으로 채우기 · 내보내기 · 대전 시작, 접속 중 친구 초대
//   게스트: 코드 · 나가기, 자리(HOST 상대 vs GUEST 나), 준비 완료 → 방장이 시작하면 C11
//   친선전은 레이팅에 반영되지 않는다 (전적만 저장).

using System.Collections.Generic;
using SpellboundVR.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class FriendlyRoomScreen : MetaScreen
    {
        private BackendSessionService _session;
        private readonly List<FriendDto> _friends = new List<FriendDto>();

        public override string Title => "친선전 룸";

        private bool InRoom => _session != null && _session.Kind == BackendSessionService.SessionKind.FriendlyRoom;

        protected override void OnOpen()
        {
            _session = BackendSessionService.Instance;
            _session.OnRoomChanged += HandleRoom;
            _session.OnRoomCodeChanged += HandleCode;
            _session.OnBotFilled += Redraw;
            _session.OnFriendlyMatchStarted += HandleStarted;
            _session.OnSessionEnded += HandleEnded;
            _session.OnSessionError += HandleError;

            // 우편함 초대장으로 들어온 경우 바로 입장
            if (!string.IsNullOrEmpty(MetaState.PendingRoomCode))
            {
                string code = MetaState.PendingRoomCode;
                MetaState.PendingRoomCode = null;
                _session.JoinFriendlyRoom(code);
            }
            LoadFriends();
        }

        protected override void OnClose()
        {
            if (_session == null) return;
            _session.OnRoomChanged -= HandleRoom;
            _session.OnRoomCodeChanged -= HandleCode;
            _session.OnBotFilled -= Redraw;
            _session.OnFriendlyMatchStarted -= HandleStarted;
            _session.OnSessionEnded -= HandleEnded;
            _session.OnSessionError -= HandleError;
        }

        private void LoadFriends()
        {
            if (MetaState.Offline || !AuthService.IsLoggedIn) return;
            FriendService.List(false, l =>
            {
                _friends.Clear();
                foreach (var f in l) if (f.relation == "friend") _friends.Add(f);
                Redraw();
            });
        }

        private void HandleRoom(RoomDto room) => Redraw();
        private void HandleCode(string code) => Redraw();
        private void HandleError(string err) { MetaUiRoot.Toast(err, Tone.Enemy); Redraw(); }

        private void HandleEnded(string reason)
        {
            if (!string.IsNullOrEmpty(reason) && reason != "나가기") MetaUiRoot.Toast(reason, Tone.Enemy);
            Redraw();
        }

        private void HandleStarted(string opponent, bool bot)
        {
            int rating = bot ? MetaState.Rating : (_session.Room != null ? (_session.IsHost ? _session.Room.guestRating : _session.Room.hostRating) : 0);
            MetaFlow.Instance.StartMatch(new MatchContext
            {
                Mode = MatchResultService.ModeFriendly,
                OpponentName = string.IsNullOrEmpty(opponent) ? "상대" : opponent,
                OpponentRating = rating,
                OpponentBot = bot,
                RoomCode = _session.RoomCode,
            });
        }

        protected override void HandleStateChanged(string key)
        {
            if (key == "online" && InRoom) Redraw();
        }

        protected override void Build(RectTransform body)
        {
            var outer = UiKit.Stretch(UiKit.HBox(body, 0, 0, TextAnchor.UpperCenter, "Outer"));
            outer.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            var col = UiKit.VBox(outer, 20, 0, TextAnchor.UpperLeft, "Col");
            UiKit.Size(col, 1300, -1, 0, 1);
            if (!InRoom) BuildNoRoom(col);
            else if (_session.IsHost) BuildHost(col);
            else BuildGuest(col);
        }

        private void BuildNoRoom(Transform col)
        {
            var two = UiKit.HBox(col, 24, 0, TextAnchor.UpperLeft, "Two");
            two.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            var make = UiKit.Card(two, 32, 18, "Create");
            UiKit.Size(make, -1, 360, 1);
            UiKit.Heading(make, "룸 만들기");
            UiKit.Label(make, "방을 만들면 6자리 코드가 발급됩니다. 친구를 초대하거나 봇으로 빈 자리를 채울 수 있어요.", UiTheme.FontBody, UiTheme.Text2);
            UiKit.Spacer(make);
            UiKit.Button(make, "룸 생성", BtnStyle.Play, () => { _session.CreateFriendlyRoom(); Redraw(); }, -1, 66, UiTheme.FontTitle - 2);

            var join = UiKit.Card(two, 32, 18, "Join");
            UiKit.Size(join, -1, 360, 1);
            UiKit.Heading(join, "코드로 입장");
            var code = UiKit.Input(join, "ABC123", false, 6, 84);
            code.textComponent.fontSize = 44;
            code.textComponent.alignment = TextAnchor.MiddleCenter;
            ((Text)code.placeholder).fontSize = 44;
            ((Text)code.placeholder).alignment = TextAnchor.MiddleCenter;
            code.onValueChanged.AddListener(v =>
            {
                string clean = System.Text.RegularExpressions.Regex.Replace(v.ToUpperInvariant(), "[^A-Z0-9]", "");
                if (clean != v) code.text = clean;
            });
            UiKit.Spacer(join);
            var go = UiKit.Button(join, "입장", BtnStyle.Info, () =>
            {
                string c = code.text.Trim().ToUpperInvariant();
                if (!System.Text.RegularExpressions.Regex.IsMatch(c, "^[A-Z0-9]{6}$")) { MetaUiRoot.Toast("룸 코드는 영문 대문자·숫자 6자리입니다.", Tone.Enemy); return; }
                _session.JoinFriendlyRoom(c);
                Redraw();
            }, -1, 66, UiTheme.FontTitle - 2);
            go.interactable = !MetaState.Offline;
            if (MetaState.Offline) UiKit.Note(col, "오프라인 모드: 방은 이 기기에만 만들어지며 봇으로만 채울 수 있습니다.", Tone.Amber);
        }

        private void CodeCard(Transform col, string label, bool host)
        {
            var c = UiKit.CardRow(col, 26, 14, "Code");
            var left = UiKit.VBox(c, 4, 0, TextAnchor.MiddleLeft, "Left");
            UiKit.Size(left, -1, -1, 1);
            UiKit.Label(left, label, UiTheme.FontLabel, UiTheme.Text3, true);
            var code = UiKit.Label(left, string.IsNullOrEmpty(_session.RoomCode) ? "------" : _session.RoomCode, 60, UiTheme.Text, true);
            code.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (host)
            {
                UiKit.Button(c, "복사", BtnStyle.Default, () => { GUIUtility.systemCopyBuffer = _session.RoomCode; MetaUiRoot.Toast("룸 코드를 복사했습니다.", Tone.Play); }, -1, 50, UiTheme.FontSmall);
                UiKit.Button(c, "룸 닫기", BtnStyle.Ghost, () => { _session.LeaveSession(); Redraw(); }, -1, 50, UiTheme.FontSmall);
            }
            else UiKit.Button(c, "나가기", BtnStyle.Ghost, () => { _session.LeaveSession(); Redraw(); }, -1, 50, UiTheme.FontSmall);
        }

        /// <summary>자리 카드 (you = 청록 나, foe = 분홍빨강 상대)</summary>
        private static void Seat(Transform parent, bool you, string role, string name, int rating, string state, bool readyChip)
        {
            var c = UiKit.Card(parent, 26, 8, "Seat", you ? Color.Lerp(UiTheme.Panel, UiTheme.Cyan, 0.08f) : Color.Lerp(UiTheme.Panel, UiTheme.Enemy, 0.08f), you ? UiTheme.Cyan2 : Color.Lerp(UiTheme.Panel, UiTheme.Enemy, 0.55f));
            UiKit.Size(c, -1, 230, 1);
            var top = UiKit.HBox(c, 16, 0, TextAnchor.MiddleLeft, "Top");
            UiKit.Wizard(top, !you, 130, name == null);
            var txt = UiKit.VBox(top, 6, 0, TextAnchor.MiddleLeft, "Text");
            UiKit.Size(txt, -1, -1, 1);
            UiKit.Label(txt, role + (you ? " · 나" : ""), UiTheme.FontLabel, you ? UiTheme.Cyan : UiTheme.Enemy, true);
            if (name == null)
            {
                UiKit.Label(txt, "게스트 자리", UiTheme.FontTitle, UiTheme.Text3, true);
                UiKit.Label(txt, "친구가 코드로 들어오거나 초대를 받으면 앉습니다", UiTheme.FontSmall - 3, UiTheme.Text3);
                return;
            }
            UiKit.Label(txt, name, UiTheme.FontTitle + 4, UiTheme.Text, true);
            if (rating > 0) UiKit.Label(txt, $"{MetaState.TierOf(rating)} · {rating}", UiTheme.FontSmall - 2, UiTheme.Text3);
            var chipRow = UiKit.HBox(txt, 0, 0, TextAnchor.MiddleLeft, "State");
            UiKit.Chip(chipRow, state, readyChip ? Tone.Cyan : Tone.Neutral);
        }

        private void BuildHost(Transform col)
        {
            CodeCard(col, "룸 코드", true);
            var room = _session.Room;
            bool bot = _session.OpponentIsBot;
            bool hasGuest = room != null && room.HasGuest;
            bool guestReady = hasGuest && room.GuestReady;

            var seats = UiKit.HBox(col, 24, 0, TextAnchor.MiddleCenter, "Seats");
            seats.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Seat(seats, true, "HOST", MetaState.Nickname, MetaState.Rating, "준비 완료", true);
            UiKit.Label(seats, "VS", UiTheme.FontHeadline, UiTheme.Text3, true, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            if (bot) Seat(seats, false, "GUEST", "Bot", MetaState.Rating, "Bot", true);
            else if (hasGuest) Seat(seats, false, "GUEST", room.guestNickname, room.guestRating, guestReady ? "준비 완료" : "입장 완료", guestReady);
            else Seat(seats, false, "GUEST", null, 0, "", false);

            string problem = MetaState.DeckProblem();
            var actions = UiKit.HBox(col, 12, 0, TextAnchor.MiddleLeft, "Actions");
            var fill = UiKit.Button(actions, "봇으로 채우기", BtnStyle.Default, () => _session.FillWithBot());
            fill.interactable = !hasGuest && !bot;
            if (hasGuest) UiKit.Button(actions, "내보내기", BtnStyle.Ghost, () => _session.KickGuest());
            UiKit.Spacer(actions);
            var start = UiKit.Button(actions, "▶  대전 시작", BtnStyle.Play, () => _session.StartFriendlyMatch(), 280, 66, UiTheme.FontTitle - 2);
            start.interactable = problem == null && (bot || guestReady);
            if (problem != null) UiKit.Note(col, problem + " 마법서에서 덱을 편성해주세요.", Tone.Amber);
            else if (hasGuest && !guestReady) UiKit.Note(col, "게스트가 준비를 마치면 대전을 시작할 수 있습니다.", Tone.Cyan);

            if (MetaState.Offline) return;
            var inv = UiKit.Card(col, 22, 6, "Invite");
            var online = _friends.FindAll(f => MetaState.Online.Contains(f.friendPlayerId) || f.npc);
            UiKit.Heading(inv, "접속 중인 친구 초대", online.Count.ToString());
            if (online.Count == 0) UiKit.Label(inv, "접속 중인 친구가 없습니다.", UiTheme.FontSmall, UiTheme.Text3, false, TextAnchor.MiddleCenter);
            foreach (var f in online)
            {
                var r = UiKit.HBox(inv, 14, 0, TextAnchor.MiddleLeft, "Row");
                UiKit.Size(r, -1, 60);
                UiKit.Dot(r, MetaState.Online.Contains(f.friendPlayerId) ? UiTheme.PlayHi : UiTheme.Amber);
                var n = UiKit.Label(r, $"{f.nickname}  <size=18><color={UiKit.Hex(UiTheme.Text3)}>{f.rating}</color></size>", UiTheme.FontBody, UiTheme.Text, true);
                UiKit.Size(n, -1, -1, 1);
                var friend = f;
                Button b = null;
                b = UiKit.Button(r, "초대", BtnStyle.Info, () =>
                {
                    var done = UiKit.Busy(b, "보내는 중…");
                    MailService.SendInvite(friend.friendPlayerId, _session.RoomCode, () => { done(); MetaUiRoot.Toast($"{friend.nickname} 님에게 초대장을 보냈습니다.", Tone.Play); },
                                           e => { done(); MetaUiRoot.Toast(e, Tone.Enemy); });
                }, -1, 48, UiTheme.FontSmall);
            }
        }

        private void BuildGuest(Transform col)
        {
            CodeCard(col, "입장한 룸", false);
            var room = _session.Room;
            var seats = UiKit.HBox(col, 24, 0, TextAnchor.MiddleCenter, "Seats");
            seats.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Seat(seats, false, "HOST", room != null ? room.hostNickname : "호스트", room != null ? room.hostRating : 0, "대기 중", false);
            UiKit.Label(seats, "VS", UiTheme.FontHeadline, UiTheme.Text3, true, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            Seat(seats, true, "GUEST", MetaState.Nickname, MetaState.Rating, _session.IsReady ? "준비 완료" : "준비 전", _session.IsReady);

            string problem = MetaState.DeckProblem();
            if (problem != null) UiKit.Note(col, problem + " 마법서에서 덱을 편성해주세요.", Tone.Amber);
            var actions = UiKit.HBox(col, 12, 0, TextAnchor.MiddleRight, "Actions");
            UiKit.Spacer(actions);
            var ready = UiKit.Button(actions, _session.IsReady ? "호스트가 시작하길 기다리는 중…" : "준비 완료", BtnStyle.Play, () => { _session.SetReady(true); Redraw(); }, -1, 66, UiTheme.FontTitle - 2);
            ready.interactable = problem == null && !_session.IsReady;
        }
    }
}
