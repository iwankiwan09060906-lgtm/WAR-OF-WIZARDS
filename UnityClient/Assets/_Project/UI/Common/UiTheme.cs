// 메타 UI(C01~C13) 디자인 토큰 — 팀 VR UI 프로토타입(Figma Make · React, 저장소 루트 UI/ — 2026-10-25 Unity 이식 후 삭제)과 같은 시각 언어.
//   · 바탕: 거의 검은 남보라 #06060f + 가운데 보라 빛 번짐 + 천천히 도는 마법진 + 떠오르는 입자
//   · 판: 반투명 짙은 보라(rgba 8,6,24,.85) + 얇은 보라 테두리 + 네 모서리 ㄱ자 강조선 (VRButton · 카드 공통)
//   · 색: 보라 #7c3aed = 주요 행동, 청록 #06b6d4 = 정보 · 나, 빨강 #ef4444 = 상대 · 위험, 호박 #fbbf24 = 고위력, 주황 #f97316 · 초록 #4ade80 = 통계
//   · 글꼴: 영문 제목 Cinzel (프로토타입 display), 한글 Noto Sans KR — 둘 다 SIL OFL, Resources/Fonts
//   · 스킬 그림: Resources/SkillArt/circle(발동 마법진) · rune(그리는 룬) — 웹 Backend/static/assets/skills 와 같은 파일

using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.UI
{
    public static class UiTheme
    {
        // ── 색 (프로토타입 index.css · Tailwind 보라/청록 단계) ─────────────
        public static readonly Color Bg0 = Hex(0x06060f);
        public static readonly Color Bg1 = Hex(0x0a0818);
        public static readonly Color Bg2 = Hex(0x0d0b22);
        public static readonly Color Panel = new Color(8 / 255f, 6 / 255f, 24 / 255f, 0.88f);
        public static readonly Color Panel2 = new Color(18 / 255f, 12 / 255f, 44 / 255f, 0.88f);
        public static readonly Color Panel3 = new Color(32 / 255f, 22 / 255f, 72 / 255f, 0.92f);
        public static readonly Color Line = new Color(124 / 255f, 58 / 255f, 237 / 255f, 0.28f);
        public static readonly Color Line2 = new Color(124 / 255f, 58 / 255f, 237 / 255f, 0.55f);
        public static readonly Color InputWell = new Color(6 / 255f, 4 / 255f, 20 / 255f, 0.9f);
        public static readonly Color Text = Hex(0xe8e4ff);
        public static readonly Color Text2 = Hex(0xc8c0f0);
        public static readonly Color Text3 = Hex(0x8b80c8);

        public static readonly Color Purple = Hex(0x7c3aed);
        public static readonly Color PurpleLight = Hex(0xa78bfa);
        public static readonly Color PurplePale = Hex(0xc4b5fd);
        /// <summary>주요 행동 (프로토타입 primary = 보라)</summary>
        public static readonly Color Play = Purple;
        public static readonly Color PlayHi = PurpleLight;
        public static readonly Color PlayLo = Hex(0x5b21b6);
        public static readonly Color Cyan = Hex(0x06b6d4);
        public static readonly Color CyanLight = Hex(0x22d3ee);
        public static readonly Color Cyan2 = Hex(0x0e7490);
        public static readonly Color Enemy = Hex(0xef4444);
        public static readonly Color Amber = Hex(0xfbbf24);
        public static readonly Color Orange = Hex(0xf97316);
        public static readonly Color Green = Hex(0x4ade80);
        public static readonly Color Gold = Hex(0xfbbf24);
        public static readonly Color Silver = Hex(0xd9e2ec);
        public static readonly Color Bronze = Hex(0xe09f5e);

        // ── 크기 (캔버스 기준 1920 × 1080) ──────────────────────
        public const float CanvasWidth = 1920f;
        public const float CanvasHeight = 1080f;
        public const int FontBody = 24;
        public const int FontSmall = 20;
        public const int FontLabel = 19;
        public const int FontTitle = 28;
        public const int FontHeadline = 40;
        public const float ButtonHeight = 58f;
        public const float InputHeight = 60f;

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ── 글꼴 ─────────────────────────────────────────────
        private static Font s_regular, s_bold, s_display;

        public static Font Regular => s_regular != null ? s_regular : (s_regular = LoadFont("Fonts/NotoSansKR-Regular"));
        public static Font Bold => s_bold != null ? s_bold : (s_bold = LoadFont("Fonts/NotoSansKR-Bold"));
        /// <summary>영문 제목용 Cinzel (한글이 섞이면 UiKit이 자동으로 Noto Bold를 쓴다)</summary>
        public static Font Display
        {
            get
            {
                if (s_display != null) return s_display;
                s_display = Resources.Load<Font>("Fonts/Cinzel");
                if (s_display == null) s_display = Bold;
                return s_display;
            }
        }

        private static Font LoadFont(string path)
        {
            var f = Resources.Load<Font>(path);
            if (f != null) return f;
            Debug.LogWarning($"[UiTheme] 글꼴 {path} 없음 → OS 한글 글꼴로 대신합니다");
            return Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Noto Sans KR", "NotoSansCJK-Regular", "Apple SD Gothic Neo", "Arial" }, 32);
        }

        public static bool HasHangul(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c >= 0xAC00 && c <= 0xD7A3 || c >= 0x3131 && c <= 0x318E) return true;
            return false;
        }

        // ── 스프라이트 (코드 생성, 캐시) ─────────────────────────
        private static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>();

        /// <summary>
        /// 프로토타입 판 · 버튼: fill 색으로 채운 둥근(4px) 사각형 + border 색 테두리.
        /// accent(코너) > 0 이면 네 모서리에 밝은 ㄱ자 강조선을 그린다 (VRButton corner accents).
        /// 9-slice라 어떤 크기에도 늘어난다 (Image.type = Sliced). 시그니처는 예전 깎은 모서리 판과 같다.
        /// </summary>
        public static Sprite Cut(Color fill, Color border, int accent = 12, int borderPx = 2)
        {
            string key = $"frame{ColorUtility.ToHtmlStringRGBA(fill)}{ColorUtility.ToHtmlStringRGBA(border)}{accent}_{borderPx}";
            if (s_sprites.TryGetValue(key, out var cached)) return cached;
            int corner = accent > 0 ? Mathf.Clamp(accent + 4, 10, 24) : 0; // ㄱ자 길이
            const float radius = 4f;
            float bw = Mathf.Max(1f, borderPx * 0.75f); // 얇은 테두리
            int size = Mathf.Max(32, corner * 2 + 12);
            Color accentColor = border.a <= 0.02f ? border : new Color(Mathf.Lerp(border.r, 1f, 0.25f), Mathf.Lerp(border.g, 1f, 0.25f), Mathf.Lerp(border.b, 1f, 0.25f), Mathf.Min(1f, border.a * 1.7f + 0.15f));
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = key };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    // 둥근 사각형까지의 부호 거리 (안쪽 양수)
                    float qx = Mathf.Max(radius - fx, fx - (size - radius), 0f);
                    float qy = Mathf.Max(radius - fy, fy - (size - radius), 0f);
                    float outside = Mathf.Sqrt(qx * qx + qy * qy);
                    float inside = Mathf.Min(Mathf.Min(fx, fy), Mathf.Min(size - fx, size - fy));
                    float d = outside > 0f ? radius - outside : inside;
                    if (outside > 0f) d = radius - outside;
                    float alpha = Mathf.Clamp01(d + 0.5f);
                    Color c = d < bw ? border : fill;
                    // 코너 강조선: 모서리에서 corner px 안의 바깥 2px 띠
                    if (corner > 0)
                    {
                        float ex = Mathf.Min(fx, size - fx), ey = Mathf.Min(fy, size - fy);
                        bool nearCorner = ex < corner && ey < corner;
                        if (nearCorner && Mathf.Min(ex, ey) < 2.4f) c = accentColor;
                    }
                    c.a *= alpha;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            float b = Mathf.Max(corner, radius) + 2;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s_sprites[key] = sprite;
            return sprite;
        }

        /// <summary>둥근 원 (아바타 · 접속 점 · 입자)</summary>
        public static Sprite Circle(int size = 128)
        {
            string key = "circle" + size;
            if (s_sprites.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d));
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_sprites[key] = sprite;
            return sprite;
        }

        /// <summary>부드러운 빛 번짐 원 (가운데 1 → 가장자리 0) — 배경 · 입자 빛</summary>
        public static Sprite Glow(int size = 128)
        {
            string key = "glow" + size;
            if (s_sprites.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_sprites[key] = sprite;
            return sprite;
        }

        /// <summary>원 테두리 (고위력 마법진 링 등)</summary>
        public static Sprite Ring(int size = 128, float thickness = 0.06f)
        {
            string key = $"ring{size}_{thickness}";
            if (s_sprites.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color[size * size];
            float r = size * 0.5f, t = size * thickness;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d) * Mathf.Clamp01(d - (r - t) + 0.5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_sprites[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 프로토타입 MagicCircle.tsx를 두 겹으로 나눈 그림.
        /// outer = 바깥 보라 고리 + 룬 점선 + 십자선 (시계 방향으로 천천히), inner = 청록 고리 + 오각별 + 안쪽 고리 + 가운데 구슬 (반대로)
        /// </summary>
        public static Sprite MagicCircle(bool inner)
        {
            string key = inner ? "magicInner" : "magicOuter";
            if (s_sprites.TryGetValue(key, out var cached)) return cached;
            const int size = 768;
            float c = size * 0.5f;
            float r1 = size * 0.46f, r2 = size * 0.38f, r3 = size * 0.28f;
            var star = new Vector2[5];
            for (int i = 0; i < 5; i++)
            {
                float a = i * 4f * Mathf.PI / 5f - Mathf.PI / 2f;
                star[i] = new Vector2(c + r2 * 0.92f * Mathf.Cos(a), c + r2 * 0.92f * Mathf.Sin(a));
            }
            Color purple = Alpha(Purple, 1f), cyan = Alpha(Cyan, 1f), light = Alpha(PurpleLight, 1f);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float dist = Vector2.Distance(p, new Vector2(c, c));
                    float ang = Mathf.Atan2(p.y - c, p.x - c);
                    Color col = Color.clear;
                    if (!inner)
                    {
                        col = Over(col, purple, Stroke(Mathf.Abs(dist - r1), 1.6f) * 0.55f + Stroke(Mathf.Abs(dist - r1), 7f) * 0.12f);
                        // 룬 점선 (바깥 고리 바깥쪽)
                        float dash = Mathf.Repeat(ang * r1 / 21f, 1f) < 0.16f ? 1f : 0f;
                        col = Over(col, purple, Stroke(Mathf.Abs(dist - (r1 + 9f)), 1.2f) * dash * 0.45f);
                        // 십자 · 대각선
                        if (dist < r1)
                        {
                            float axis = Mathf.Min(Mathf.Abs(p.x - c), Mathf.Abs(p.y - c));
                            float diag = Mathf.Min(Mathf.Abs((p.x - c) - (p.y - c)), Mathf.Abs((p.x - c) + (p.y - c))) / 1.414f;
                            col = Over(col, purple, Stroke(axis, 0.8f) * 0.18f);
                            col = Over(col, cyan, Stroke(diag, 0.8f) * 0.14f);
                        }
                    }
                    else
                    {
                        col = Over(col, cyan, Stroke(Mathf.Abs(dist - r2), 1.3f) * 0.5f + Stroke(Mathf.Abs(dist - r2), 6f) * 0.1f);
                        float dash = Mathf.Repeat(ang * r2 / 18f, 1f) < 0.33f ? 1f : 0f;
                        col = Over(col, cyan, Stroke(Mathf.Abs(dist - (r2 - 8f)), 1f) * dash * 0.2f);
                        col = Over(col, light, Stroke(Mathf.Abs(dist - r3), 1.3f) * 0.45f);
                        float sd = float.MaxValue;
                        for (int i = 0; i < 5; i++) sd = Mathf.Min(sd, SegDist(p, star[i], star[(i + 2) % 5]));
                        col = Over(col, purple, Stroke(sd, 1.1f) * 0.45f + Stroke(sd, 5f) * 0.08f);
                        float orb = size * 0.04f;
                        col = Over(col, light, Stroke(Mathf.Abs(dist - orb), 1.3f) * 0.7f);
                        if (dist < orb) col = Over(col, purple, 0.2f);
                    }
                    px[y * size + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_sprites[key] = sprite;
            return sprite;
        }

        private static float Stroke(float d, float w) => Mathf.Clamp01(1f - d / w);

        private static Color Over(Color under, Color over, float a)
        {
            a = Mathf.Clamp01(a);
            if (a <= 0f) return under;
            float outA = a + under.a * (1f - a);
            if (outA <= 0f) return Color.clear;
            Color rgb = (over * a + under * under.a * (1f - a)) / outA;
            return new Color(rgb.r, rgb.g, rgb.b, outA);
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ── 스킬 그림 ─────────────────────────────────────────
        public static Sprite SkillCircle(string code) => LoadArt("SkillArt/circle/" + code);
        public static Sprite SkillRune(string code) => LoadArt("SkillArt/rune/" + code);

        private static readonly Dictionary<int, Vector2[]> s_guideStarts = new Dictionary<int, Vector2[]>();

        /// <summary>
        /// 룬 그리는 방법 그림 (RuneDesigns 의 획) — 획마다 시작은 초록 점, 진행할수록 흐린 청록 → 흰색, 끝에 화살표.
        /// starts = 획 시작점 (0~1, 번호 표시용). 밉맵을 만들어 VR 에서 작게 보여도 반짝이지 않게 한다. 디자인이 없으면 null
        /// </summary>
        public static Sprite RuneGuide(int spellId, out Vector2[] starts)
        {
            string key = "guide" + spellId;
            starts = null;
            if (s_sprites.TryGetValue(key, out var cached))
            {
                s_guideStarts.TryGetValue(spellId, out starts);
                return cached;
            }
            if (!Gesture.RuneDesigns.TryGet(spellId, out var design)) return null;

            const int size = 128;
            const float margin = 0.2f;
            var px = new Color32[size * size];
            Vector2 ToPx(Vector2 p) => new Vector2(margin + p.x * (1f - 2f * margin), margin + p.y * (1f - 2f * margin)) * size;

            // 원판 안을 칠하는 둥근 붓 (가장 진한 값만 남긴다)
            void Stamp(Vector2 c, float r, Color col)
            {
                int x0 = Mathf.Max(0, (int)(c.x - r - 1)), x1 = Mathf.Min(size - 1, (int)(c.x + r + 1));
                int y0 = Mathf.Max(0, (int)(c.y - r - 1)), y1 = Mathf.Min(size - 1, (int)(c.y + r + 1));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float a = Mathf.Clamp01(r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) + 0.5f) * col.a;
                        int i = y * size + x;
                        if (a * 255f <= px[i].a) continue;
                        px[i] = new Color(col.r, col.g, col.b, a);
                    }
            }
            void Line(Vector2 a, Vector2 b, float r, Color ca, Color cb)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 2f));
                for (int s = 0; s <= steps; s++) Stamp(Vector2.Lerp(a, b, s / (float)steps), r, Color.Lerp(ca, cb, s / (float)steps));
            }

            Color dim = Alpha(CyanLight, 0.55f), bright = Color.white, startCol = Hex(0x4ade80);
            var startList = new Vector2[design.Strokes.Length];
            for (int k = 0; k < design.Strokes.Length; k++)
            {
                var stroke = design.Strokes[k];
                if (stroke == null || stroke.Length < 2) continue;
                for (int i = 1; i < stroke.Length; i++)
                {
                    float t0 = (i - 1) / (float)(stroke.Length - 1), t1 = i / (float)(stroke.Length - 1);
                    Line(ToPx(stroke[i - 1]), ToPx(stroke[i]), 3.2f, Color.Lerp(dim, bright, t0), Color.Lerp(dim, bright, t1));
                }
                // 끝 화살표: 마지막 점에서 몇 px 뒤 방향 기준 ±30°
                Vector2 tip = ToPx(stroke[stroke.Length - 1]);
                Vector2 back = tip;
                for (int i = stroke.Length - 2; i >= 0 && Vector2.Distance(back, tip) < 6f; i--) back = ToPx(stroke[i]);
                Vector2 dir = (tip - back).sqrMagnitude > 1e-4f ? (tip - back).normalized : Vector2.right;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 wing = (Vector2)(Quaternion.Euler(0f, 0f, 150f * side) * dir);
                    Line(tip, tip + wing * 13f, 3f, bright, bright);
                }
                startList[k] = ToPx(stroke[0]) / size;
            }
            // 시작 점은 선 위에 (나중에 그려서 덮는다)
            for (int k = 0; k < startList.Length; k++)
            {
                Vector2 c = startList[k] * size;
                for (int i = 0; i < px.Length; i++)
                {
                    float d = Vector2.Distance(new Vector2(i % size + 0.5f, i / size + 0.5f), c);
                    if (d < 10.5f) px[i] = Color32.Lerp(px[i], startCol, Mathf.Clamp01(10.5f - d));
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = key };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_sprites[key] = sprite;
            s_guideStarts[spellId] = startList;
            starts = startList;
            return sprite;
        }

        private static Sprite LoadArt(string path)
        {
            if (s_sprites.TryGetValue(path, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>(path);
            Sprite s = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            s_sprites[path] = s;
            return s;
        }

        // ── 마법사 그림 (파랑 = 나, 빨강 = 상대) ──────────────────
        /// <summary>마법사 실루엣 — 고깔모자 · 얼굴 그림자 · 로브 · 지팡이 · 빛나는 구슬 (나 = 청록 · 보라, 상대 = 빨강)</summary>
        public static Sprite Wizard(bool foe)
        {
            string key = foe ? "wizardFoe" : "wizardYou";
            if (s_sprites.TryGetValue(key, out var cached)) return cached;
            const int w = 256, h = 320;
            Color robe1 = foe ? Hex(0xc4405a) : Hex(0x6d3fd8), robe2 = foe ? Hex(0x4a1220) : Hex(0x1b1250);
            Color hat = foe ? Hex(0xa32f47) : Hex(0x5b2bc4), brim = foe ? Hex(0x2c0912) : Hex(0x140c38);
            Color glow = foe ? Hex(0xff7d95) : Hex(0x22d3ee), eye = foe ? Hex(0xffd7df) : Hex(0xcffafe);
            Color staff = Hex(0x5f4532), face = Hex(0x0c0a18), beard = Hex(0xd9dde3), belt = foe ? Hex(0xcdb27a) : Hex(0xa5f3fc);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fx = foe ? w - 1 - x : x; // 상대는 좌우 반전 (서로 마주 봄)
                    float fy = h - 1 - y;           // 위 = 0
                    Color c = Color.clear;
                    if (Ellipse(fx, fy, 128, 312, 80, 8)) c = new Color(0, 0, 0, 0.45f);
                    if (fx > 52 && fx < 60 && fy > 64 && fy < 310) c = staff;
                    float orb = Vector2.Distance(new Vector2(fx, fy), new Vector2(56, 56));
                    if (orb < 34f) c = Blend(c, glow, Mathf.Clamp01(1f - orb / 34f) * 0.9f);
                    if (orb < 8f) c = Color.white;
                    float t = Mathf.InverseLerp(118, 306, fy);
                    float half = Mathf.Lerp(26, 76, t);
                    if (fy >= 118 && fy <= 306 && Mathf.Abs(fx - 124) < half)
                    {
                        c = Color.Lerp(robe1, robe2, t);
                        if (Mathf.Abs(fx - 124) > half - 4) c = Blend(c, glow, 0.6f);
                        if (fy > 180 && fy < 192) c = belt;
                    }
                    if (Ellipse(fx, fy, 126, 136, 18, 34) && fy > 110) c = beard;
                    if (Ellipse(fx, fy, 124, 100, 22, 24)) c = face;
                    if (Vector2.Distance(new Vector2(fx, fy), new Vector2(130, 98)) < 3.2f || Vector2.Distance(new Vector2(fx, fy), new Vector2(141, 97)) < 3.2f) c = eye;
                    if (Ellipse(fx, fy, 122, 80, 60, 11)) c = brim;
                    float ht = Mathf.InverseLerp(6, 78, fy);
                    float hatHalf = Mathf.Lerp(2, 38, ht);
                    float hatCx = Mathf.Lerp(96, 122, ht);
                    if (fy >= 6 && fy <= 78 && Mathf.Abs(fx - hatCx) < hatHalf) c = Color.Lerp(hat, brim, 1f - ht);
                    if (fy > 70 && fy < 77 && Mathf.Abs(fx - 122) < 38) c = belt;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            s_sprites[key] = sprite;
            return sprite;
        }

        private static bool Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return dx * dx + dy * dy <= 1f;
        }

        private static Color Blend(Color under, Color over, float a)
        {
            if (under.a <= 0f) return new Color(over.r, over.g, over.b, a);
            return Color.Lerp(under, new Color(over.r, over.g, over.b, 1f), a);
        }
    }
}
