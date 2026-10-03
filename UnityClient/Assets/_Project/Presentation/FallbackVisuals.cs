// 아트 에셋(ㅇㅎㅅ)이 아직 없을 때 쓰는 대체 비주얼 (§25.1 Mock: 빈 프리팹 · 캡슐)
// 머티리얼은 색상별로 한 번만 만들어 공유한다 (배칭 유지).

using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public static class FallbackVisuals
    {
        public static readonly Color HomeColor = new Color(0.25f, 0.55f, 1f);
        public static readonly Color AwayColor = new Color(1f, 0.3f, 0.25f);

        private static readonly Dictionary<Color32, Material> s_opaque = new Dictionary<Color32, Material>();
        private static readonly Dictionary<Color32, Material> s_transparent = new Dictionary<Color32, Material>();
        private static Font s_font;

        public static Color TeamColor(Contracts.Team team) => team == Contracts.Team.Home ? HomeColor : AwayColor;

        public static Font DefaultFont
        {
            get
            {
                if (s_font == null)
                {
                    s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (s_font == null) s_font = Font.CreateDynamicFontFromOSFont("Arial", 32);
                }
                return s_font;
            }
        }

        public static Material Opaque(Color color)
        {
            Color32 key = color;
            if (s_opaque.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            m = new Material(shader) { color = color, name = "Fallback_Opaque_" + key };
            s_opaque[key] = m;
            return m;
        }

        public static Material Transparent(Color color)
        {
            Color32 key = color;
            if (s_transparent.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader) { color = color, name = "Fallback_Transparent_" + key };
            m.renderQueue = 3000;
            s_transparent[key] = m;
            return m;
        }

        /// <summary>콜라이더 없는 프리미티브 (서버 판정과 무관한 표시 전용)</summary>
        public static GameObject Primitive(PrimitiveType type, string name, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public static TextMesh Text(Transform parent, string text, float characterSize, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = DefaultFont;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = characterSize;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = DefaultFont.material;
            return tm;
        }

        /// <summary>MaterialPropertyBlock으로 팀 컬러 지정 (§25.4: 머티리얼 프로퍼티 1개로 변경)</summary>
        public static void ApplyTint(GameObject go, Color color, MaterialPropertyBlock block)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].GetPropertyBlock(block);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                block.SetColor("_TeamColor", color);
                renderers[i].SetPropertyBlock(block);
            }
        }
    }
}
