// §6.5 스냅 이동(짧은 순간 이동 + 비네팅)으로 VR 멀미 방지
// 카메라 바로 앞 반투명 검은 판을 0.2초 동안 페이드한다 (완전 암전 아님).

using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class SnapVignette : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Range(0f, 1f)] public float maxAlpha = 0.75f;
        public float duration = 0.22f;

        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private float _start = -1f;

        public void Initialize(Transform head)
        {
            var quad = FallbackVisuals.Primitive(PrimitiveType.Quad, "SnapVignette", FallbackVisuals.Transparent(Color.black));
            quad.transform.SetParent(head, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            quad.transform.localScale = new Vector3(0.6f, 0.4f, 1f);
            _renderer = quad.GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
            _renderer.enabled = false;
        }

        public void Play()
        {
            _start = Time.time;
            if (_renderer != null) _renderer.enabled = true;
        }

        private void LateUpdate()
        {
            if (_start < 0f || _renderer == null) return;
            float t = (Time.time - _start) / Mathf.Max(0.01f, duration);
            if (t >= 1f)
            {
                _renderer.enabled = false;
                _start = -1f;
                return;
            }
            float a = maxAlpha * (t < 0.3f ? t / 0.3f : 1f - (t - 0.3f) / 0.7f);
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, new Color(0f, 0f, 0f, a));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
