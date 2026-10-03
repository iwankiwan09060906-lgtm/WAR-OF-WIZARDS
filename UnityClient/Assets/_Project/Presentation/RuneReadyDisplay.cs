// §6.2 RuneReady — 인식된 룬이 손 위에 표시된다. Armed · Aiming 단계 안내도 함께 보여준다.

using SpellboundVR.Input;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class RuneReadyDisplay : MonoBehaviour
    {
        public Vector3 offsetAboveHand = new Vector3(0f, 0.12f, 0f);
        public float textSize = 0.0035f;

        private CastStateMachine _cast;
        private ISpellInputSource _input;
        private Transform _head;
        private TextMesh _text;
        private Transform _orb;
        private Transform _root;

        public void Initialize(CastStateMachine cast, ISpellInputSource input, Transform head)
        {
            _cast = cast;
            _input = input;
            _head = head;

            _root = new GameObject("RuneReadyDisplay").transform;
            _root.SetParent(transform, false);
            _orb = FallbackVisuals.Primitive(PrimitiveType.Sphere, "RuneOrb", FallbackVisuals.Transparent(Color.white)).transform;
            _orb.SetParent(_root, false);
            _orb.localScale = Vector3.one * 0.05f;
            _text = FallbackVisuals.Text(_root, "", textSize, Color.white);
            _text.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            _root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_cast == null) return;
            var state = _cast.State;
            bool show = (state == CastState.RuneReady || state == CastState.Armed || state == CastState.Aiming) && _cast.SelectedSpell != null;
            if (show != _root.gameObject.activeSelf) _root.gameObject.SetActive(show);
            if (!show) return;

            if (_input.TryGetRightHandPosition(out var hand)) _root.position = hand + offsetAboveHand;
            if (_head != null)
            {
                Vector3 toHead = _root.position - _head.position;
                if (toHead.sqrMagnitude > 1e-6f) _root.rotation = Quaternion.LookRotation(toHead, Vector3.up);
            }

            var def = _cast.SelectedSpell;
            var color = def.ThemeColor;
            _orb.GetComponent<Renderer>().sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.75f));
            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 8f);
            _orb.localScale = Vector3.one * 0.05f * pulse;

            string hint;
            switch (state)
            {
                case CastState.RuneReady: hint = def.RequiresAim ? "FIST to arm" : "FIST to cast"; break;
                case CastState.Armed: hint = "POINT to aim"; break;
                default: hint = AimBar(_cast.AimProgress); break;
            }
            _text.text = def.DisplayName + "\n" + hint;
            _text.color = color;
        }

        private static readonly string[] s_bars =
        {
            "[.....]", "[|....]", "[||...]", "[|||..]", "[||||.]", "[|||||]",
        };

        private static string AimBar(float progress)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(progress * 5f), 0, 5);
            return s_bars[i];
        }
    }
}
