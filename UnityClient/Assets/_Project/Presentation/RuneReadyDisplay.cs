// §6.2 RuneReady — 인식된 룬이 손 위에 표시된다. Armed · Aiming 단계 안내도 함께 보여준다.
//   룬 아이콘(RuneTemplate.Icon = Art/Runes/Icons/{SkillId}_s.png)을 원본 가로세로 비율대로 띄운다.
//   선은 스킬 색, 뒤에 반투명 원형 받침 (Resources/RuneIcon.shader). 아이콘이 없는 룬만 예전처럼 색 구체.
// 여러 획 룬을 그리는 중(획 사이)에는 "FIST to finish" 안내를 보여준다.

using System.Collections.Generic;
using SpellboundVR.Gesture;
using SpellboundVR.Input;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class RuneReadyDisplay : MonoBehaviour
    {
        [Tooltip("손 위 표시 위치 — 룬이 커서 손을 가리지 않도록 아이콘 반지름만큼 위로")]
        public Vector3 offsetAboveHand = new Vector3(0f, 0.32f, 0f);
        public float textSize = 0.006f;
        [Tooltip("아이콘 긴 변 길이 (m) — 손 크기(약 0.1m)의 5배")]
        public float iconSize = 0.5f;
        [Tooltip("아이콘(마법진)이 천천히 도는 속도 (도/초)")]
        public float iconSpinDegrees = 20f;

        private CastStateMachine _cast;
        private ISpellInputSource _input;
        private Transform _head;
        private TextMesh _text;
        private Transform _orb;
        private Transform _root;
        private RuneTemplateLibrary _runes;
        private Renderer _icon;
        private Shader _iconShader;
        private readonly Dictionary<int, (Material material, float aspect)> _iconMaterials = new Dictionary<int, (Material, float)>();

        public void Initialize(CastStateMachine cast, ISpellInputSource input, Transform head, RuneTemplateLibrary runes)
        {
            _cast = cast;
            _input = input;
            _head = head;
            _runes = runes;
            _iconShader = Shader.Find("Spellbound/RuneIcon");
            if (_iconShader == null) _iconShader = Shader.Find("Sprites/Default");

            _root = new GameObject("RuneReadyDisplay").transform;
            _root.SetParent(transform, false);
            _orb = FallbackVisuals.Primitive(PrimitiveType.Sphere, "RuneOrb", FallbackVisuals.Transparent(Color.white)).transform;
            _orb.SetParent(_root, false);
            _orb.localScale = Vector3.one * iconSize * 0.5f;
            _text = FallbackVisuals.Text(_root, "", textSize, Color.white);
            _text.transform.localPosition = new Vector3(0f, iconSize * 0.5f + 0.03f, 0f);
            // 룬 아이콘 — 구체 자리(손 위)에 쿼드, 머리 쪽을 향함
            var quad = FallbackVisuals.Primitive(PrimitiveType.Quad, "RuneIcon", FallbackVisuals.Transparent(Color.white));
            quad.transform.SetParent(_root, false);
            _icon = quad.GetComponent<Renderer>();
            _icon.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_cast == null) return;
            var state = _cast.State;
            // 여러 획 룬: 획 사이(핀치를 놓은 상태)에는 "주먹 = 그리기 끝" 안내만 띄운다
            bool strokeGap = state == CastState.Drawing && !_input.IsDrawing && _cast.StrokeCount > 0;
            bool show = strokeGap ||
                        ((state == CastState.RuneReady || state == CastState.Armed || state == CastState.Aiming) && _cast.SelectedSpell != null);
            if (show != _root.gameObject.activeSelf) _root.gameObject.SetActive(show);
            if (!show) return;

            if (_input.TryGetRightHandPosition(out var hand)) _root.position = hand + offsetAboveHand;
            if (_head != null)
            {
                Vector3 toHead = _root.position - _head.position;
                if (toHead.sqrMagnitude > 1e-6f) _root.rotation = Quaternion.LookRotation(toHead, Vector3.up);
            }

            if (strokeGap)
            {
                SetActive(_orb, false);
                SetActive(_icon.transform, false);
                _text.text = "FIST to finish";
                _text.color = Color.white;
                return;
            }

            var def = _cast.SelectedSpell;
            var color = def.ThemeColor;
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f);
            var (iconMat, aspect) = GetIconMaterial(def.SpellId, color);
            SetActive(_icon.transform, iconMat != null);
            SetActive(_orb, iconMat == null);
            if (iconMat != null)
            {
                _icon.sharedMaterial = iconMat;
                // 긴 변 = iconSize, 원본 비율 유지
                var size = aspect >= 1f ? new Vector2(iconSize, iconSize / aspect) : new Vector2(iconSize * aspect, iconSize);
                _icon.transform.localScale = new Vector3(size.x * pulse, size.y * pulse, 1f);
                _icon.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * iconSpinDegrees);
            }
            else
            {
                _orb.GetComponent<Renderer>().sharedMaterial = FallbackVisuals.Transparent(new Color(color.r, color.g, color.b, 0.75f));
                _orb.localScale = Vector3.one * iconSize * 0.5f * pulse;
            }

            string hint;
            switch (state)
            {
                case CastState.RuneReady: hint = def.RequiresAim ? "FIST to arm" : "FIST to cast"; break;
                case CastState.Armed: hint = "POINT to aim"; break;
                default: hint = AimBar(_cast.AimProgress); break;
            }
            _text.text = def.DisplayName + "\n" + hint;
            _text.color = Color.Lerp(color, Color.white, 0.3f);
        }

        private static void SetActive(Component c, bool active)
        {
            if (c.gameObject.activeSelf != active) c.gameObject.SetActive(active);
        }

        /// <summary>스킬별 아이콘 재질 (선 = 스킬 색을 밝게) + 가로/세로 비율. 아이콘이 없으면 null (캐시)</summary>
        private (Material, float) GetIconMaterial(int spellId, Color themeColor)
        {
            if (_iconMaterials.TryGetValue(spellId, out var cached)) return cached;
            var rune = _runes != null ? _runes.Find(spellId) : null;
            (Material, float) result = (null, 1f);
            if (rune != null && rune.Icon != null && _iconShader != null)
            {
                var m = new Material(_iconShader) { mainTexture = rune.Icon, name = "RuneIcon_" + spellId };
                m.color = Color.Lerp(themeColor, Color.white, 0.35f);
                result = (m, rune.Icon.height > 0 ? (float)rune.Icon.width / rune.Icon.height : 1f);
            }
            _iconMaterials[spellId] = result;
            return result;
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
