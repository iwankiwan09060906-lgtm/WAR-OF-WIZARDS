// S01 연막 피격 — 시야 암전 (표시 전용, 서버 PlayerFlags.Blind 를 따른다)
//   · 머리를 감싼 검은 구체(ZTest Always, Overlay 큐)로 월드 · 룬 궤적 · HUD를 모두 덮는다 → 그리는 모양도 안 보인다
//   · 0.25초 페이드 인/아웃, 가장자리에 연기가 소용돌이치는 느낌으로 살짝 맥동
//   · 가운데 작은 글자로 남은 시간만 보여준다 (헤드셋을 벗지 않게)
//   · 상대가 연막에 맞으면 상대 아바타 머리 주변에 검은 연기 (ArenaStateView가 담당)

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class BlindOverlayView : MonoBehaviour
    {
        [Tooltip("페이드 인/아웃 시간 (초)")]
        public float fadeSeconds = 0.25f;
        [Tooltip("최대 불투명도 (1 = 완전 암전)")]
        public float maxAlpha = 1f;

        private ClientSession _session;
        private Transform _head;
        private Transform _root;
        private Material _sphereMat;
        private Material _textMat;
        private TextMesh _text;
        private float _alpha;
        private float _blindStart = -1f;
        private int _lastSecond = -1;

        public void Initialize(ClientSession session, Transform head)
        {
            _session = session;
            _head = head;

            var shader = Shader.Find("Spellbound/OverlayAlways");
            if (shader == null)
            {
                Debug.LogWarning("[BlindOverlayView] Spellbound/OverlayAlways 셰이더 없음 — 연막 암전 표시 생략");
                enabled = false;
                return;
            }

            _root = new GameObject("[BlindOverlay]").transform;
            _root.SetParent(transform, false);

            _sphereMat = new Material(shader) { name = "BlindOverlay", color = new Color(0f, 0f, 0f, 0f) };
            _sphereMat.renderQueue = 4010;
            var sphere = FallbackVisuals.Primitive(PrimitiveType.Sphere, "BlindSphere", _sphereMat);
            sphere.transform.SetParent(_root, false);
            sphere.transform.localScale = Vector3.one * 0.6f; // 근거리 클립(0.1)보다 크고 손보다 가깝게

            _text = FallbackVisuals.Text(_root, "", 0.0022f, Color.white);
            _text.transform.localPosition = new Vector3(0f, -0.04f, 0.25f);
            _textMat = new Material(shader) { name = "BlindText", mainTexture = FallbackVisuals.DefaultFont.material.mainTexture };
            _textMat.color = new Color(0.75f, 0.75f, 0.8f, 0f);
            _textMat.renderQueue = 4011;
            _text.GetComponent<MeshRenderer>().sharedMaterial = _textMat;

            _root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_session == null || _root == null) return;
            var snap = _session.Events.Latest;
            bool blind = false;
            if (snap != null && _session.Events.HasLocalTeam && snap.Phase == MatchPhase.Playing)
                blind = (snap.Players[(int)_session.Events.LocalTeam].Flags & PlayerFlags.Blind) != 0;

            if (blind && _blindStart < 0f) _blindStart = Time.time;
            if (!blind) _blindStart = -1f;

            float target = blind ? maxAlpha : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
            bool show = _alpha > 0.001f;
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;

            if (_head != null) _root.SetPositionAndRotation(_head.position, _head.rotation);

            // 연기 맥동: 완전 검정 위로 아주 옅은 회색 숨결
            float breathe = 0.03f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.3f));
            _sphereMat.color = new Color(breathe, breathe, breathe * 1.1f, _alpha);

            float elapsed = _blindStart >= 0f ? Time.time - _blindStart : 0f;
            var smoke = _session.Catalog.Get(Spells.SpellIds.S01_Smoke);
            float total = smoke != null && smoke.EffectDuration > 0f ? smoke.EffectDuration : 5f;
            int second = Mathf.Max(0, Mathf.CeilToInt(total - elapsed));
            if (second != _lastSecond)
            {
                _lastSecond = second;
                _text.text = blind ? "SMOKE  " + second : "";
            }
            var tc = _textMat.color;
            tc.a = _alpha * 0.55f;
            _textMat.color = tc;
        }

        private void OnDestroy()
        {
            if (_sphereMat != null) Destroy(_sphereMat);
            if (_textMat != null) Destroy(_textMat);
        }
    }
}
