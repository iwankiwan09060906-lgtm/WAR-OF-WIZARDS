// C05 친구 — 웹 pages/friends.js
//   닉네임으로 요청 보내기 · 받은 요청(수락/거절) · 친구(접속 상태 · 친선전 초대 · 삭제) · 보낸 요청(취소)

using System;
using System.Collections.Generic;
using SpellboundVR.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class FriendsScreen : MetaScreen
    {
        private FriendDto[] _list;
        private string _error;

        public override string Title => "친구 목록";

        protected override void OnOpen()
        {
            _list = null;
            _error = null;
            Load();
        }

        private void Load()
        {
            if (MetaState.Offline || !AuthService.IsLoggedIn)
            {
                _list = new FriendDto[0];
                _error = "친구 기능은 게임 서버에 로그인해야 쓸 수 있습니다.";
                return;
            }
            FriendService.List(true, l => { _list = l; _error = null; Redraw(); }, e => { _error = e; _list ??= new FriendDto[0]; Redraw(); });
        }

        protected override void HandleStateChanged(string key)
        {
            if (key == "online" && _list != null) Redraw();
        }

        protected override void Build(RectTransform body)
        {
            var session = BackendSessionService.Existing;
            bool hosting = session != null && session.Kind == BackendSessionService.SessionKind.FriendlyRoom && session.IsHost;
            UiKit.Button(Ui.HeaderRight, hosting ? $"룸 {session.RoomCode}" : "친선전 룸 만들기", BtnStyle.Info, () => Ui.Go(MetaScreenId.Room), -1, 50, UiTheme.FontSmall);

            var outer = UiKit.Stretch(UiKit.HBox(body, 0, 0, TextAnchor.UpperCenter, "Outer"));
            outer.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            var col = UiKit.VBox(outer, 18, 0, TextAnchor.UpperLeft, "Col");
            UiKit.Size(col, 1240, -1, 0, 1);

            var form = UiKit.CardRow(col, 22, 12, "Request");
            var f = UiKit.VBox(form, 6, 0, TextAnchor.UpperLeft, "Field");
            UiKit.Size(f, -1, -1, 1);
            UiKit.Label(f, "닉네임으로 친구 요청 보내기", UiTheme.FontLabel, UiTheme.Text2, true);
            var nick = UiKit.Input(f, "친구 닉네임", false, 12);
            Button send = null;
            send = UiKit.Button(form, "요청", BtnStyle.Play, () =>
            {
                string n = nick.text.Trim();
                if (n.Length < 2) { MetaUiRoot.Toast("닉네임을 2자 이상 입력해주세요.", Tone.Enemy); return; }
                Act(send, done => FriendService.Request(n, _ => done(null), done), $"{n} 님에게 친구 요청을 보냈습니다.");
            }, 160, UiTheme.InputHeight);
            send.interactable = !MetaState.Offline;

            if (_error != null) UiKit.Note(col, _error, Tone.Enemy);
            if (_list == null)
            {
                UiKit.Label(col, "불러오는 중…", UiTheme.FontBody, UiTheme.Text3, false, TextAnchor.MiddleCenter);
                return;
            }

            var incoming = new List<FriendDto>();
            var friends = new List<FriendDto>();
            var outgoing = new List<FriendDto>();
            foreach (var x in _list)
            {
                if (x.relation == "incoming") incoming.Add(x);
                else if (x.relation == "outgoing") outgoing.Add(x);
                else friends.Add(x);
            }
            friends.Sort((a, b) => MetaState.Online.Contains(b.friendPlayerId).CompareTo(MetaState.Online.Contains(a.friendPlayerId)));

            var sr = UiKit.Scroll(col, out var content, 18);
            UiKit.Size(sr, -1, -1, -1, 1);

            if (incoming.Count > 0)
            {
                var card = Section(content, "받은 요청", incoming.Count);
                foreach (var x in incoming)
                {
                    var r = RowFor(card, x);
                    Button acc = null, dec = null;
                    acc = UiKit.Button(r, "수락", BtnStyle.Info, () => Act(acc, done => FriendService.Respond(x.friendPlayerId, true, _ => done(null), done), $"{x.nickname} 님과 친구가 되었습니다."), -1, 48, UiTheme.FontSmall);
                    dec = UiKit.Button(r, "거절", BtnStyle.Ghost, () => Act(dec, done => FriendService.Respond(x.friendPlayerId, false, _ => done(null), done), null), -1, 48, UiTheme.FontSmall);
                }
            }

            var fc = Section(content, "친구", friends.Count);
            if (friends.Count == 0) UiKit.Label(fc, "아직 친구가 없습니다. 닉네임으로 친구 요청을 보내보세요.", UiTheme.FontSmall, UiTheme.Text3, false, TextAnchor.MiddleCenter);
            foreach (var x in friends)
            {
                bool on = MetaState.Online.Contains(x.friendPlayerId);
                var r = RowFor(fc, x, on);
                var st = UiKit.Label(r, on ? "접속 중" : "오프라인", UiTheme.FontSmall - 2, on ? UiTheme.PlayHi : UiTheme.Text3);
                UiKit.Size(st, 100, -1);
                Button inv = null, del = null;
                inv = UiKit.Button(r, "초대", BtnStyle.Info, () =>
                {
                    if (!hosting) { MetaUiRoot.Toast("먼저 친선전 룸을 만들어야 초대할 수 있습니다.", Tone.Enemy); return; }
                    Act(inv, done => MailService.SendInvite(x.friendPlayerId, session.RoomCode, () => done(null), done), $"{x.nickname} 님에게 초대장을 보냈습니다. (룸 {session.RoomCode})");
                }, -1, 48, UiTheme.FontSmall);
                inv.interactable = on || x.npc;
                del = UiKit.Button(r, "삭제", BtnStyle.Ghost, () =>
                    Ui.Confirm("친구 삭제", $"{x.nickname} 님을 친구 목록에서 삭제할까요?", "삭제", true, ok =>
                    {
                        if (ok) Act(del, done => FriendService.Remove(x.friendPlayerId, () => done(null), done), $"{x.nickname} 님을 삭제했습니다.");
                    }), -1, 48, UiTheme.FontSmall);
            }

            if (outgoing.Count > 0)
            {
                var oc = Section(content, "보낸 요청", outgoing.Count);
                foreach (var x in outgoing)
                {
                    var r = RowFor(oc, x);
                    UiKit.Label(r, "수락 대기 중", UiTheme.FontSmall - 2, UiTheme.Text3);
                    Button c = null;
                    c = UiKit.Button(r, "취소", BtnStyle.Ghost, () => Act(c, done => FriendService.Remove(x.friendPlayerId, () => done(null), done), null), -1, 48, UiTheme.FontSmall);
                }
            }
        }

        private static RectTransform Section(Transform parent, string title, int count)
        {
            var card = UiKit.Card(parent, 22, 6, "Section");
            UiKit.Heading(card, title, count.ToString());
            return card;
        }

        private static RectTransform RowFor(Transform card, FriendDto f, bool? online = null)
        {
            var r = UiKit.HBox(card, 14, 0, TextAnchor.MiddleLeft, "Row");
            UiKit.Size(r, -1, 64);
            if (online != null) UiKit.Dot(r, online.Value ? UiTheme.PlayHi : UiTheme.Line2);
            var who = UiKit.VBox(r, 0, 0, TextAnchor.MiddleLeft, "Who");
            UiKit.Size(who, -1, -1, 1);
            UiKit.Label(who, f.nickname + (f.npc ? $"  <size=16><color={UiKit.Hex(UiTheme.Text3)}>데모 NPC</color></size>" : ""), UiTheme.FontBody, UiTheme.Text, true);
            UiKit.Label(who, $"{MetaState.TierOf(f.rating)} · {f.rating}", UiTheme.FontSmall - 3, UiTheme.Text3);
            return r;
        }

        /// <summary>요청 실행 → 성공 알림 → 목록 새로고침. run(done): done(null) = 성공, done("오류") = 실패</summary>
        private void Act(Button b, Action<Action<string>> run, string okMessage)
        {
            var restore = UiKit.Busy(b, "처리 중…");
            run(err =>
            {
                restore();
                if (err != null) { MetaUiRoot.Toast(err, Tone.Enemy); return; }
                if (okMessage != null) MetaUiRoot.Toast(okMessage, Tone.Play);
                Load();
            });
        }
    }
}
