// C07 우편함 · 공지 — 웹 pages/mailbox.js
//   왼쪽: 탭(우편함 / 공지사항, 안 읽음 배지) + 목록, 오른쪽: 읽기 (친선전 초대면 "바로 입장", 우편 삭제)

using SpellboundVR.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class MailboxScreen : MetaScreen
    {
        private int _tab; // 0 우편, 1 공지
        private string _openId;

        public override string Title => "우편함 · 공지";

        protected override void OnOpen()
        {
            _tab = 0;
            _openId = null;
            MetaState.RefreshMails();
        }

        protected override void HandleStateChanged(string key)
        {
            if (key == "mails") Redraw();
        }

        private static string KindName(MailboxItemDto m) => m.IsNotice ? "공지" : m.IsInvite ? "초대" : "우편";

        protected override void Build(RectTransform body)
        {
            var row = UiKit.Stretch(UiKit.HBox(body, 24, 0, TextAnchor.UpperLeft, "Mailbox"));
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;

            // ── 목록 ──
            var listCard = UiKit.Card(row, 20, 10, "List");
            UiKit.Size(listCard, 720, -1, 0, 1);
            UiKit.Tabs(listCard, new[] { "우편함", "공지사항" }, _tab, i => { _tab = i; _openId = null; Redraw(); },
                       new[] { MetaState.UnreadCount(false), MetaState.UnreadCount(true) });
            if (MetaState.Offline)
            {
                UiKit.Label(listCard, "우편함은 게임 서버에 로그인해야 볼 수 있습니다.", UiTheme.FontSmall, UiTheme.Text3, false, TextAnchor.MiddleCenter);
                return;
            }
            var sr = UiKit.Scroll(listCard, out var content, 6);
            UiKit.Size(sr, -1, -1, -1, 1);
            MailboxItemDto open = null;
            int shown = 0;
            foreach (var m in MetaState.Mails)
            {
                if (m.IsNotice != (_tab == 1)) continue;
                shown++;
                if (m.id == _openId) open = m;
                var item = m;
                var b = UiKit.Button(content, "", BtnStyle.Default, () =>
                {
                    _openId = item.id;
                    MetaState.MarkRead(item);
                    Redraw();
                }, -1, 86);
                b.GetComponent<Image>().sprite = item.id == _openId ? UiTheme.Cut(Color.Lerp(UiTheme.Panel, UiTheme.Cyan, 0.12f), UiTheme.Cyan2, 10, 2) : UiTheme.Cut(UiTheme.Panel, UiTheme.Line, 10, 2);
                UiKit.Clear(b.transform);
                var r = UiKit.Stretch(UiKit.HBox(b.transform, 12, 0, TextAnchor.MiddleLeft, "Row"), 18, 8, 18, 8);
                UiKit.Dot(r, item.read ? UiTheme.Line2 : UiTheme.PlayHi);
                var txt = UiKit.VBox(r, 2, 0, TextAnchor.MiddleLeft, "Text");
                UiKit.Size(txt, -1, -1, 1);
                var title = UiKit.Label(txt, item.title, UiTheme.FontBody, item.read ? UiTheme.Text2 : UiTheme.Text, !item.read);
                title.horizontalOverflow = HorizontalWrapMode.Overflow;
                UiKit.Label(txt, (string.IsNullOrEmpty(item.from) ? "" : item.from + " · ") + MetaState.TimeAgo(item.createdAt), UiTheme.FontSmall - 3, UiTheme.Text3);
                UiKit.Chip(r, KindName(item), item.IsInvite ? Tone.Cyan : Tone.Neutral, UiTheme.FontSmall - 4);
            }
            if (shown == 0) UiKit.Label(content, _tab == 1 ? "등록된 공지가 없습니다." : "받은 우편이 없습니다.", UiTheme.FontSmall, UiTheme.Text3, false, TextAnchor.MiddleCenter);

            // ── 읽기 ──
            var read = UiKit.Card(row, 36, 16, "Read");
            UiKit.Size(read, -1, -1, 1, 1);
            if (open == null)
            {
                read.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
                UiKit.Label(read, "왼쪽에서 읽을 항목을 고르세요.", UiTheme.FontBody, UiTheme.Text2, false, TextAnchor.MiddleCenter);
                int invites = 0;
                foreach (var m in MetaState.Mails) if (!m.read && m.IsInvite) invites++;
                var trio = UiKit.HBox(read, 14, 0, TextAnchor.MiddleCenter, "Trio");
                Stat(trio, MetaState.UnreadCount(false).ToString(), "읽지 않은 우편");
                Stat(trio, MetaState.UnreadCount(true).ToString(), "새 공지");
                Stat(trio, invites.ToString(), "대전 초대");
                return;
            }
            var meta = UiKit.HBox(read, 10, 0, TextAnchor.MiddleLeft, "Meta");
            UiKit.Chip(meta, KindName(open), Tone.Cyan);
            UiKit.Label(meta, (string.IsNullOrEmpty(open.from) ? "" : $"보낸 사람: {open.from} · ") + MetaState.TimeAgo(open.createdAt), UiTheme.FontSmall - 2, UiTheme.Text3);
            UiKit.Label(read, open.title, UiTheme.FontHeadline - 4, UiTheme.Text, true);
            UiKit.Label(read, open.body ?? "", UiTheme.FontBody, UiTheme.Text2, false, TextAnchor.UpperLeft);
            if (!string.IsNullOrEmpty(open.roomCode))
            {
                var att = UiKit.CardRow(read, 20, 14, "Attach", UiTheme.Panel, UiTheme.Line2);
                var box = UiKit.VBox(att, 2, 0, TextAnchor.MiddleLeft, "Code");
                UiKit.Size(box, -1, -1, 1);
                UiKit.Label(box, "룸 코드", UiTheme.FontLabel, UiTheme.Text3, true);
                UiKit.Label(box, open.roomCode, UiTheme.FontHeadline, UiTheme.Text, true);
                string code = open.roomCode;
                UiKit.Button(att, "바로 입장", BtnStyle.Play, () =>
                {
                    MetaState.PendingRoomCode = code;
                    Ui.Go(MetaScreenId.Room);
                });
            }
            UiKit.Spacer(read);
            if (!open.IsNotice)
            {
                var foot = UiKit.HBox(read, 10, 0, TextAnchor.MiddleRight, "Foot");
                UiKit.Spacer(foot);
                string id = open.id;
                UiKit.Button(foot, "우편 삭제", BtnStyle.Danger, () =>
                    Ui.Confirm("우편 삭제", "이 우편을 삭제할까요? 되돌릴 수 없습니다.", "삭제", true, ok =>
                    {
                        if (!ok) return;
                        MailService.Delete(id, () => { _openId = null; MetaUiRoot.Toast("우편을 삭제했습니다.", Tone.Play); MetaState.RefreshMails(); },
                                           e => MetaUiRoot.Toast(e, Tone.Enemy));
                    }), -1, 50, UiTheme.FontSmall);
            }
        }

        private static void Stat(Transform parent, string value, string label)
        {
            var box = UiKit.VBox(parent, 2, 14, TextAnchor.MiddleCenter, "Stat");
            var img = box.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(UiTheme.Panel, UiTheme.Line, 0, 2);
            img.type = Image.Type.Sliced;
            UiKit.Size(box, 200, -1);
            UiKit.Label(box, value, UiTheme.FontHeadline, UiTheme.Text, true, TextAnchor.MiddleCenter);
            UiKit.Label(box, label, UiTheme.FontSmall - 3, UiTheme.Text3, false, TextAnchor.MiddleCenter);
        }
    }
}
