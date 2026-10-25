// C04 환경설정 — VR UI 프로토타입(Figma Make, 이식 후 삭제)의 SettingsScreen.tsx (+ 웹 pages/settings.js 의 저장 방식)
//   2 × 2 섹션 카드: 카메라(보라) · 사운드(청록) · 제스처 인식(주황) · 접근성(보라). 카드 머리 = Cinzel 영문 + 한글 제목 + 아래 선,
//   슬라이더 · 토글은 섹션 색으로 빛난다. 아래에 저장 막대("저장 후 돌아가기").
//   이 기기에 자동 저장 (PlayerPrefs "wow.settings.v1"). 효과음 크기 · 비네팅 · UI 크게는 바로 게임에 반영된다.

using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class SettingsScreen : MetaScreen
    {
        private enum Kind { Switch, Range }

        private struct Item
        {
            public Kind Kind;
            public string Label, Desc, Unit;
            public int Min, Max;
            public Func<MetaSettings, int> GetInt;
            public Action<MetaSettings, int> SetInt;
            public Func<MetaSettings, bool> GetBool;
            public Action<MetaSettings, bool> SetBool;
        }

        public override string Title => "환경설정";

        private static Item R(string label, string desc, int min, int max, string unit, Func<MetaSettings, int> get, Action<MetaSettings, int> set) =>
            new Item { Kind = Kind.Range, Label = label, Desc = desc, Min = min, Max = max, Unit = unit, GetInt = get, SetInt = set };

        private static Item S(string label, string desc, Func<MetaSettings, bool> get, Action<MetaSettings, bool> set) =>
            new Item { Kind = Kind.Switch, Label = label, Desc = desc, GetBool = get, SetBool = set };

        private static readonly (string title, string english, Color color, Item[] items)[] Groups =
        {
            ("카메라 · 멀미 방지", "Camera", UiTheme.Purple, new[]
            {
                R("카메라 감도", null, 0, 100, "%", s => s.camSensitivity, (s, v) => s.camSensitivity = v),
                S("비네팅", "열을 옮길 때 시야 가장자리를 어둡게 하여 멀미를 줄입니다.", s => s.comfortVignette, (s, v) => s.comfortVignette = v),
                S("스냅 이동", "왼손 스냅으로 좌/중/우 열을 한 칸씩 순간 이동합니다.", s => s.snapTurning, (s, v) => s.snapTurning = v),
            }),
            ("사운드", "Audio", UiTheme.Cyan, new[]
            {
                R("효과음", null, 0, 100, "%", s => s.sfx, (s, v) => s.sfx = v),
                R("배경음", null, 0, 100, "%", s => s.bgm, (s, v) => s.bgm = v),
                S("공간 음향", "소리가 나는 방향으로 들립니다.", s => s.spatialAudio, (s, v) => s.spatialAudio = v),
            }),
            ("제스처 인식", "Gesture Recognition", UiTheme.Orange, new[]
            {
                R("인식 민감도", null, 0, 100, "%", s => s.gestureSensitivity, (s, v) => s.gestureSensitivity = v),
                R("유지 시간 (포인팅)", "검지로 조준을 확정하기까지 필요한 프레임 수 (웹캠 손 추적)", 5, 45, "", s => s.holdFrames, (s, v) => s.holdFrames = v),
                S("햅틱 피드백", "시전 · 피격 때 컨트롤러 진동", s => s.haptics, (s, v) => s.haptics = v),
            }),
            ("접근성", "Accessibility", UiTheme.Purple, new[]
            {
                S("자막", "전투 이벤트를 화면 하단에 큰 글씨로 표시합니다.", s => s.subtitles, (s, v) => s.subtitles = v),
                S("UI 크게", "글자와 버튼을 조금 더 크게 보여 줍니다.", s => s.uiLarge, (s, v) => s.uiLarge = v),
                R("포인터 크기", "메뉴에서 손이 가리키는 점의 크기", 0, 100, "%", s => s.pointerSize, (s, v) => s.pointerSize = v),
                S("레이 선 표시", "점 말고 손에서 나오는 선도 보입니다 (기본 꺼짐)", s => s.showRayLine, (s, v) => s.showRayLine = v),
                S("색약 모드", "적/아군 표시를 색 외에 무늬로도 구분합니다.", s => s.colorblind, (s, v) => s.colorblind = v),
            }),
        };

        protected override void Build(RectTransform body)
        {
            UiKit.Button(Ui.HeaderRight, "기본값", BtnStyle.Ghost, () =>
            {
                MetaState.ResetSettings();
                Redraw();
                MetaUiRoot.Toast("기본값으로 되돌렸습니다.", Tone.Play);
            }, -1, 50, UiTheme.FontSmall);

            var col = UiKit.Stretch(UiKit.VBox(body, 18, 0, TextAnchor.UpperLeft, "Settings"));
            var grid = UiKit.Grid(col, new Vector2(894, 410), new Vector2(28, 22), 2, "Groups");
            UiKit.Size(grid, -1, 2 * 410 + 22);
            var s = MetaState.Settings;
            foreach (var (title, english, color, items) in Groups)
            {
                var card = UiKit.Card(grid, 26, 10, "Group", new Color(0.12f, 0.06f, 0.28f, 0.22f), UiTheme.Alpha(UiTheme.Purple, 0.32f));
                // 섹션 머리: 색 마름모 · 영문(Cinzel) · 한글 제목 · 아래 선
                var sectionHead = UiKit.HBox(card, 14, 0, TextAnchor.MiddleLeft, "SectionHead");
                sectionHead.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                var gem = UiKit.Node(sectionHead, "Gem");
                UiKit.Size(gem, 18, 18);
                var d = UiKit.Panel(gem, color);
                d.rectTransform.sizeDelta = new Vector2(12, 12);
                d.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
                UiKit.Display(sectionHead, english, UiTheme.FontTitle - 2, UiTheme.Hex(0xede9fe));
                UiKit.Label(sectionHead, title, UiTheme.FontSmall - 2, UiTheme.Alpha(color == UiTheme.Purple ? UiTheme.PurpleLight : color, 0.9f));
                UiKit.Size(UiKit.Panel(card, UiTheme.Alpha(UiTheme.Purple, 0.25f), null, "SectionLine"), -1, 1);
                foreach (var it in items)
                {
                    var item = it;
                    if (item.Kind == Kind.Switch)
                    {
                        var row = UiKit.HBox(card, 16, 0, TextAnchor.MiddleLeft, "Row");
                        var txt = UiKit.VBox(row, 2, 0, TextAnchor.MiddleLeft, "Text");
                        UiKit.Size(txt, -1, -1, 1);
                        UiKit.Label(txt, item.Label, UiTheme.FontBody, UiTheme.Text, true);
                        if (item.Desc != null) UiKit.Label(txt, item.Desc, UiTheme.FontSmall - 3, UiTheme.Text3);
                        UiKit.Switch(row, item.GetBool(s), v => { item.SetBool(MetaState.Settings, v); MetaState.SaveSettings(); }, color);
                    }
                    else
                    {
                        var head = UiKit.HBox(card, 10, 0, TextAnchor.MiddleLeft, "Head");
                        UiKit.Label(head, item.Label, UiTheme.FontBody, UiTheme.Text, true);
                        UiKit.Spacer(head);
                        var val = UiKit.Label(head, item.GetInt(s) + item.Unit, UiTheme.FontTitle - 2, color == UiTheme.Purple ? UiTheme.PurpleLight : color, true, TextAnchor.MiddleRight);
                        val.font = UiTheme.Display;
                        val.horizontalOverflow = HorizontalWrapMode.Overflow;
                        UiKit.Range(card, item.Min, item.Max, item.GetInt(s), true, v =>
                        {
                            item.SetInt(MetaState.Settings, Mathf.RoundToInt(v));
                            val.text = Mathf.RoundToInt(v) + item.Unit;
                            MetaState.SaveSettings();
                        }, color);
                        if (item.Desc != null) UiKit.Label(card, item.Desc, UiTheme.FontSmall - 3, UiTheme.Text3);
                    }
                }
            }
            // 저장 막대 (프로토타입 Save bar)
            var foot = UiKit.HBox(col, 16, 0, TextAnchor.MiddleRight, "SaveBar");
            var fimg = foot.gameObject.AddComponent<Image>();
            fimg.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0.4f), UiTheme.Alpha(UiTheme.Purple, 0.25f), 0, 2);
            fimg.type = Image.Type.Sliced;
            foot.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(24, 14, 10, 10);
            var info = UiKit.VBox(foot, 2, 0, TextAnchor.MiddleLeft, "Info");
            UiKit.Size(info, -1, -1, 1);
            UiKit.Label(info, "변경 사항은 이 기기에 바로 저장됩니다.", UiTheme.FontSmall, UiTheme.Text2);
            UiKit.Label(info, "메뉴는 오른손 레이 + 핀치로 조작하며, 전투에 들어가면 UI 레이는 자동으로 꺼집니다.", UiTheme.FontSmall - 4, UiTheme.Text3);
            var save = UiKit.Button(foot, "저장 후 돌아가기", BtnStyle.Play, () => { MetaState.SaveSettings(); Ui.Go(MetaScreenId.Lobby); }, 280, 60);
            UiKit.Halo(save.transform, UiTheme.Purple, 0.18f, 30, 18);
        }
    }
}
