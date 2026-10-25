// C08 랭킹 — 웹 pages/ranking.js: 내 순위 + 랭킹전 레이팅 상위 100명 (1~3위 메달 색)

using SpellboundVR.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class RankingScreen : MetaScreen
    {
        private RankEntryDto[] _rows;
        private string _error;

        public override string Title => "랭킹";

        protected override void OnOpen()
        {
            _rows = null;
            _error = null;
            if (MetaState.Offline) { _rows = new RankEntryDto[0]; _error = "랭킹은 게임 서버에 연결해야 볼 수 있습니다."; return; }
            RankingService.GetTop(100, r => { _rows = r; Redraw(); }, e => { _rows = new RankEntryDto[0]; _error = e; Redraw(); });
        }

        protected override void Build(RectTransform body)
        {
            long meId = MetaState.Profile != null ? MetaState.Profile.id : 0;
            RankEntryDto mine = null;
            if (_rows != null) foreach (var r in _rows) if (r.id == meId && meId != 0) mine = r;
            UiKit.Chip(Ui.HeaderRight, mine != null ? $"내 순위  {mine.rank}위 · {mine.rating}점 · {mine.wins}승 {mine.losses}패" : $"내 순위  Top 100 밖 · {MetaState.Rating}점",
                       mine != null ? Tone.Cyan : Tone.Neutral);

            var outer = UiKit.Stretch(UiKit.HBox(body, 0, 0, TextAnchor.UpperCenter, "Outer"));
            outer.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            var col = UiKit.VBox(outer, 14, 0, TextAnchor.UpperLeft, "Col");
            UiKit.Size(col, 1300, -1, 0, 1);
            UiKit.Label(col, "랭킹전 레이팅 기준 · 상위 100명", UiTheme.FontBody, UiTheme.Text2);
            var card = UiKit.Card(col, 20, 4, "Table");
            UiKit.Size(card, -1, -1, -1, 1);
            if (_error != null) UiKit.Note(card, _error, Tone.Enemy);
            if (_rows == null) { UiKit.Label(card, "불러오는 중…", UiTheme.FontBody, UiTheme.Text3, false, TextAnchor.MiddleCenter); return; }

            Row(card, "순위", "닉네임", "등급", "레이팅", "전적", "승률", UiTheme.Text3, UiTheme.Text3, true, false);
            var sr = UiKit.Scroll(card, out var content, 2);
            UiKit.Size(sr, -1, -1, -1, 1);
            foreach (var r in _rows)
            {
                Color rankColor = r.rank == 1 ? UiTheme.Gold : r.rank == 2 ? UiTheme.Silver : r.rank == 3 ? UiTheme.Bronze : UiTheme.Text2;
                bool me = r.id == meId && meId != 0;
                var line = Row(content, r.rank.ToString(), r.nickname + (me ? $"  <color={UiKit.Hex(UiTheme.Cyan)}>나</color>" : ""), MetaState.TierOf(r.rating),
                               r.rating.ToString(), $"{r.wins}승 {r.losses}패", MetaState.WinRate(r.wins, r.losses) + "%", rankColor, UiTheme.Text, false, me);
            }
        }

        private static RectTransform Row(Transform parent, string rank, string name, string tier, string rating, string record, string rate, Color rankColor, Color nameColor, bool head, bool me)
        {
            var r = UiKit.HBox(parent, 14, 0, TextAnchor.MiddleLeft, "Row");
            r.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 16, 0, 0);
            UiKit.Size(r, -1, head ? 44 : 56);
            if (me)
            {
                var img = r.gameObject.AddComponent<Image>();
                img.color = new Color(UiTheme.Cyan.r, UiTheme.Cyan.g, UiTheme.Cyan.b, 0.1f);
            }
            int fs = head ? UiTheme.FontLabel - 1 : UiTheme.FontBody;
            Cell(r, rank, 110, head ? fs : UiTheme.FontTitle, rankColor, !head, TextAnchor.MiddleLeft);
            var n = Cell(r, name, -1, fs, head ? UiTheme.Text3 : nameColor, !head, TextAnchor.MiddleLeft);
            UiKit.Size(n, -1, -1, 1);
            Cell(r, tier, 190, fs, UiTheme.Text3, false, TextAnchor.MiddleLeft);
            Cell(r, rating, 140, fs, head ? UiTheme.Text3 : UiTheme.Text, !head, TextAnchor.MiddleRight);
            Cell(r, record, 170, fs, UiTheme.Text3, false, TextAnchor.MiddleRight);
            Cell(r, rate, 110, fs, head ? UiTheme.Text3 : UiTheme.Text2, false, TextAnchor.MiddleRight);
            return r;
        }

        private static Text Cell(Transform r, string text, float w, int size, Color color, bool bold, TextAnchor align)
        {
            var t = UiKit.Label(r, text, size, color, bold, align);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (w > 0) UiKit.Size(t, w, -1);
            return t;
        }
    }
}
