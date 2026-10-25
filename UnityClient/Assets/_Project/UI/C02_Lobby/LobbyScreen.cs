// C02 메인 로비 — VR UI 프로토타입(Figma Make, 이식 후 삭제)의 StartScreen.tsx 를 바탕으로
//   가운데: "— VR MAGIC BATTLE —" · 두 줄 Cinzel 로고(그라데이션) · 마름모 구분선 · 세로 버튼 (primary 랭킹전 / secondary 친선전 · 봇 연습전 / 튜토리얼)
//          뒤로 배경 마법진이 진하게 돈다 (프로토타입은 로고 뒤 마법진 불투명도 100%).
//   왼쪽 판: 내 마법사 (이름 · 등급 · 전적) + 내 마법서 8장 → 마법서 편성
//   오른쪽 판: 메뉴 (마법서 · 마이페이지 · 친구 · 우편함 · 랭킹 · 환경설정) + 스킬 테스트 (19종 전부)
//   프로토타입의 Start/Tutorial/Settings 3버튼은 이 게임의 대전 종류(랭킹전 · 친선전 · 봇)와 튜토리얼로 나눠 옮겼다.

using SpellboundVR.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class LobbyScreen : MetaScreen
    {
        private int _onlineFriends = -1;

        public override string Title => null;
        public override bool ShowBackToLobby => false;
        public override float CircleAlpha => 0.42f;

        protected override void OnOpen()
        {
            _onlineFriends = -1;
            CountFriends();
            MetaState.RefreshMails();
            MetaState.RefreshProfile();
        }

        private void CountFriends()
        {
            if (MetaState.Offline || !AuthService.IsLoggedIn) return;
            FriendService.List(false, list =>
            {
                int n = 0;
                foreach (var f in list) if (f.relation == "friend" && MetaState.Online.Contains(f.friendPlayerId)) n++;
                _onlineFriends = n;
                Redraw();
            });
        }

        protected override void HandleStateChanged(string key)
        {
            if (key == "online") CountFriends();
            if (key == "profile" || key == "deck" || key == "mails") Redraw();
        }

        protected override void Build(RectTransform body)
        {
            var row = UiKit.Stretch(UiKit.HBox(body, 40, 0, TextAnchor.MiddleCenter, "Lobby"));
            var hg = row.GetComponent<HorizontalLayoutGroup>();
            hg.childForceExpandHeight = false;
            hg.childForceExpandWidth = false;
            string problem = MetaState.DeckProblem();
            BuildPlayer(row, problem);
            BuildCenter(row, problem);
            BuildMenu(row);
        }

        // ── 가운데: 로고 + 버튼 ─────────────────────────────────

        private void BuildCenter(Transform parent, string problem)
        {
            var col = UiKit.VBox(parent, 0, 0, TextAnchor.MiddleCenter, "Center");
            col.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Size(col, -1, -1, 1);

            UiKit.Kicker(col, "— VR Magic Battle —", UiTheme.CyanLight, TextAnchor.MiddleCenter, 18);
            UiKit.Spacer(col, 0, 10);
            var l1 = UiKit.Display(col, "War of", 104, null, TextAnchor.MiddleCenter, true);
            UiGradient.Apply(l1, UiTheme.PurplePale, UiTheme.Purple, UiTheme.Cyan);
            UiKit.Size(l1, -1, 112);
            var l2 = UiKit.Display(col, "Wizards", 104, UiTheme.Cyan, TextAnchor.MiddleCenter, true);
            UiGradient.Apply(l2, UiTheme.Cyan, UiTheme.Purple, UiTheme.PurplePale);
            UiKit.Size(l2, -1, 112);
            UiKit.Spacer(col, 0, 14);
            UiKit.Divider(col, 260);
            UiKit.Spacer(col, 0, 44);

            var buttons = UiKit.VBox(col, 16, 0, TextAnchor.MiddleCenter, "Buttons");
            buttons.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            // 대전 종류를 고르면 덱 선택(C09 대전 준비)을 거쳐 시작한다
            var ranked = UiKit.Button(buttons, "랭킹전 매칭", BtnStyle.Play,
                () => DeckBuilderScreen.OpenForBattle(Ui, "랭킹전", () => Ui.Go(MetaScreenId.Queue)), 440, 84, UiTheme.FontTitle + 2);
            ranked.interactable = !MetaState.Offline;
            UiKit.Halo(ranked.transform, UiTheme.Purple);

            var pair = UiKit.HBox(buttons, 16, 0, TextAnchor.MiddleCenter, "Pair");
            pair.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var friendly = UiKit.Button(pair, "친선전", BtnStyle.Info, () => Ui.Go(MetaScreenId.Room), 212, 68, UiTheme.FontTitle - 2);
            friendly.interactable = !MetaState.Offline;
            UiKit.Button(pair, "봇 연습전", BtnStyle.Info, () => DeckBuilderScreen.OpenForBattle(Ui, "봇 연습전",
                () => MetaFlow.Instance.StartMatch(new MatchContext { Mode = MatchResultService.ModeBot, OpponentName = "Bot", OpponentRating = MetaState.Rating, OpponentBot = true })),
                212, 68, UiTheme.FontTitle - 2);
            UiKit.Button(buttons, "튜토리얼", BtnStyle.Ghost, () => Ui.Go(MetaScreenId.Tutorial), 300, 58, UiTheme.FontBody);

            UiKit.Spacer(col, 0, 30);
            UiKit.Label(col, "손가락으로 가리키고 엄지 · 검지를 집으면 선택", UiTheme.FontSmall - 2, UiTheme.Alpha(UiTheme.PurpleLight, 0.6f), false, TextAnchor.MiddleCenter)
                 .horizontalOverflow = HorizontalWrapMode.Overflow;
            if (MetaState.Offline)
            {
                UiKit.Spacer(col, 0, 10);
                UiKit.Chip(col, "오프라인 모드 · 봇 연습전만 가능 · 전적 저장 안 함", Tone.Amber, UiTheme.FontSmall - 2);
            }
        }

        // ── 왼쪽: 내 마법사 · 마법서 ─────────────────────────────

        private void BuildPlayer(Transform parent, string problem)
        {
            var card = SidePanel(parent, "Player");
            UiKit.Kicker(card, "Wizard", UiTheme.Alpha(UiTheme.PurpleLight, 0.8f), TextAnchor.MiddleLeft, 14);
            var who = UiKit.HBox(card, 16, 0, TextAnchor.MiddleLeft, "Who");
            who.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Wizard(who, false, 110);
            var txt = UiKit.VBox(who, 4, 0, TextAnchor.MiddleLeft, "Text");
            UiKit.Size(txt, -1, -1, 1);
            UiKit.Label(txt, MetaState.Nickname, UiTheme.FontTitle, UiTheme.Text, true).horizontalOverflow = HorizontalWrapMode.Overflow;
            var p = MetaState.Profile;
            if (MetaState.Offline) UiKit.Label(txt, "오프라인", UiTheme.FontSmall, UiTheme.Amber);
            else
            {
                UiKit.Label(txt, $"{MetaState.TierOf(MetaState.Rating)} · {MetaState.Rating}", UiTheme.FontSmall, UiTheme.CyanLight, true);
                if (p != null) UiKit.Label(txt, $"{p.wins}승 {p.losses}패 · 승률 {MetaState.WinRate(p.wins, p.losses)}%", UiTheme.FontSmall - 3, UiTheme.Text3);
            }
            Line(card);

            var head = UiKit.HBox(card, 10, 0, TextAnchor.MiddleLeft, "DeckHead");
            UiKit.Kicker(head, "내 마법서", UiTheme.PurpleLight, TextAnchor.MiddleLeft, UiTheme.FontLabel);
            UiKit.Spacer(head);
            UiKit.Button(head, "편성 →", BtnStyle.Text, () => Ui.Go(MetaScreenId.Spellbook), -1, 40, UiTheme.FontSmall - 2);
            if (MetaState.Deck.Count == 0)
                UiKit.Label(card, "아직 덱이 없습니다. 마법서에서 스킬 8장을 편성해야 대전을 시작할 수 있어요.", UiTheme.FontSmall - 2, UiTheme.Amber);
            else
            {
                var grid = UiKit.Grid(card, new Vector2(84, 84), new Vector2(10, 10), 4, "Deck");
                UiKit.Size(grid, -1, 2 * 84 + 10);
                foreach (var code in MetaState.Deck) UiKit.SkillIcon(grid, code, 84);
                if (problem != null) UiKit.Label(card, problem, UiTheme.FontSmall - 3, UiTheme.Amber);
            }
            UiKit.Spacer(card);
            var exits = UiKit.HBox(card, 10, 0, TextAnchor.MiddleCenter, "Exits");
            exits.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            UiKit.Size(UiKit.Button(exits, "로그아웃", BtnStyle.Ghost, () =>
            {
                MetaState.SignOut();
                Ui.Go(MetaScreenId.Login);
            }, 186, 54, UiTheme.FontSmall - 1), 186, -1, 1);
            UiKit.Size(UiKit.Button(exits, "게임 종료", BtnStyle.Danger, () => MetaFlow.Instance.QuitGame(), 186, 54, UiTheme.FontSmall - 1), 186, -1, 1);
        }

        // ── 오른쪽: 메뉴 ───────────────────────────────────────

        private void BuildMenu(Transform parent)
        {
            var card = SidePanel(parent, "Menu");
            UiKit.Kicker(card, "Menu", UiTheme.Alpha(UiTheme.PurpleLight, 0.8f), TextAnchor.MiddleLeft, 14);
            var p = MetaState.Profile;
            int unread = MetaState.UnreadMail() + MetaState.UnreadCount(true);
            Item(card, "마법서", "19종 중 8장 덱 편성", MetaScreenId.Spellbook);
            Item(card, "마이페이지", "전적 · 프로필 · 비밀번호", MetaScreenId.MyPage);
            Item(card, "친구", MetaState.Offline ? "서버 연결 필요" : _onlineFriends < 0 ? "확인 중…" : $"접속 중 {_onlineFriends}명", MetaScreenId.Friends);
            Item(card, "우편함 · 공지", unread > 0 ? $"읽지 않음 {unread}건" : "새 우편 없음", MetaScreenId.Mailbox, unread);
            Item(card, "랭킹", "랭킹전 레이팅 기준", MetaScreenId.Ranking);
            Item(card, "환경설정", "카메라 · 소리 · 제스처", MetaScreenId.Settings);
            UiKit.Spacer(card);

            // 스킬 테스트: 덱과 상관없이 19종 룬을 모두 그려 볼 수 있는 봇전 (GameBootstrap.recognizeAllRunes)
            var test = UiKit.Card(card, 18, 8, "SkillTest", new Color(0.03f, 0.2f, 0.26f, 0.35f), UiTheme.Alpha(UiTheme.Cyan, 0.45f));
            UiKit.Label(test, "스킬 테스트 (19종 전부)", UiTheme.FontBody - 2, UiTheme.Hex(0xcffafe), true);
            UiKit.Label(test, "봇전에서 덱 밖의 룬도 그려서 바로 시전", UiTheme.FontSmall - 4, UiTheme.Text3);
            UiKit.Button(test, "시작", BtnStyle.Info, () =>
            {
                if (MetaState.Deck.Count == 0) MetaState.Deck = new System.Collections.Generic.List<string> { "S03", "S07", "S08", "S01", "S11", "S13", "H01", "H04" };
                MetaFlow.Instance.StartMatch(new MatchContext { Mode = "", OpponentName = "Bot (테스트)", OpponentRating = MetaState.Rating, OpponentBot = true });
            }, -1, 50, UiTheme.FontSmall);
        }

        // ── 부품 ───────────────────────────────────────────────

        private static RectTransform SidePanel(Transform parent, string name)
        {
            var card = UiKit.Card(parent, 26, 12, name, new Color(8 / 255f, 6 / 255f, 24 / 255f, 0.78f), UiTheme.Alpha(UiTheme.Purple, 0.3f));
            UiKit.Size(card, 440, 900);
            return card;
        }

        private static void Line(Transform parent)
        {
            UiKit.Size(UiKit.Panel(parent, UiTheme.Alpha(UiTheme.Purple, 0.25f)), -1, 1);
        }

        private void Item(Transform parent, string title, string sub, MetaScreenId target, int badge = 0)
        {
            var b = UiKit.Button(parent, "", BtnStyle.Ghost, () => Ui.Go(target), -1, 78);
            b.GetComponent<Image>().sprite = UiTheme.Cut(new Color(0.12f, 0.06f, 0.28f, 0.28f), UiTheme.Alpha(UiTheme.Purple, 0.28f), 8, 2);
            UiKit.Clear(b.transform);
            var row = UiKit.Stretch(UiKit.HBox(b.transform, 12, 0, TextAnchor.MiddleLeft, "Row"), 20, 0, 18, 0);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var txt = UiKit.VBox(row, 0, 0, TextAnchor.MiddleLeft, "Text");
            UiKit.Size(txt, -1, -1, 1);
            UiKit.Label(txt, title, UiTheme.FontBody, UiTheme.Text, true);
            UiKit.Label(txt, sub, UiTheme.FontSmall - 4, UiTheme.Text3);
            if (badge > 0) UiKit.Badge(row, badge, 32);
            UiKit.Label(row, "›", UiTheme.FontTitle, UiTheme.PurpleLight, true, TextAnchor.MiddleCenter);
        }


    }
}
