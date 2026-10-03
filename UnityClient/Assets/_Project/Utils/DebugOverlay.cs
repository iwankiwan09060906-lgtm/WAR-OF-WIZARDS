// §31 금요일 최소 테스트 — DebugOverlay 기록 (FPS · 유닛 수 · 핑 · 경기 시간)
// 화면 우상단 OnGUI 표시 + 일정 간격 콘솔 로그. F11로 표시 토글.

using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Utils
{
    public sealed class DebugOverlay : MonoBehaviour
    {
        public bool visible = true;
        public float logIntervalSeconds = 30f;

        private System.Func<int> _unitCount;
        private System.Func<float> _matchTime;
        private System.Func<float> _pingMs;
        private float _fps;
        private float _fpsAccum;
        private int _fpsFrames;
        private float _nextFpsSample;
        private float _nextLog;
        private string _text = "";
        private GUIStyle _style;

        public void Initialize(System.Func<int> unitCount, System.Func<float> matchTime, System.Func<float> pingMs)
        {
            _unitCount = unitCount;
            _matchTime = matchTime;
            _pingMs = pingMs;
        }

        private void Update()
        {
            if (KeyInput.Down(Key.F11)) visible = !visible;

            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (Time.unscaledTime >= _nextFpsSample)
            {
                _fps = _fpsFrames / Mathf.Max(1e-4f, _fpsAccum);
                _fpsAccum = 0f;
                _fpsFrames = 0;
                _nextFpsSample = Time.unscaledTime + 0.5f;
                int units = _unitCount != null ? _unitCount() : 0;
                float t = _matchTime != null ? _matchTime() : 0f;
                float ping = _pingMs != null ? _pingMs() : -1f;
                _text = "FPS " + Mathf.RoundToInt(_fps) + "  units " + units + "  t " + Mathf.FloorToInt(t) + "s" +
                        (ping >= 0f ? "  ping " + Mathf.RoundToInt(ping) + "ms" : "");
            }

            if (logIntervalSeconds > 0f && Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + logIntervalSeconds;
                Debug.Log("[DebugOverlay] " + _text);
            }
        }

        private void OnGUI()
        {
            if (!visible) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperRight };
                _style.normal.textColor = Color.yellow;
            }
            GUI.Label(new Rect(Screen.width - 410, 8, 400, 24), _text, _style);
        }
    }
}
