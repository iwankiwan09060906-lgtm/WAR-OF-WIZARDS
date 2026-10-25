// C03 마이페이지 — 웹 pages/mypage.js
//   왼쪽: 프로필 (이름 · 등급 · 승률 · 전체/승/패) · 전적 분석 (최근 W/L · 평균 시간 · 평균 시전)
//   오른쪽: 내 덱(HUD 슬롯 순서) · 최근 대전 10경기 · 계정 관리(닉네임 · 비밀번호 변경)

using SpellboundVR.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class MyPageScreen : MetaScreen
    {
        private MatchHistoryDto[] _matches;
        private string _loadError;

        public override string Title => "마이페이지";

        protected override void OnOpen()
        {
            _matches = null;
            _loadError = null;
            MetaState.RefreshProfile();
            if (MetaState.Offline || !AuthService.IsLoggedIn)
            {
                _matches = new MatchHistoryDto[0];
                return;
            }
            MatchResultService.GetHistory(AuthService.PlayerId, 30, m => { _matches = m; Redraw(); }, e => { _loadError = e; _matches = new MatchHistoryDto[0]; Redraw(); });
        }

        protected override void HandleStateChanged(string key)
        {
            if (key == "profile" || key == "deck") Redraw();
        }

        protected override void Build(RectTransform body)
        {
            var p = MetaState.Profile ?? new PlayerDto { nickname = MetaState.Nickname, rating = 1000 };
            var row = UiKit.Stretch(UiKit.HBox(body, 28, 0, TextAnchor.UpperLeft, "Mine"));
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;

            // ── 왼쪽 ──
            var left = UiKit.VBox(row, 22, 0, TextAnchor.UpperLeft, "Left");
            UiKit.Size(left, 560, -1, 0, 1);
            var prof = UiKit.Card(left, 30, 14, "Profile");
            prof.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            var avatarRow = UiKit.HBox(prof, 0, 0, TextAnchor.MiddleCenter, "AvatarRow");
            var avatar = UiKit.Node(avatarRow, "Avatar");
            UiKit.Size(avatar, 120, 120);
            var av = avatar.gameObject.AddComponent<Image>();
            av.sprite = UiTheme.Circle();
            av.color = UiTheme.Cyan2;
            var ini = UiKit.Label(avatar, p.nickname.Length > 0 ? p.nickname.Substring(0, 1) : "?", 56, Color.white, true, TextAnchor.MiddleCenter);
            UiKit.Stretch((RectTransform)ini.transform);
            UiKit.Label(prof, p.nickname, UiTheme.FontHeadline, UiTheme.Text, true, TextAnchor.MiddleCenter);
            var chipRow = UiKit.HBox(prof, 0, 0, TextAnchor.MiddleCenter, "Tier");
            UiKit.Chip(chipRow, $"{MetaState.TierOf(p.rating)} · {p.rating}", Tone.Cyan);
            int rate = MetaState.WinRate(p.wins, p.losses);
            UiKit.Label(prof, $"{rate}%", 72, UiTheme.Cyan, true, TextAnchor.MiddleCenter);
            UiKit.Label(prof, "WIN RATE", UiTheme.FontLabel, UiTheme.Text3, true, TextAnchor.MiddleCenter);
            UiKit.Progress(prof, rate / 100f, UiTheme.Cyan, 12);
            Trio(prof, (p.wins + p.losses).ToString(), "전체", UiTheme.Text, p.wins.ToString(), "승", UiTheme.Cyan, p.losses.ToString(), "패", UiTheme.Enemy);

            var ana = UiKit.Card(left, 26, 12, "Analysis");
            var list = _matches ?? new MatchHistoryDto[0];
            UiKit.Heading(ana, "전적 분석", $"최근 {list.Length}경기");
            if (list.Length == 0) UiKit.Label(ana, _matches == null ? "불러오는 중…" : "기록 없음", UiTheme.FontSmall, UiTheme.Text3);
            else
            {
                var wl = UiKit.HBox(ana, 6, 0, TextAnchor.MiddleLeft, "WL");
                for (int i = Mathf.Min(15, list.Length) - 1; i >= 0; i--)
                {
                    bool win = list[i].result == "WIN";
                    var t = UiKit.Chip(wl, win ? "W" : list[i].result == "DRAW" ? "D" : "L", win ? Tone.Cyan : Tone.Enemy, UiTheme.FontSmall - 2);
                }
                float dur = 0f, casts = 0f;
                foreach (var m in list) { dur += m.durationSec; casts += m.casts; }
                Trio(ana, MetaState.Mmss(dur / list.Length), "평균 플레이 시간", UiTheme.Text, (casts / list.Length).ToString("0"), "평균 시전 횟수", UiTheme.Text, null, null, UiTheme.Text);
            }

            // ── 오른쪽 ──
            var right = UiKit.VBox(row, 22, 0, TextAnchor.UpperLeft, "Right");
            UiKit.Size(right, -1, -1, 1, 1);
            var deck = UiKit.Card(right, 24, 12, "Deck");
            var dh = UiKit.HBox(deck, 10, 0, TextAnchor.MiddleLeft, "Head");
            UiKit.Heading(dh, "내 덱", "HUD 슬롯 순서");
            UiKit.Spacer(dh);
            UiKit.Button(dh, "덱 편성", BtnStyle.Ghost, () => Ui.Go(MetaScreenId.Spellbook), -1, 46, UiTheme.FontSmall);
            if (MetaState.Deck.Count == 0) UiKit.Label(deck, "편성된 덱이 없습니다.", UiTheme.FontSmall, UiTheme.Text3);
            else
            {
                var line = UiKit.HBox(deck, 14, 0, TextAnchor.UpperLeft, "Line");
                for (int i = 0; i < MetaState.Deck.Count; i++)
                {
                    var fig = UiKit.VBox(line, 6, 0, TextAnchor.UpperCenter, "Slot");
                    UiKit.Size(fig, 116, -1);
                    var ic = UiKit.HBox(fig, 0, 0, TextAnchor.MiddleCenter, "Icon");
                    UiKit.SkillIcon(ic, MetaState.Deck[i], 68);
                    UiKit.Label(fig, $"{i + 1}. {MetaSkills.Name(MetaState.Deck[i])}", UiTheme.FontSmall - 3, UiTheme.Text2, false, TextAnchor.MiddleCenter);
                }
            }

            var recent = UiKit.Card(right, 24, 10, "Recent");
            UiKit.Size(recent, -1, -1, -1, 1);
            UiKit.Heading(recent, "최근 대전");
            if (_loadError != null) UiKit.Note(recent, _loadError, Tone.Enemy);
            else if (_matches == null) UiKit.Label(recent, "불러오는 중…", UiTheme.FontSmall, UiTheme.Text3);
            else if (_matches.Length == 0) UiKit.Label(recent, MetaState.Offline ? "오프라인 모드에서는 전적이 없습니다." : "아직 대전 기록이 없습니다. 첫 대전을 시작해보세요!", UiTheme.FontSmall, UiTheme.Text3);
            else
            {
                var sr = UiKit.Scroll(recent, out var content, 4);
                UiKit.Size(sr, -1, 262, -1, 1);
                Row(content, "결과", "모드", "상대", "시간", "시전", "레이팅", "언제", true, UiTheme.Text3, UiTheme.Text3);
                for (int i = 0; i < Mathf.Min(10, _matches.Length); i++)
                {
                    var m = _matches[i];
                    bool win = m.result == "WIN";
                    string word = win ? "VICTORY" : m.result == "DRAW" ? "DRAW" : "DEFEAT";
                    string delta = m.ratingDelta > 0 ? "+" + m.ratingDelta : m.ratingDelta < 0 ? m.ratingDelta.ToString() : "±0";
                    Color dc = m.ratingDelta > 0 ? UiTheme.PlayHi : m.ratingDelta < 0 ? UiTheme.Enemy : UiTheme.Text3;
                    Row(content, word, MetaState.ModeName(m.mode), "vs " + m.opponent, MetaState.Mmss(m.durationSec), m.casts + "회", delta, MetaState.TimeAgo(m.recordedAt),
                        false, win ? UiTheme.Cyan : UiTheme.Enemy, dc);
                }
            }

            if (!MetaState.Offline) BuildAccount(right);
        }

        private static void Row(Transform parent, string a, string b, string c, string d, string e, string f, string g, bool head, Color first, Color delta)
        {
            var r = UiKit.HBox(parent, 10, 0, TextAnchor.MiddleLeft, "Row");
            UiKit.Size(r, -1, head ? 34 : 40);
            int fs = head ? UiTheme.FontLabel - 2 : UiTheme.FontSmall;
            Cell(r, a, 150, fs, first, true);
            Cell(r, b, 130, fs, head ? UiTheme.Text3 : UiTheme.Text2, false);
            var oc = Cell(r, c, -1, fs, head ? UiTheme.Text3 : UiTheme.Text, false);
            UiKit.Size(oc, -1, -1, 1);
            Cell(r, d, 80, fs, head ? UiTheme.Text3 : UiTheme.Text2, false);
            Cell(r, e, 70, fs, head ? UiTheme.Text3 : UiTheme.Text2, false);
            Cell(r, f, 90, fs, delta, !head);
            Cell(r, g, 110, fs, UiTheme.Text3, false);
        }

        private static Text Cell(Transform r, string text, float w, int size, Color color, bool bold)
        {
            var t = UiKit.Label(r, text, size, color, bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (w > 0) UiKit.Size(t, w, -1);
            return t;
        }

        private static void Trio(Transform parent, string v1, string l1, Color c1, string v2, string l2, Color c2, string v3, string l3, Color c3)
        {
            var row = UiKit.HBox(parent, 12, 0, TextAnchor.MiddleCenter, "Trio");
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            void One(string v, string l, Color c)
            {
                var box = UiKit.VBox(row, 2, 8, TextAnchor.MiddleCenter, "Stat");
                var img = box.gameObject.AddComponent<Image>();
                img.sprite = UiTheme.Cut(UiTheme.Panel, UiTheme.Line, 0, 2);
                img.type = Image.Type.Sliced;
                UiKit.Size(box, -1, -1, 1);
                UiKit.Label(box, v, UiTheme.FontTitle + 4, c, true, TextAnchor.MiddleCenter);
                UiKit.Label(box, l, UiTheme.FontSmall - 3, UiTheme.Text3, false, TextAnchor.MiddleCenter);
            }
            One(v1, l1, c1);
            One(v2, l2, c2);
            if (v3 != null) One(v3, l3, c3);
        }

        private void BuildAccount(Transform parent)
        {
            var acc = UiKit.Card(parent, 24, 12, "Account");
            var head = UiKit.HBox(acc, 10, 0, TextAnchor.MiddleLeft, "Head");
            UiKit.Heading(head, "계정 관리");
            UiKit.Spacer(head);
            UiKit.Label(head, MetaState.User != null ? MetaState.User.email : "", UiTheme.FontSmall - 2, UiTheme.Text3).horizontalOverflow = HorizontalWrapMode.Overflow;
            var two = UiKit.HBox(acc, 24, 0, TextAnchor.UpperLeft, "Two");
            two.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;

            var nickCol = UiKit.VBox(two, 8, 0, TextAnchor.UpperLeft, "Nick");
            UiKit.Size(nickCol, -1, -1, 1);
            UiKit.Label(nickCol, "닉네임 변경", UiTheme.FontLabel, UiTheme.Text2, true);
            var nr = UiKit.HBox(nickCol, 8, 0, TextAnchor.MiddleLeft, "Row");
            var nick = UiKit.Input(nr, MetaState.Nickname, false, 12);
            UiKit.Size(nick, -1, -1, 1);
            Button nb = null;
            nb = UiKit.Button(nr, "변경", BtnStyle.Default, () =>
            {
                string v = nick.text.Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^[A-Za-z0-9가-힣_]{2,12}$")) { MetaUiRoot.Toast("닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.", Tone.Enemy); return; }
                var done = UiKit.Busy(nb, "변경 중…");
                ProfileService.UpdateNickname(v, _ => { done(); MetaUiRoot.Toast("닉네임을 바꿨습니다.", Tone.Play); MetaState.RefreshProfile(); },
                                              e => { done(); MetaUiRoot.Toast(e, Tone.Enemy); });
            });

            var pwCol = UiKit.VBox(two, 8, 0, TextAnchor.UpperLeft, "Password");
            UiKit.Size(pwCol, -1, -1, 1);
            UiKit.Label(pwCol, "비밀번호 변경", UiTheme.FontLabel, UiTheme.Text2, true);
            var cur = UiKit.Input(pwCol, "현재 비밀번호", true);
            var next = UiKit.Input(pwCol, "새 비밀번호 (6자 이상)", true);
            Button pb = null;
            pb = UiKit.Button(pwCol, "비밀번호 변경", BtnStyle.Default, () =>
            {
                if (next.text.Length < 6) { MetaUiRoot.Toast("새 비밀번호는 6자 이상이어야 합니다.", Tone.Enemy); return; }
                var done = UiKit.Busy(pb, "변경 중…");
                AuthService.ChangePassword(cur.text, next.text, () => { done(); cur.text = next.text = ""; MetaUiRoot.Toast("비밀번호를 바꿨습니다.", Tone.Play); },
                                           e => { done(); MetaUiRoot.Toast(e, Tone.Enemy); });
            });
        }
    }
}
