// 메타 UI 배경 — 프로토타입의 공통 배경을 UGUI로: 가운데 보라 빛 번짐 + 서로 반대로 천천히 도는 마법진 두 겹(MagicCircle.tsx)
// + 아래에서 위로 떠오르는 보라 · 청록 입자(Particles.tsx) + 화면 네 모서리 ㄱ자 테두리(FrameCorners).
// 화면마다 분위기(Tint)만 바꾼다: 기본 보라, 회원가입 청록, 패배 빨강.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class MagicBackdrop : MonoBehaviour
    {
        private struct Particle
        {
            public RectTransform Rt;
            public Image Img;
            public float Speed, Drift, Phase, Size;
        }

        private RectTransform _outer, _inner;
        private Image _glow, _glow2, _outerImg, _innerImg;
        private readonly List<Particle> _particles = new List<Particle>();
        private float _circleAlpha = 0.22f;
        private Color _tint = UiTheme.Purple;

        public static MagicBackdrop Build(RectTransform canvasRoot)
        {
            var root = UiKit.Stretch(UiKit.Node(canvasRoot, "MagicBackdrop"));
            root.SetAsFirstSibling();
            var b = root.gameObject.AddComponent<MagicBackdrop>();
            var bg = UiKit.Panel(root, UiTheme.Bg0, null, "Base");
            UiKit.Stretch(bg.rectTransform);
            bg.raycastTarget = true; // 메뉴 뒤 클릭이 새지 않게
            b._glow = UiKit.Panel(root, UiTheme.Alpha(UiTheme.Purple, 0.42f), null, "Glow");
            b._glow.sprite = UiTheme.Glow();
            b._glow.rectTransform.anchorMin = b._glow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            b._glow.rectTransform.sizeDelta = new Vector2(2300, 1500);
            b._glow2 = UiKit.Panel(root, UiTheme.Alpha(UiTheme.Cyan, 0.10f), null, "Glow2");
            b._glow2.sprite = UiTheme.Glow();
            b._glow2.rectTransform.anchorMin = b._glow2.rectTransform.anchorMax = new Vector2(0.78f, 0.25f);
            b._glow2.rectTransform.sizeDelta = new Vector2(900, 900);
            b._outerImg = Circle(root, "CircleOuter", UiTheme.MagicCircle(false));
            b._innerImg = Circle(root, "CircleInner", UiTheme.MagicCircle(true));
            b._outer = b._outerImg.rectTransform;
            b._inner = b._innerImg.rectTransform;
            var rng = new System.Random(7);
            for (int i = 0; i < 26; i++)
            {
                var img = UiKit.Panel(root, Color.white, null, "Particle");
                img.sprite = UiTheme.Glow(32);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2((float)rng.NextDouble(), 0f);
                float size = 6f + (float)rng.NextDouble() * 10f;
                rt.sizeDelta = new Vector2(size, size);
                img.color = rng.NextDouble() > 0.5 ? UiTheme.Alpha(UiTheme.Purple, 0.85f) : UiTheme.Alpha(UiTheme.Cyan, 0.8f);
                b._particles.Add(new Particle
                {
                    Rt = rt, Img = img, Size = size,
                    Speed = 40f + (float)rng.NextDouble() * 70f,
                    Drift = 10f + (float)rng.NextDouble() * 25f,
                    Phase = (float)rng.NextDouble() * 1100f,
                });
            }
            // 화면 네 모서리 ㄱ자
            for (int i = 0; i < 4; i++)
            {
                var corner = UiKit.Node(root, "FrameCorner");
                corner.anchorMin = corner.anchorMax = new Vector2(i % 2, i / 2);
                corner.pivot = new Vector2(i % 2, i / 2);
                corner.anchoredPosition = new Vector2(i % 2 == 0 ? 24 : -24, i / 2 == 0 ? 24 : -24);
                corner.sizeDelta = new Vector2(44, 44);
                var h = UiKit.Panel(corner, UiTheme.Alpha(UiTheme.Purple, 0.45f));
                h.rectTransform.anchorMin = new Vector2(0f, i / 2);
                h.rectTransform.anchorMax = new Vector2(1f, i / 2);
                h.rectTransform.sizeDelta = new Vector2(0f, 3f);
                h.rectTransform.anchoredPosition = Vector2.zero;
                h.rectTransform.pivot = new Vector2(0.5f, i / 2);
                var v = UiKit.Panel(corner, UiTheme.Alpha(UiTheme.Purple, 0.45f));
                v.rectTransform.anchorMin = new Vector2(i % 2, 0f);
                v.rectTransform.anchorMax = new Vector2(i % 2, 1f);
                v.rectTransform.sizeDelta = new Vector2(3f, 0f);
                v.rectTransform.anchoredPosition = Vector2.zero;
                v.rectTransform.pivot = new Vector2(i % 2, 0.5f);
            }
            b.SetMood(UiTheme.Purple, 0.22f);
            return b;
        }

        private static Image Circle(RectTransform root, string name, Sprite sprite)
        {
            var img = UiKit.Panel(root, Color.white, null, name);
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            img.rectTransform.sizeDelta = new Vector2(860, 860);
            return img;
        }

        /// <summary>분위기 색과 마법진 밝기 (0~1)</summary>
        public void SetMood(Color tint, float circleAlpha)
        {
            _tint = tint;
            _circleAlpha = circleAlpha;
            if (_glow != null) _glow.color = UiTheme.Alpha(tint, 0.42f);
            if (_outerImg != null) _outerImg.color = new Color(1f, 1f, 1f, circleAlpha);
            if (_innerImg != null) _innerImg.color = new Color(1f, 1f, 1f, circleAlpha);
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            if (_outer != null) _outer.localRotation = Quaternion.Euler(0, 0, -t * 18f);   // 20초에 한 바퀴쯤
            if (_inner != null) _inner.localRotation = Quaternion.Euler(0, 0, t * 24f);
            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 1.6f);
            if (_innerImg != null) _innerImg.color = new Color(1f, 1f, 1f, _circleAlpha * pulse);
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                p.Phase += p.Speed * dt;
                if (p.Phase > 1200f) p.Phase -= 1200f;
                float y = p.Phase - 20f;
                float fade = Mathf.Clamp01(y / 120f) * Mathf.Clamp01((1150f - y) / 160f);
                p.Rt.anchoredPosition = new Vector2(Mathf.Sin((t + i) * 0.4f) * p.Drift, y);
                var c = p.Img.color;
                c.a = fade * 0.85f;
                p.Img.color = c;
                _particles[i] = p;
            }
        }
    }
}
