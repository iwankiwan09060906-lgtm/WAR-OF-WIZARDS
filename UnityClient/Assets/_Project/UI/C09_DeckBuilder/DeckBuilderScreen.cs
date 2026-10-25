// C09 마법서 · 덱 편성 — 웹 pages/spellbook.js
//   왼쪽: 19종 스킬 (탭 전체 / 일반 13 / 고위력 6) — 누르면 덱에 넣기 · 빼기
//   오른쪽: 고른 스킬 상세 (그리는 룬 → 발동 마법진, 쿨타임 · 조준 · 제한) + 내 덱 8칸(HUD 슬롯 순서, 앞뒤 이동 · 빼기) + 저장
//   규칙: 정확히 8장, 중복 불가, 고위력 최대 2장 (서버도 같은 규칙으로 다시 검사). 저장하지 않고 나가면 확인 창.
//   대전 준비 모드(OpenForBattle): 로비에서 랭킹전 · 봇 연습전을 누르면 이 화면을 거친다 — "이 덱으로 시작"이
//   (바꾼 덱이 있으면 저장한 뒤) 매칭 · 경기를 시작한다. 흐름: 로그인 → 로비(대전 종류) → 덱 선택 → 경기 → 결과.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class DeckBuilderScreen : MetaScreen
    {
        private const int DeckSize = 8;
        private const int HighLimit = 2;

        private List<string> _draft = new List<string>();
        private int _tab;
        private string _focus = "S01";

        private static string s_battleLabel;
        private static System.Action s_battleStart;

        /// <summary>대전 준비: 덱을 확인 · 수정한 뒤 "이 덱으로 시작"을 누르면 start (매칭 대기 · 경기 시작)</summary>
        public static void OpenForBattle(MetaUiRoot ui, string label, System.Action start)
        {
            s_battleLabel = label;
            s_battleStart = start;
            ui.Go(MetaScreenId.Spellbook);
        }

        private static bool BattleMode => s_battleStart != null;

        public override string Title => BattleMode ? $"대전 준비 · {s_battleLabel} — 덱 선택" : "마법서 · 덱 편성";

        protected override void OnClose()
        {
            s_battleStart = null;
            s_battleLabel = null;
        }

        private bool Saved => string.Join(",", _draft) == string.Join(",", MetaState.Deck);

        private int Highs()
        {
            int n = 0;
            foreach (var c in _draft) if (MetaSkills.IsHigh(c)) n++;
            return n;
        }

        protected override void OnOpen()
        {
            _draft = new List<string>(MetaState.Deck);
            _tab = 0;
            _focus = _draft.Count > 0 ? _draft[0] : "S01";
        }

        public override bool CanLeave(System.Action proceed)
        {
            if (Saved) return true;
            Ui.Confirm("저장하지 않은 덱", "편성한 덱을 저장하지 않고 나갈까요? 바꾼 내용은 사라집니다.", "저장하지 않고 나가기", true, ok => { if (ok) proceed(); });
            return false;
        }

        protected override void Build(RectTransform body)
        {
            UiKit.Chip(Ui.HeaderRight, $"{_draft.Count} / {DeckSize}", _draft.Count == DeckSize ? Tone.Cyan : Tone.Neutral);
            UiKit.Chip(Ui.HeaderRight, $"고위력 {Highs()} / {HighLimit}", Tone.Amber);

            var row = UiKit.Stretch(UiKit.HBox(body, 26, 0, TextAnchor.UpperLeft, "Book"));
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            BuildList(row);
            BuildSide(row);
        }

        private void BuildList(Transform parent)
        {
            var main = UiKit.VBox(parent, 14, 0, TextAnchor.UpperLeft, "Main");
            UiKit.Size(main, -1, -1, 1, 1);
            UiKit.Tabs(main, new[] { "전체 19", "일반 13", "고위력 6" }, _tab, i => { _tab = i; Redraw(); });
            string problem = Validate();
            if (problem != null && _draft.Count > 0) UiKit.Note(main, problem, Tone.Amber);
            var sr = UiKit.Scroll(main, out var content, 0);
            UiKit.Size(sr, -1, -1, -1, 1);
            var grid = UiKit.Grid(content, new Vector2(338, 108), new Vector2(14, 14), 3, "Skills");
            UiKit.Fit(grid);
            bool full = _draft.Count >= DeckSize;
            foreach (var s in MetaSkills.All)
            {
                if (_tab == 1 && s.High) continue;
                if (_tab == 2 && !s.High) continue;
                int slot = _draft.IndexOf(s.Code);
                bool blocked = slot < 0 && (full || (s.High && Highs() >= HighLimit));
                var info = s;
                var b = UiKit.Button(grid, "", BtnStyle.Default, () => Toggle(info.Code));
                Color fill = slot >= 0 ? Color.Lerp(UiTheme.Panel2, UiTheme.Cyan, 0.12f) : UiTheme.Panel2;
                Color border = s.Code == _focus ? UiTheme.Cyan : slot >= 0 ? UiTheme.Cyan2 : UiTheme.Line;
                b.GetComponent<Image>().sprite = UiTheme.Cut(fill, border, 12, 2);
                UiKit.Clear(b.transform);
                var r = UiKit.Stretch(UiKit.HBox(b.transform, 12, 0, TextAnchor.MiddleLeft, "Row"), 14, 6, 14, 6);
                UiKit.SkillIcon(r, s.Code, 76, blocked);
                var txt = UiKit.VBox(r, 2, 0, TextAnchor.MiddleLeft, "Text");
                UiKit.Size(txt, -1, -1, 1);
                UiKit.Label(txt, s.Name, UiTheme.FontBody, blocked ? UiTheme.Text3 : UiTheme.Text, true);
                UiKit.Label(txt, $"{s.Code} · {MetaSkills.Kinds[s.Kind].label} · {s.Cooldown:0}s", UiTheme.FontSmall - 4, UiTheme.Text3);
                if (slot >= 0)
                {
                    var no = UiKit.Badge(b.transform, slot + 1, 34);
                    no.color = UiTheme.Cyan2;
                    no.GetComponent<LayoutElement>().ignoreLayout = true;
                    var nrt = (RectTransform)no.transform;
                    nrt.anchorMin = nrt.anchorMax = new Vector2(1f, 1f);
                    nrt.sizeDelta = new Vector2(34, 34);
                    nrt.anchoredPosition = new Vector2(-24f, -24f);
                }
            }
        }

        private void BuildSide(Transform parent)
        {
            var side = UiKit.VBox(parent, 16, 0, TextAnchor.UpperLeft, "Side");
            UiKit.Size(side, 720, -1, 0, 1);

            // 상세
            MetaSkills.TryGet(_focus, out var s);
            var detail = UiKit.Card(side, 24, 10, "Detail");
            var top = UiKit.HBox(detail, 16, 0, TextAnchor.MiddleLeft, "Top");
            UiKit.SkillIcon(top, s.Code, 64);
            var tt = UiKit.VBox(top, 2, 0, TextAnchor.MiddleLeft, "Title");
            UiKit.Label(tt, s.Name, UiTheme.FontTitle + 4, UiTheme.Text, true);
            UiKit.Label(tt, $"{s.Code} · {(s.High ? "고위력" : "일반")} · {MetaSkills.Kinds[s.Kind].label}", UiTheme.FontSmall - 2, s.High ? UiTheme.Amber : UiTheme.Text3);
            UiKit.Label(detail, s.Effect, UiTheme.FontSmall, UiTheme.Text2);
            var pair = UiKit.HBox(detail, 18, 0, TextAnchor.MiddleCenter, "RuneToCircle");
            Figure(pair, true, s.Code, "그리는 룬 · " + s.Rune);
            UiKit.Label(pair, "→", UiTheme.FontHeadline, UiTheme.Text3, true, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;
            Figure(pair, false, s.Code, "발동하는 마법진");
            var facts = UiKit.HBox(detail, 10, 0, TextAnchor.MiddleLeft, "Facts");
            facts.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Fact(facts, $"{s.Cooldown:0}초", "쿨타임");
            Fact(facts, MetaSkills.Kinds[s.Kind].aim, "조준");
            Fact(facts, s.High ? "게임당 3회" : "없음", "제한");

            // 내 덱 8칸
            var box = UiKit.Card(side, 20, 10, "Deck");
            UiKit.Label(box, "내 덱 · HUD 슬롯 순서 (2줄 × 4칸, 키보드 1~8과 같은 순서)", UiTheme.FontLabel, UiTheme.Text3, true);
            var slots = UiKit.Grid(box, new Vector2(157, 118), new Vector2(10, 10), 4, "Slots");
            UiKit.Size(slots, -1, 246);
            for (int i = 0; i < DeckSize; i++)
            {
                int idx = i;
                var cell = UiKit.Node(slots, "Slot" + (i + 1));
                var img = cell.gameObject.AddComponent<Image>();
                img.sprite = UiTheme.Cut(i < _draft.Count ? UiTheme.Panel2 : UiTheme.Bg2, UiTheme.Line, 10, 2);
                img.type = Image.Type.Sliced;
                var no = UiKit.Label(cell, (i + 1).ToString(), UiTheme.FontSmall - 3, UiTheme.Cyan, true, TextAnchor.UpperLeft);
                UiKit.Stretch((RectTransform)no.transform, 10, 6, 10, 6);
                if (i >= _draft.Count)
                {
                    var e = UiKit.Label(cell, "빈 슬롯", UiTheme.FontSmall - 3, UiTheme.Text3, false, TextAnchor.MiddleCenter);
                    UiKit.Stretch((RectTransform)e.transform);
                    continue;
                }
                string code = _draft[i];
                var col = UiKit.Stretch(UiKit.VBox(cell, 2, 0, TextAnchor.UpperCenter, "Col"), 4, 8, 4, 4);
                var icRow = UiKit.HBox(col, 0, 0, TextAnchor.MiddleCenter, "Icon");
                var focusBtn = UiKit.Button(icRow, "", BtnStyle.Text, () => { _focus = code; Redraw(); }, 52, 52);
                UiKit.Clear(focusBtn.transform);
                UiKit.Stretch(UiKit.SkillIcon(focusBtn.transform, code, 50));
                UiKit.Label(col, MetaSkills.Name(code), UiTheme.FontSmall - 5, UiTheme.Text2, false, TextAnchor.MiddleCenter);
                var tools = UiKit.HBox(col, 4, 0, TextAnchor.MiddleCenter, "Tools");
                var l = UiKit.Button(tools, "◀", BtnStyle.Ghost, () => Move(idx, -1), 40, 28, 16);
                l.interactable = idx > 0;
                var rr = UiKit.Button(tools, "▶", BtnStyle.Ghost, () => Move(idx, 1), 40, 28, 16);
                rr.interactable = idx < _draft.Count - 1;
                UiKit.Button(tools, "×", BtnStyle.Danger, () => { _draft.Remove(code); Redraw(); }, 40, 28, 16);
            }

            UiKit.Spacer(side);
            var foot = UiKit.HBox(side, 12, 0, TextAnchor.MiddleRight, "Foot");
            var clear = UiKit.Button(foot, "비우기", BtnStyle.Ghost, () => { _draft.Clear(); Redraw(); });
            clear.interactable = _draft.Count > 0;
            UiKit.Spacer(foot);
            if (BattleMode)
            {
                // 대전 준비: 저장(바뀐 경우)과 시작을 한 번에
                Button go = null;
                go = UiKit.Button(foot, "이 덱으로 시작  ▶", BtnStyle.Play, () =>
                {
                    string why = Validate();
                    if (why != null) { MetaUiRoot.Toast(why, Tone.Enemy); return; }
                    if (Saved) { StartBattle(); return; }
                    var done = UiKit.Busy(go, "덱 저장 중…");
                    MetaState.SaveDeck(_draft, () => { done(); StartBattle(); }, e => { done(); MetaUiRoot.Toast(e, Tone.Enemy); });
                }, 320, 70, UiTheme.FontTitle - 2);
                go.interactable = Validate() == null;
                UiKit.Halo(go.transform, UiTheme.Purple);
                return;
            }
            Button save = null;
            save = UiKit.Button(foot, Saved ? "저장됨" : "덱 저장", BtnStyle.Play, () =>
            {
                var done = UiKit.Busy(save, "저장 중…");
                MetaState.SaveDeck(_draft, () => { done(); MetaUiRoot.Toast("마법서를 저장했습니다.", Tone.Play); Redraw(); },
                                   e => { done(); MetaUiRoot.Toast(e, Tone.Enemy); });
            }, 240, 62);
            save.interactable = !Saved && Validate() == null;
        }

        private static void StartBattle()
        {
            var start = s_battleStart;
            s_battleStart = null;
            start?.Invoke();
        }

        private static void Figure(Transform parent, bool rune, string code, string caption)
        {
            var f = UiKit.VBox(parent, 8, 0, TextAnchor.UpperCenter, "Figure");
            UiKit.Size(f, 230, -1);
            var holder = UiKit.HBox(f, 0, 0, TextAnchor.MiddleCenter, "Art");
            if (rune) UiKit.RuneIcon(holder, code, 120);
            else UiKit.SkillIcon(holder, code, 120);
            UiKit.Label(f, caption, UiTheme.FontSmall - 3, UiTheme.Text3, false, TextAnchor.MiddleCenter);
        }

        private static void Fact(Transform parent, string value, string label)
        {
            var b = UiKit.VBox(parent, 2, 10, TextAnchor.MiddleCenter, "Fact");
            var img = b.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(UiTheme.Panel, UiTheme.Line, 0, 2);
            img.type = Image.Type.Sliced;
            UiKit.Size(b, -1, -1, 1);
            UiKit.Label(b, value, UiTheme.FontSmall, UiTheme.Text, true, TextAnchor.MiddleCenter);
            UiKit.Label(b, label, UiTheme.FontSmall - 5, UiTheme.Text3, false, TextAnchor.MiddleCenter);
        }

        private void Toggle(string code)
        {
            _focus = code;
            int i = _draft.IndexOf(code);
            if (i >= 0) _draft.RemoveAt(i);
            else if (_draft.Count >= DeckSize) MetaUiRoot.Toast("덱이 가득 찼습니다. (8/8) 다른 스킬을 먼저 빼주세요.", Tone.Enemy);
            else if (MetaSkills.IsHigh(code) && Highs() >= HighLimit) MetaUiRoot.Toast("고위력 스킬은 덱에 최대 2장까지 넣을 수 있습니다.", Tone.Enemy);
            else _draft.Add(code);
            Redraw();
        }

        private void Move(int i, int dir)
        {
            int j = i + dir;
            if (j < 0 || j >= _draft.Count) return;
            (_draft[i], _draft[j]) = (_draft[j], _draft[i]);
            Redraw();
        }

        private string Validate() => _draft.Count == 0 ? "덱이 비어 있습니다." : Backend.DeckService.Validate(_draft.ToArray());
    }
}
