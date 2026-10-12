// 룬 드로잉 궤적 표시 (§8 VR 클라이언트: 궤적 · 조준 포인터 표시)
// 그리는 동안 손끝 궤적을 LineRenderer로 보여주고, 인식 결과에 따라 초록(성공)/빨강(실패)으로 바뀐 뒤 사라진다.
// 여러 획 룬: 획마다 LineRenderer 하나 (풀에서 재사용). 핀치를 놓아도 룬이 끝날 때(주먹 · 취소)까지 이전 획이 남는다.

using System.Collections.Generic;
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

        private readonly List<LineRenderer> _lines = new List<LineRenderer>(8);
        private int _used; // 지금 룬에 쓴 줄 수 (= 획 수)
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
            _lines.Add(SetupLine(GetComponent<LineRenderer>()));
            _cast.OnRuneRecognized += HandleRecognized;
            _cast.OnRecognitionFailed += HandleFailed;
        }

        private void OnDestroy()
        {
            if (_cast == null) return;
            _cast.OnRuneRecognized -= HandleRecognized;
            _cast.OnRecognitionFailed -= HandleFailed;
        }

        private LineRenderer SetupLine(LineRenderer line)
        {
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.sharedMaterial = FallbackVisuals.Transparent(Color.white);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private LineRenderer NextLine()
        {
            if (_used == _lines.Count)
            {
                var go = new GameObject("Stroke" + _used);
                go.transform.SetParent(transform, false);
                _lines.Add(SetupLine(go.AddComponent<LineRenderer>()));
            }
            return _lines[_used++];
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
            if (_input == null || _lines.Count == 0) return;

            if (_input.IsDrawing)
            {
                if (!_wasDrawing)
                {
                    if (_fadeStart >= 0f) ClearLines(); // 이전 룬이 사라지는 중 → 지우고 새 룬
                    _fadeStart = -1f;
                    NextLine();
                    SetColor(drawingColor);
                }
                _wasDrawing = true;
                var line = _lines[_used - 1];
                int n = _input.DrawPointCount;
                line.positionCount = n;
                var pts = _input.DrawPoints;
                for (int i = 0; i < n; i++) line.SetPosition(i, pts[i]);
                return;
            }
            _wasDrawing = false;

            // 획 사이 (같은 룬을 아직 그리는 중)에는 그대로 둔다. 취소 · 시간 초과로 끝나면 그리던 색으로 사라진다
            if (_fadeStart < 0f && _used > 0 && _cast.State != CastState.Drawing) StartFade(drawingColor);

            if (_fadeStart >= 0f)
            {
                float t = (Time.time - _fadeStart) / Mathf.Max(0.01f, fadeSeconds);
                if (t >= 1f)
                {
                    ClearLines();
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

        private void ClearLines()
        {
            for (int i = 0; i < _used; i++) _lines[i].positionCount = 0;
            _used = 0;
        }

        private void SetColor(Color c)
        {
            for (int i = 0; i < _used; i++)
            {
                _lines[i].startColor = c;
                _lines[i].endColor = c;
            }
        }
    }
}
