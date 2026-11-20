// 시연 게임 오버 — 내가 쓰러지면 시야 전체를 검게 덮고 "GAME OVER" 를 띄운 뒤 몇 초 뒤 onFinished (GameBootstrap 이 연습부터 다시 = 다음 관람객).
//   내 자리는 서버가 이미 AI 에게 넘겼다 (MatchSimulation.TickDemoRespawn).

using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class GameOverView : MonoBehaviour
    {
        [Tooltip("눈앞 거리 (m) — 스킬창 · 전장보다 가깝게")]
        public float distance = 0.6f;
        [Tooltip("글씨 판 폭 (m)")]
        public float width = 0.45f;

        private Transform _head;
        private Text _countdown;
        private float _endAt = -1f;
        private Action _onFinished;

        public void Show(Transform head, float seconds, Action onFinished)
        {
            _head = head;
            _onFinished = onFinished;
            _endAt = Time.time + seconds;

            var go = new GameObject("GameOver", typeof(RectTransform));
            go.transform.SetParent(head, false); // 머리에 붙여 고개를 돌려도 검은 화면
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 1.5f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1000f, 460f);
            rt.localScale = Vector3.one * (width / 1000f);
            rt.localPosition = new Vector3(0f, 0f, distance);
            rt.localRotation = Quaternion.identity;

            // 시야 전체를 덮는 검은 판 (0.6m 앞 약 3.6m — Quest 시야각보다 넓다)
            var black = UiKit.Panel(rt, Color.black, null, "Black");
            black.raycastTarget = false;
            var brt = black.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(8000f, 8000f);

            var bg = UiKit.Panel(rt, Color.white, UiTheme.Cut(new Color(10 / 255f, 2 / 255f, 8 / 255f, 0.92f), UiTheme.Alpha(UiTheme.Enemy, 0.8f), 18, 3), "Bg");
            bg.type = Image.Type.Sliced;
            UiKit.Stretch(bg.rectTransform);
            var title = Line(rt, "GAME OVER", new Vector2(0f, 80f), 130, UiTheme.Enemy);
            title.font = UiTheme.Display;
            Line(rt, "쓰러졌습니다 — 체험해 주셔서 고맙습니다", new Vector2(0f, -40f), 40, UiTheme.Text);
            _countdown = Line(rt, "", new Vector2(0f, -130f), 32, UiTheme.CyanLight);
        }

        private static Text Line(RectTransform parent, string text, Vector2 pos, int size, Color color)
        {
            var t = UiKit.Label(parent, text, size, color, true, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(980f, size * 1.4f);
            return t;
        }

        private void Update()
        {
            if (_endAt < 0f) return;
            float left = _endAt - Time.time;
            _countdown.text = Mathf.CeilToInt(Mathf.Max(0f, left)) + "초 뒤 처음 화면(연습)으로 돌아갑니다";
            if (left > 0f) return;
            _endAt = -1f;
            _onFinished?.Invoke();
        }
    }
}
