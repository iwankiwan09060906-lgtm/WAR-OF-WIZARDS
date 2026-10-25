// 제스처 튜토리얼 — VR UI 프로토타입(Figma Make, 이식 후 삭제)의 TutorialScreen.tsx 를 이 게임의 실제 손동작으로 옮김 (C02 로비 → 튜토리얼)
//   왼쪽: 동작 목록 5개 (본 동작은 청록 "확인") · 가운데: 큰 표시 + 이름 · 설명 · PC 키 · 연습 구역 · 오른쪽: n / 5 · 다음 · 건너뛰기
//   프로토타입의 Fireball / Shield / Lightning 3동작 대신 시전 흐름 그대로:
//     핀치로 룬 그리기 → 주먹(인식) → 검지 포인팅 2초(조준 · 시전) · 손바닥(취소) · 왼손 휘두르기(이동)
//   "연습" = 스킬 테스트 봇전 (덱 밖의 19종 룬도 바로 시전, 쿨타임 · 횟수 없음).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class TutorialScreen : MetaScreen
    {
        private struct Step
        {
            public string Name, Gesture, Description, Hold, PcKeys, Art;
            public Color Color;
        }

        private static readonly Step[] Steps =
        {
            new Step
            {
                Name = "룬 그리기", Gesture = "Pinch Draw", Color = UiTheme.PurpleLight, Art = "S03",
                Description = "오른손 엄지와 검지를 집은 채 허공에 룬을 그립니다.\n여러 획이면 손가락을 뗐다가 다시 집어 이어 그리세요.",
                Hold = "그린 모양은 덱 8장의 룬과 비교해 가장 닮은 스킬로 인식됩니다",
                PcKeys = "마우스 왼쪽 드래그",
            },
            new Step
            {
                Name = "주먹", Gesture = "Closed Fist", Color = UiTheme.Orange, Art = "S08",
                Description = "다 그렸으면 주먹을 쥐어 룬을 확정합니다.\n조준이 필요 없는 스킬(S08 · S09 · S10 · H01 · H04)은 여기서 바로 시전됩니다.",
                Hold = "인식이 애매하면 \"다시 그려 주세요\" 안내가 나옵니다",
                PcKeys = "F",
            },
            new Step
            {
                Name = "포인팅 조준", Gesture = "Index Point", Color = UiTheme.CyanLight, Art = "S06",
                Description = "검지를 펴서 노릴 열(· 깊이)을 가리킵니다.\n같은 곳을 2초 동안 유지하면 시전이 확정됩니다.",
                Hold = "가리킨 곳에 조준 표시가 차오르면 시전",
                PcKeys = "G → 마우스로 조준",
            },
            new Step
            {
                Name = "손바닥 취소", Gesture = "Open Palm", Color = UiTheme.Enemy, Art = "S05",
                Description = "룬을 그린 뒤 · 조준 중에 오른손 손바닥을 활짝 펴면 취소됩니다.\n취소하면 쿨타임 · 횟수가 줄지 않습니다.",
                Hold = "잘못 그렸을 때 바로 손바닥",
                PcKeys = "X 또는 마우스 오른쪽",
            },
            new Step
            {
                Name = "왼손 이동", Gesture = "Left Swipe", Color = UiTheme.Green, Art = "S09",
                Description = "왼손 손가락을 모두 펴고 왼쪽 · 오른쪽으로 휘두르면 한 칸씩 움직입니다.\n날아오는 마법을 피할 때 쓰세요.",
                Hold = "빠르게 짧게 휘두르기 (0.5초 간격)",
                PcKeys = "A / D",
            },
        };

        private int _active;
        private readonly HashSet<int> _seen = new HashSet<int>();

        public override string Title => "제스처 튜토리얼";

        protected override void OnOpen()
        {
            _active = 0;
            _seen.Clear();
            _seen.Add(0);
        }

        protected override void Build(RectTransform body)
        {
            // 머리글 오른쪽: 진행 점 + n/5 확인
            var dots = UiKit.HBox(Ui.HeaderRight, 10, 0, TextAnchor.MiddleRight, "Progress");
            for (int i = 0; i < Steps.Length; i++)
                UiKit.Dot(dots, _seen.Contains(i) && i != _active ? UiTheme.CyanLight : i == _active ? UiTheme.Purple : UiTheme.Alpha(UiTheme.Purple, 0.3f), 14);
            UiKit.Label(dots, $"{_seen.Count}/{Steps.Length} 확인", UiTheme.FontSmall, UiTheme.PurpleLight).horizontalOverflow = HorizontalWrapMode.Overflow;

            var row = UiKit.Stretch(UiKit.HBox(body, 28, 0, TextAnchor.UpperLeft, "Tutorial"));
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            BuildList(row);
            BuildDetail(row);
            BuildNav(row);
        }

        private void BuildList(Transform parent)
        {
            var col = UiKit.VBox(parent, 12, 0, TextAnchor.UpperLeft, "List");
            UiKit.Size(col, 400, -1);
            for (int i = 0; i < Steps.Length; i++)
            {
                int idx = i;
                var s = Steps[i];
                bool on = i == _active;
                var b = UiKit.Button(col, "", BtnStyle.Ghost, () => Select(idx), -1, 104);
                b.GetComponent<Image>().sprite = on
                    ? UiTheme.Cut(new Color(0.30f, 0.14f, 0.62f, 0.4f), UiTheme.Alpha(UiTheme.Purple, 0.65f), 10, 2)
                    : UiTheme.Cut(new Color(0.12f, 0.06f, 0.28f, 0.2f), UiTheme.Alpha(UiTheme.Purple, 0.2f), 8, 2);
                UiKit.Clear(b.transform);
                if (on) UiKit.Halo(b.transform, UiTheme.Purple, 0.16f, 24, 16);
                var r = UiKit.Stretch(UiKit.HBox(b.transform, 16, 0, TextAnchor.MiddleLeft, "Row"), 16, 0, 16, 0);
                r.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                Medallion(r, i, s.Color, 66, on);
                var txt = UiKit.VBox(r, 2, 0, TextAnchor.MiddleLeft, "Text");
                UiKit.Size(txt, -1, -1, 1);
                UiKit.Label(txt, s.Name, UiTheme.FontBody, s.Color, true);
                UiKit.Kicker(txt, s.Gesture, UiTheme.Alpha(UiTheme.PurpleLight, 0.75f), TextAnchor.MiddleLeft, 12);
                if (_seen.Contains(i) && !on) UiKit.Label(txt, "* 확인", UiTheme.FontSmall - 5, UiTheme.CyanLight, true);
            }
        }

        private void BuildDetail(Transform parent)
        {
            var s = Steps[_active];
            var col = UiKit.VBox(parent, 16, 0, TextAnchor.MiddleCenter, "Detail");
            col.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Size(col, -1, -1, 1);

            // 큰 표시: 빛 번짐 + 발동 마법진 + 번호 메달
            var big = UiKit.Node(col, "Big");
            UiKit.Size(big, 230, 230);
            var glow = UiKit.Panel(big, UiTheme.Alpha(s.Color, 0.45f), null, "Glow");
            glow.sprite = UiTheme.Glow();
            UiKit.Stretch(glow.rectTransform, -70, -70, -70, -70);
            var art = UiKit.SkillIcon(big, s.Art, 230);
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.anchoredPosition = Vector2.zero;
            art.sizeDelta = new Vector2(230, 230);
            var med = Medallion(big, _active, s.Color, 80, true);
            med.anchorMin = med.anchorMax = new Vector2(1f, 0f);
            med.pivot = new Vector2(1f, 0f);
            med.sizeDelta = new Vector2(80, 80);
            med.anchoredPosition = new Vector2(6f, -6f);

            UiKit.Kicker(col, "Spell Gesture", UiTheme.PurpleLight, TextAnchor.MiddleCenter, 15);
            UiKit.Display(col, s.Name, 60, s.Color, TextAnchor.MiddleCenter, true);
            var chipRow = UiKit.HBox(col, 0, 0, TextAnchor.MiddleCenter, "Gesture");
            var chip = UiKit.Chip(chipRow, s.Gesture.ToUpperInvariant(), Tone.Neutral, UiTheme.FontSmall - 2);
            chip.color = s.Color;
            chip.font = UiTheme.Display;
            var desc = UiKit.Label(col, s.Description, UiTheme.FontBody, UiTheme.Text2, false, TextAnchor.MiddleCenter);
            UiKit.Size(desc, 820, -1);

            var tip = UiKit.CardRow(col, 18, 18, "Tip", new Color(0.12f, 0.06f, 0.28f, 0.25f), UiTheme.Alpha(UiTheme.Purple, 0.3f));
            UiKit.Size(tip, 820, -1);
            var tipTxt = UiKit.VBox(tip, 4, 0, TextAnchor.MiddleLeft, "Text");
            UiKit.Size(tipTxt, -1, -1, 1);
            UiKit.Label(tipTxt, s.Hold, UiTheme.FontSmall, UiTheme.Text, false);
            UiKit.Label(tipTxt, $"PC 테스트 키 · <color={UiKit.Hex(s.Color)}><b>{s.PcKeys}</b></color>", UiTheme.FontSmall - 2, UiTheme.Text3, false);

            // 연습 구역 (프로토타입 Practice Zone) — 실제 스킬 테스트 봇전으로
            var zone = UiKit.Card(col, 22, 10, "Practice", new Color(0f, 0f, 0f, 0.15f), UiTheme.Alpha(s.Color, 0.45f));
            zone.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            zone.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Size(zone, 620, -1);
            UiKit.Label(zone, "연습 구역 — 봇전에서 19종 룬을 직접 그려 보세요", UiTheme.FontSmall, UiTheme.PurpleLight, false, TextAnchor.MiddleCenter);
            var practice = UiKit.Button(zone, "연습 시작", BtnStyle.Info, StartPractice, 260, 56);
            practice.GetComponentInChildren<Text>().color = s.Color;
        }

        private void BuildNav(Transform parent)
        {
            var col = UiKit.VBox(parent, 12, 0, TextAnchor.LowerCenter, "Nav");
            UiKit.Size(col, 300, -1);
            col.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            UiKit.Spacer(col);
            UiKit.Kicker(col, $"{_active + 1} of {Steps.Length}", UiTheme.Alpha(UiTheme.PurpleLight, 0.75f), TextAnchor.MiddleCenter, 14);
            if (_active < Steps.Length - 1)
            {
                var next = UiKit.Button(col, "다음 →", BtnStyle.Play, () => Select(_active + 1), -1, 66, UiTheme.FontTitle - 4);
                UiKit.Halo(next.transform, UiTheme.Purple);
            }
            else
            {
                var go = UiKit.Button(col, "전투 연습 시작", BtnStyle.Play, StartPractice, -1, 66, UiTheme.FontTitle - 4);
                UiKit.Halo(go.transform, UiTheme.Purple);
            }
            if (_active > 0) UiKit.Button(col, "← 이전", BtnStyle.Ghost, () => Select(_active - 1), -1, 54);
            UiKit.Button(col, "건너뛰기", BtnStyle.Text, () => Ui.Go(MetaScreenId.Lobby), -1, 48, UiTheme.FontSmall);
        }

        private void Select(int idx)
        {
            _active = Mathf.Clamp(idx, 0, Steps.Length - 1);
            _seen.Add(_active);
            Redraw();
        }

        private static void StartPractice()
        {
            if (MetaState.Deck.Count == 0) MetaState.Deck = new List<string> { "S03", "S07", "S08", "S01", "S11", "S13", "H01", "H04" };
            MetaFlow.Instance.StartMatch(new MatchContext { Mode = "", OpponentName = "Bot (연습)", OpponentRating = MetaState.Rating, OpponentBot = true });
        }

        /// <summary>로마 숫자 메달 (색 고리 + Cinzel 숫자) — 프로토타입 GestureIcon 자리</summary>
        private static RectTransform Medallion(Transform parent, int index, Color color, float size, bool active)
        {
            var rt = UiKit.Node(parent, "Medal");
            UiKit.Size(rt, size, size);
            var fill = rt.gameObject.AddComponent<Image>();
            fill.sprite = UiTheme.Circle();
            fill.color = new Color(color.r * 0.18f, color.g * 0.18f, color.b * 0.18f, 0.92f);
            fill.raycastTarget = false;
            var ring = UiKit.Panel(rt, active ? color : UiTheme.Alpha(color, 0.5f), null, "Ring");
            ring.sprite = UiTheme.Ring(128, 0.06f);
            UiKit.Stretch(ring.rectTransform);
            var n = UiKit.Label(rt, new[] { "I", "II", "III", "IV", "V" }[index], (int)(size * 0.38f), color, true, TextAnchor.MiddleCenter);
            n.font = UiTheme.Display;
            n.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Stretch(n.rectTransform);
            return rt;
        }
    }
}
