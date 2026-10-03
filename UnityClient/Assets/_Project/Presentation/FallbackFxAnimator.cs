// VFX 프리팹이 없을 때 쓰는 대체 연출 (구체 확대 · 이동 · 페이드). 끝나면 스스로 비활성 → 풀 반환.
// ㅇㅎㅅ의 실제 VFX 프리팹에는 이 스크립트를 붙이지 않는다 (Rule 13). 대체 연출 전용이다.

using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class FallbackFxAnimator : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _block;
        private Renderer _renderer;
        private Color _color;
        private Vector3 _from;
        private Vector3 _to;
        private float _startScale;
        private float _endScale;
        private float _duration;
        private float _elapsed;
        private bool _loop;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        /// <param name="loop">true면 duration 동안 맥동하며 유지 (외부에서 끄기 전까지)</param>
        public void Play(Color color, Vector3 from, Vector3 to, float startScale, float endScale, float duration, bool loop)
        {
            _color = color;
            _from = from;
            _to = to;
            _startScale = startScale;
            _endScale = endScale;
            _duration = Mathf.Max(0.05f, duration);
            _elapsed = 0f;
            _loop = loop;
            Apply(0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_loop)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(_elapsed * 4f);
                transform.position = _to;
                transform.localScale = Vector3.one * Mathf.Lerp(_startScale, _endScale, pulse);
                SetAlpha(_color.a * (0.6f + 0.4f * pulse));
                if (_elapsed >= _duration) gameObject.SetActive(false);
                return;
            }
            float t = Mathf.Clamp01(_elapsed / _duration);
            Apply(t);
            if (t >= 1f) gameObject.SetActive(false);
        }

        private void Apply(float t)
        {
            transform.position = Vector3.Lerp(_from, _to, t);
            transform.localScale = Vector3.one * Mathf.Lerp(_startScale, _endScale, t);
            SetAlpha(_color.a * (1f - t * t));
        }

        private void SetAlpha(float a)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, new Color(_color.r, _color.g, _color.b, a));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
