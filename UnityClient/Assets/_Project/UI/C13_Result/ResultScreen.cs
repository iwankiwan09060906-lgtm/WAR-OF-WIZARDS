// C13 결과 — VR UI 프로토타입(Figma Make, 이식 후 삭제)의 ResultScreen.tsx (+ 웹 pages/result.js 의 저장 · 레이팅 표시)
//   "BATTLE COMPLETE" · VICTORY(보라↔청록으로 흐르는 글자, 뒤로 빛줄기 8개) / DEFEATED(빨강, 배경도 빨강) · 영문 부제 + 끝난 이유
//   · 모서리 강조 상자 (프로토타입 Final Score 자리 → 랭킹전 레이팅 변화 / 전적 저장 상태 — 점수를 지어내지 않는다)
//   · 기록 4칸 (입힌 피해 주황 · 시전 보라 · 받은 피해 청록 · 플레이 시간 초록/빨강) · 다시 대전 / 로비로

using SpellboundVR.Backend;
using SpellboundVR.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class ResultScreen : MetaScreen
    {
        private Image[] _rays;

        public override string Title => null;
        public override bool ShowBackToLobby => false;
        public override Color Mood => Lost ? UiTheme.Enemy : UiTheme.Purple;
        public override float CircleAlpha => 0.3f;

        private static bool Lost => MetaState.LastMatch != null && MetaState.LastMatch.Result == MatchResult.Lose;

        protected override void HandleStateChanged(string key)
        {
            if (key == "lastMatch" || key == "profile")
            {
                Ui.Backdrop?.SetMood(Mood, CircleAlpha);
                Redraw();
            }
        }

        protected override void Build(RectTransform body)
        {
            _rays = null;
            var m = MetaState.LastMatch;
            if (m == null)
            {
                var empty = UiKit.Stretch(UiKit.VBox(body, 20, 0, TextAnchor.MiddleCenter, "Result"));
                empty.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
                UiKit.Label(empty, "표시할 대전 결과가 없습니다.", UiTheme.FontBody, UiTheme.Text2, false, TextAnchor.MiddleCenter);
                UiKit.Button(empty, "로비로", BtnStyle.Play, () => MetaFlow.Instance.ReturnTo(MetaScreenId.Lobby), 240);
                return;
            }
            bool win = m.Result == MatchResult.Win;
            bool lose = m.Result == MatchResult.Lose;
            Color accent = lose ? UiTheme.Enemy : UiTheme.Purple;

            if (win) BuildRays(body);

            var col = UiKit.Stretch(UiKit.VBox(body, 26, 0, TextAnchor.MiddleCenter, "Result"));
            col.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;

            // 결과 글자
            var head = UiKit.VBox(col, 4, 0, TextAnchor.MiddleCenter, "Head");
            head.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            UiKit.Kicker(head, "Battle Complete", UiTheme.PurpleLight, TextAnchor.MiddleCenter, 16);
            string word = win ? "VICTORY" : lose ? "DEFEATED" : m.Result == MatchResult.Void ? "VOID" : "DRAW";
            var big = UiKit.Display(head, word, 150, lose ? UiTheme.Enemy : UiTheme.Text, TextAnchor.MiddleCenter, true);
            UiKit.Size(big, -1, 170);
            if (win) UiGradient.Apply(big, UiTheme.PurpleLight, UiTheme.Cyan, UiTheme.PurpleLight, 0.35f);
            else if (!lose) UiGradient.Apply(big, UiTheme.PurplePale, UiTheme.Text2, UiTheme.PurplePale);
            string sub = win ? "The realm is yours, Wizard" : lose ? "The shadows claim you" : "The duel ends in balance";
            UiKit.Kicker(head, sub, win ? UiTheme.Hex(0x67e8f9) : lose ? UiTheme.Hex(0xf87171) : UiTheme.Text2, TextAnchor.MiddleCenter, 20);
            string reason = $"{MetaState.ModeName(m.Mode)} · {m.OpponentName} 상대" + (string.IsNullOrEmpty(m.Reason) ? "" : "  ·  " + m.Reason);
            UiKit.Label(head, reason, UiTheme.FontBody - 2, UiTheme.Text2, false, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Overflow;

            BuildRatingBox(col, m, accent, lose);

            var stats = UiKit.HBox(col, 18, 0, TextAnchor.MiddleCenter, "Stats");
            stats.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Stat(stats, m.DamageDealt.ToString(), "입힌 피해", UiTheme.Orange);
            Stat(stats, m.Casts.ToString(), "시전 횟수", UiTheme.PurpleLight);
            Stat(stats, m.DamageTaken.ToString(), "받은 피해", UiTheme.CyanLight);
            Stat(stats, MetaState.Mmss(m.DurationSec), "플레이 시간", lose ? UiTheme.Hex(0xf87171) : UiTheme.Green);

            var actions = UiKit.HBox(col, 24, 0, TextAnchor.MiddleCenter, "Actions");
            actions.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            string again = m.Mode == MatchResultService.ModeFriendly ? "친선전 룸으로" : m.Mode == MatchResultService.ModeRanked ? "다시 매칭" : "다시 대전";
            var play = UiKit.Button(actions, again, BtnStyle.Play, () =>
            {
                if (m.Mode == MatchResultService.ModeRanked && !MetaState.Offline) MetaFlow.Instance.ReturnTo(MetaScreenId.Queue);
                else if (m.Mode == MatchResultService.ModeFriendly) MetaFlow.Instance.ReturnTo(MetaScreenId.Room);
                else MetaFlow.Instance.ReturnTo(MetaScreenId.Countdown); // 봇 연습 · 스킬 테스트: 같은 상대로 바로
            }, 320, 76, UiTheme.FontTitle - 2);
            UiKit.Halo(play.transform, UiTheme.Purple);
            UiKit.Button(actions, "로비로", BtnStyle.Info, () => MetaFlow.Instance.ReturnTo(MetaScreenId.Lobby), 260, 76, UiTheme.FontTitle - 2);
            UiKit.Button(actions, "게임 종료", BtnStyle.Ghost, () => MetaFlow.Instance.QuitGame(), 200, 76, UiTheme.FontBody);
        }

        /// <summary>모서리 강조 상자 — 랭킹전은 레이팅 변화, 나머지는 전적 저장 상태</summary>
        private static void BuildRatingBox(Transform parent, MatchRecord m, Color accent, bool lose)
        {
            var box = UiKit.VBox(parent, 6, 0, TextAnchor.MiddleCenter, "RatingBox");
            box.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(70, 70, 22, 24);
            box.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            var img = box.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0.4f), UiTheme.Alpha(accent, lose ? 0.35f : 0.55f), 16, 3);
            img.type = Image.Type.Sliced;
            UiKit.Size(box, 620, -1);
            Color valueColor = lose ? UiTheme.Hex(0xfca5a5) : UiTheme.PurplePale;

            if (m.Saved == true && m.Mode == MatchResultService.ModeRanked && m.NewRating > 0)
            {
                UiKit.Kicker(box, "Rating", UiTheme.PurpleLight, TextAnchor.MiddleCenter, 14);
                var line = UiKit.HBox(box, 18, 0, TextAnchor.MiddleCenter, "Value");
                line.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                UiKit.Display(line, m.NewRating.ToString(), 64, valueColor, TextAnchor.MiddleCenter);
                UiKit.Display(line, (m.RatingDelta >= 0 ? "+" : "") + m.RatingDelta, 34, m.RatingDelta >= 0 ? UiTheme.CyanLight : UiTheme.Enemy, TextAnchor.MiddleCenter);
                UiKit.Label(box, MetaState.TierOf(m.NewRating), UiTheme.FontSmall, UiTheme.Text3, true, TextAnchor.MiddleCenter);
                return;
            }
            UiKit.Kicker(box, "Record", UiTheme.PurpleLight, TextAnchor.MiddleCenter, 14);
            string text;
            Color color = valueColor;
            if (m.Saved == null) text = "전적 저장 중…";
            else if (m.Saved == false)
            {
                text = string.IsNullOrEmpty(m.SaveMessage) ? "전적을 저장하지 못했습니다." : m.SaveMessage;
                if (!MetaState.Offline && !string.IsNullOrEmpty(m.Mode)) color = UiTheme.Enemy;
            }
            else text = string.IsNullOrEmpty(m.Mode) ? "스킬 테스트 · 기록하지 않음" : "전적 저장 완료";
            var t = UiKit.Label(box, text, UiTheme.FontTitle, color, true, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (m.Saved == true && m.Mode != MatchResultService.ModeRanked && !string.IsNullOrEmpty(m.Mode))
                UiKit.Label(box, $"{MetaState.ModeName(m.Mode)}은 레이팅에 반영되지 않습니다", UiTheme.FontSmall - 2, UiTheme.Text3, false, TextAnchor.MiddleCenter);
        }

        private static void Stat(Transform parent, string value, string label, Color color)
        {
            var box = UiKit.VBox(parent, 4, 0, TextAnchor.MiddleCenter, "Stat");
            box.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 16, 16);
            var img = box.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(new Color(0.12f, 0.06f, 0.28f, 0.22f), UiTheme.Alpha(UiTheme.Purple, 0.25f), 0, 2);
            img.type = Image.Type.Sliced;
            UiKit.Size(box, 230, -1);
            UiKit.Display(box, value, UiTheme.FontHeadline, color, TextAnchor.MiddleCenter);
            UiKit.Label(box, label, UiTheme.FontSmall - 3, UiTheme.PurpleLight, false, TextAnchor.MiddleCenter);
        }

        /// <summary>승리 빛줄기 — 가운데에서 45도 간격으로 8개, 차례로 숨 쉬듯 밝아진다</summary>
        private void BuildRays(RectTransform body)
        {
            var root = UiKit.Node(body, "Rays");
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;
            _rays = new Image[8];
            for (int i = 0; i < 8; i++)
            {
                var ray = UiKit.Panel(root, UiTheme.Alpha(UiTheme.PurpleLight, 0.12f), null, "Ray");
                var rt = ray.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(2f, 480f);
                rt.localRotation = Quaternion.Euler(0, 0, i * 45f);
                UiGradient.Apply(ray, UiTheme.Alpha(UiTheme.PurpleLight, 0f), UiTheme.Alpha(UiTheme.PurpleLight, 0.05f), UiTheme.Alpha(UiTheme.PurpleLight, 0.14f), 0f, true);
                _rays[i] = ray;
            }
        }

        public override void Tick(float dt)
        {
            if (_rays == null) return;
            float t = Time.unscaledTime;
            for (int i = 0; i < _rays.Length; i++)
            {
                if (_rays[i] == null) continue;
                float a = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI - i * 0.8f));
                _rays[i].color = new Color(1f, 1f, 1f, a);
            }
        }
    }
}
