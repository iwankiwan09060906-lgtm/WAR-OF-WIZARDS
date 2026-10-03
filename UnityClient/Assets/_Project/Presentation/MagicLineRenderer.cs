// 룬 드로잉 궤적 표시 (§8 VR 클라이언트: 궤적 · 조준 포인터 표시)
// 그리는 동안 손끝 궤적을 LineRenderer로 보여주고, 인식 결과에 따라 초록(성공)/빨강(실패)으로 바뀐 뒤 사라진다.

using SpellboundVR.Input;
using SpellboundVR.Spells;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    [RequireComponent(typeof(LineRenderer))]
    public sealed class MagicLineRenderer : MonoBehaviour
    {
        public Color drawingColor = new Color(0.6f, 0.85f, 1f, 1f);
        public Color successColor = new Color(0.4f, 1f, 0.5f, 1f);
        public Color failColor = new Color(1f, 0.35f, 0.3f, 1f);
        public float width = 0.006f;
        public float fadeSeconds = 0.5f;

        private LineRenderer _line;
        private ISpellInputSource _input;
        private CastStateMachine _cast;
        private float _fadeStart = -1f;
        private Color _fadeColor;
        private bool _wasDrawing;

        public void Initialize(ISpellInputSource input, CastStateMachine cast, float lineWidth)
        {
            _input = input;
            _cast = cast;
            width = lineWidth;
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 0;
            _line.widthMultiplier = width;
            _line.numCapVertices = 4;
            _line.numCornerVertices = 2;
            _line.sharedMaterial = FallbackVisuals.Transparent(Color.white);
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _cast.OnRuneRecognized += HandleRecognized;
            _cast.OnRecognitionFailed += HandleFailed;
        }

        private void OnDestroy()
        {
            if (_cast == null) return;
            _cast.OnRuneRecognized -= HandleRecognized;
            _cast.OnRecognitionFailed -= HandleFailed;
        }

        private void HandleRecognized(SpellDefinition def, int slot) => StartFade(successColor);

        private void HandleFailed(string reason) => StartFade(failColor);

        private void StartFade(Color c)
        {
            _fadeStart = Time.time;
            _fadeColor = c;
        }

        private void LateUpdate()
        {
            if (_input == null || _line == null) return;

            if (_input.IsDrawing)
            {
                _wasDrawing = true;
                _fadeStart = -1f;
                int n = _input.DrawPointCount;
                _line.positionCount = n;
                var pts = _input.DrawPoints;
                for (int i = 0; i < n; i++) _line.SetPosition(i, pts[i]);
                SetColor(drawingColor);
                return;
            }

            if (_wasDrawing && _fadeStart < 0f) StartFade(drawingColor);
            _wasDrawing = false;

            if (_fadeStart >= 0f)
            {
                float t = (Time.time - _fadeStart) / Mathf.Max(0.01f, fadeSeconds);
                if (t >= 1f)
                {
                    _line.positionCount = 0;
                    _fadeStart = -1f;
                }
                else
                {
                    var c = _fadeColor;
                    c.a *= 1f - t;
                    SetColor(c);
                }
            }
        }

        private void SetColor(Color c)
        {
            _line.startColor = c;
            _line.endColor = c;
        }
    }
}
