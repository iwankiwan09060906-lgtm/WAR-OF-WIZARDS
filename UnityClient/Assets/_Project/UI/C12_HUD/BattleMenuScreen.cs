// 경기 중 메뉴 (C12) — 왼손 메뉴 제스처(손바닥을 얼굴 쪽으로 펴고 엄지 · 검지 집기) · 컨트롤러 ☰ 버튼 · Esc 로 연다 (MetaFlow).
//   1:1 실시간 대전이라 경기는 멈추지 않는다 — 메뉴가 열려 있는 동안은 룬 그리기 · 이동만 멈춘다.
//   계속하기 / 기권하기(확인 → 패배로 저장 → C13 결과) . 배경 판 없이 띄워 뒤의 전장이 보인다.

using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class BattleMenuScreen : MetaScreen
    {
        private bool _confirm;

        public override string Title => null;
        public override bool ShowBackToLobby => false;
        public override bool Overlay => true;

        protected override void OnOpen() => _confirm = false;

        protected override void Build(RectTransform body)
        {
            var center = UiKit.Stretch(UiKit.VBox(body, 0, 0, TextAnchor.MiddleCenter, "Menu"));
            var lg = center.GetComponent<VerticalLayoutGroup>();
            lg.childForceExpandWidth = false;
            lg.childForceExpandHeight = false;
            var card = UiKit.Card(center, 44, 16, "Card", new Color(6 / 255f, 4 / 255f, 20 / 255f, 0.94f),
                                  UiTheme.Alpha(_confirm ? UiTheme.Enemy : UiTheme.Purple, 0.6f));
            card.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            UiKit.Size(card, 640, -1);
            UiKit.Halo(card, _confirm ? UiTheme.Enemy : UiTheme.Purple, 0.18f, 60, 60);

            if (!_confirm)
            {
                UiKit.Kicker(card, "Battle Menu", UiTheme.PurpleLight, TextAnchor.MiddleCenter, 16);
                UiKit.Display(card, "경기 메뉴", 52, UiTheme.Text, TextAnchor.MiddleCenter, true);
                UiKit.Divider(card, 200);
                UiKit.Label(card, "1:1 실시간 대전이라 경기는 멈추지 않습니다.\n메뉴가 열려 있는 동안 룬 그리기와 이동만 멈춥니다.",
                            UiTheme.FontSmall, UiTheme.Text2, false, TextAnchor.MiddleCenter);
                var resume = UiKit.Button(card, "계속하기", BtnStyle.Play, () => MetaFlow.Instance.CloseBattleMenu(), 460, 76, UiTheme.FontTitle);
                UiKit.Halo(resume.transform, UiTheme.Purple);
                UiKit.Button(card, "기권하기", BtnStyle.Danger, () => { _confirm = true; Redraw(); }, 460, 64, UiTheme.FontBody);
                UiKit.Label(card, "왼손 메뉴 제스처 · 컨트롤러 ☰ 버튼으로 다시 닫을 수 있습니다", UiTheme.FontSmall - 4, UiTheme.Text3, false, TextAnchor.MiddleCenter);
            }
            else
            {
                UiKit.Kicker(card, "Forfeit", UiTheme.Hex(0xf87171), TextAnchor.MiddleCenter, 16);
                UiKit.Display(card, "기권할까요?", 48, UiTheme.Enemy, TextAnchor.MiddleCenter, true);
                UiKit.Label(card, "이 경기는 패배로 기록되고 결과 화면으로 넘어갑니다.\n결과 화면에서 로비로 가거나 게임을 종료할 수 있습니다.",
                            UiTheme.FontSmall, UiTheme.Text2, false, TextAnchor.MiddleCenter);
                UiKit.Button(card, "기권하고 결과 보기", BtnStyle.Danger, () => MetaFlow.Instance.Forfeit(), 460, 72, UiTheme.FontTitle - 2);
                UiKit.Button(card, "돌아가기", BtnStyle.Ghost, () => { _confirm = false; Redraw(); }, 460, 60, UiTheme.FontBody);
            }
        }
    }
}
