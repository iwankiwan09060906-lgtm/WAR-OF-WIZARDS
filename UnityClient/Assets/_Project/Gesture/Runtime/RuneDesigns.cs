// 19종 룬 기본 도형 (디자인: Assets/Art/Runes/Designs/{SkillId}.png) — 사람이 그리는 획 단위로 나눈 궤적
// 좌표: 0~1 상자, x = 오른쪽, y = 위. 획 순서 · 방향은 인식에 거의 영향이 없다 ($P 점 구름).
// 템플릿 샘플은 기본 도형 + 가로 · 세로로 늘린 것 + 기울인 것 (Spellbound > Runes > Build Rune Templates).
// 실제 손으로 그린 궤적은 Spellbound > Runes > Record Last Stroke로 샘플을 더 넣는다.

using System.Collections.Generic;
using SpellboundVR.Spells;
using UnityEngine;

namespace SpellboundVR.Gesture
{
    public static class RuneDesigns
    {
        public struct Design
        {
            public int SpellId;
            public string Name;
            public Vector2[][] Strokes;
        }

        private const int Dense = 12; // 직선 구간당 점 수

        private static Design[] s_all;

        public static IReadOnlyList<Design> All => s_all ??= Build();

        public static bool TryGet(int spellId, out Design design)
        {
            foreach (var d in All)
            {
                if (d.SpellId != spellId) continue;
                design = d;
                return true;
            }
            design = default;
            return false;
        }

        /// <summary>기본 도형 + 변형 (가로 1.25배 · 세로 1.25배 · 오른쪽으로 기울임)</summary>
        public static List<(string label, Vector2[][] strokes)> Samples(Design d)
        {
            return new List<(string, Vector2[][])>
            {
                ("base", d.Strokes),
                ("wide", Transform(d.Strokes, 1.25f, 1f, 0f)),
                ("tall", Transform(d.Strokes, 1f, 1.25f, 0f)),
                ("italic", Transform(d.Strokes, 1f, 1f, 0.18f)),
            };
        }

        /// <summary>
        /// 사람이 같은 룬을 다른 획 구성으로 그리는 방식 (템플릿 에셋과 상관없이 인식기가 템플릿을 만들 때 더한다).
        /// S02 독 장판(삼각형 + X)은 VR에서 핀치를 쥔 채 한 번에 이어 그리거나, 삼각형을 두 · 세 획으로 나눠 그리는 경우가 많다.
        /// </summary>
        public static List<(string label, Vector2[][] strokes)> ExtraStyles(int spellId)
        {
            var list = new List<(string, Vector2[][])>();
            if (spellId == SpellIds.S02_PoisonField && TryGet(spellId, out var d) && d.Strokes.Length == 3)
            {
                Vector2[] tri = d.Strokes[0], x1 = d.Strokes[1], x2 = d.Strokes[2];
                int third = (tri.Length - 1) / 3;
                Vector2[] e1 = Slice(tri, 0, third), e2 = Slice(tri, third, 2 * third), e3 = Slice(tri, 2 * third, tri.Length - 1);
                list.Add(("style-tri-xchain", new[] { tri, Chain(x1, x2) }));                      // 삼각형 + X 를 한 획으로
                list.Add(("style-all-one", new[] { Chain(tri, x1, x2) }));                         // 전부 한 번에 이어서
                list.Add(("style-x-first-one", new[] { Chain(x1, x2, tri) }));                     // X 부터 이어서
                list.Add(("style-tri-two", new[] { Chain(e1, e2), e3, x1, x2 }));                  // 삼각형을 두 획으로
                list.Add(("style-tri-edges", new[] { e1, e2, e3, x1, x2 }));                       // 삼각형을 세 변으로
                list.Add(("style-tri-edges-xchain", new[] { e1, e2, e3, Chain(x1, x2) }));
            }
            return list;
        }

        private static Vector2[] Slice(Vector2[] pts, int from, int to)
        {
            var r = new Vector2[to - from + 1];
            System.Array.Copy(pts, from, r, 0, r.Length);
            return r;
        }

        /// <summary>획들을 펜을 떼지 않고 이은 한 획 — 다음 획의 가까운 끝으로 이어지도록 방향을 고른다</summary>
        private static Vector2[] Chain(params Vector2[][] parts)
        {
            var result = new List<Vector2>(parts[0]);
            for (int i = 1; i < parts.Length; i++)
            {
                var next = parts[i];
                Vector2 end = result[result.Count - 1];
                bool reverse = (next[next.Length - 1] - end).sqrMagnitude < (next[0] - end).sqrMagnitude;
                if (reverse) for (int k = next.Length - 1; k >= 0; k--) result.Add(next[k]);
                else result.AddRange(next);
            }
            return result.ToArray();
        }

        private static Vector2[][] Transform(Vector2[][] strokes, float sx, float sy, float shear)
        {
            var result = new Vector2[strokes.Length][];
            for (int s = 0; s < strokes.Length; s++)
            {
                result[s] = new Vector2[strokes[s].Length];
                for (int i = 0; i < strokes[s].Length; i++)
                {
                    var p = strokes[s][i];
                    result[s][i] = new Vector2((p.x + shear * p.y) * sx, p.y * sy);
                }
            }
            return result;
        }

        private static Design[] Build()
        {
            return new[]
            {
                // S01 연막 — 눈: 아몬드 윤곽 + 눈동자
                D(SpellIds.S01_Smoke, "Smoke", Almond(0.5f, 0.5f, 0.5f, 0.3f), Circle(0.5f, 0.5f, 0.15f)),
                // S02 독 장판 — 삼각형(한 획) + 아래쪽 안의 X(두 획). 좌표는 image (4).png 를 비율 그대로 옮긴 것 (폭 1.21 : 높이 1)
                D(SpellIds.S02_PoisonField, "PoisonField",
                  Poly(V(0.605f, 1f), V(1.21f, 0f), V(0f, 0f), V(0.605f, 1f)),
                  Poly(V(0.35f, 0.295f), V(0.85f, 0.11f)),
                  Poly(V(0.35f, 0.11f), V(0.85f, 0.295f))),
                // S03 파이어볼 — 왕관
                D(SpellIds.S03_Fireball, "Fireball",
                  Poly(V(0.05f, 0.78f), V(0.3f, 0.52f), V(0.5f, 1f), V(0.7f, 0.52f), V(0.95f, 0.78f), V(0.8f, 0.08f), V(0.2f, 0.08f), V(0.05f, 0.78f))),
                // S04 화살비 — 가운데 세로선 + 아래로 모이는 V
                D(SpellIds.S04_ArrowRain, "ArrowRain",
                  Poly(V(0.5f, 1f), V(0.5f, 0f)),
                  Poly(V(0.05f, 0.8f), V(0.5f, 0f), V(0.95f, 0.8f))),
                // S05 토네이도 — 안에서 밖으로 감기는 나선
                D(SpellIds.S05_Tornado, "Tornado", Spiral(0.5f, 0.5f, 0.5f, 2.5f)),
                // S06 번개 — 번개 윤곽
                D(SpellIds.S06_Lightning, "Lightning",
                  Poly(V(0.62f, 1f), V(0.2f, 0.42f), V(0.48f, 0.42f), V(0.38f, 0f), V(0.8f, 0.6f), V(0.52f, 0.6f), V(0.62f, 1f))),
                // S07 총난사 — 가로줄 5개
                D(SpellIds.S07_Gatling, "Gatling",
                  Poly(V(0f, 1f), V(1f, 1f)), Poly(V(0f, 0.75f), V(1f, 0.75f)), Poly(V(0f, 0.5f), V(1f, 0.5f)),
                  Poly(V(0f, 0.25f), V(1f, 0.25f)), Poly(V(0f, 0f), V(1f, 0f))),
                // S08 방어 쉴드 — 윗변이 V로 들어간 방패
                D(SpellIds.S08_Shield, "Shield",
                  Poly(V(0f, 0.92f), V(0.5f, 0.78f), V(1f, 0.92f), V(1f, 0.32f), V(0.5f, 0f), V(0f, 0.32f), V(0f, 0.92f))),
                // S09 타워 강화 — #
                D(SpellIds.S09_TowerBuff, "TowerBuff",
                  Poly(V(0.35f, 1f), V(0.27f, 0f)), Poly(V(0.73f, 1f), V(0.65f, 0f)),
                  Poly(V(0f, 0.68f), V(1f, 0.68f)), Poly(V(0f, 0.3f), V(1f, 0.3f))),
                // S10 집중 — 위쪽 화살표 윤곽
                D(SpellIds.S10_Focus, "Focus",
                  Poly(V(0.34f, 0f), V(0.34f, 0.5f), V(0.05f, 0.5f), V(0.5f, 1f), V(0.95f, 0.5f), V(0.66f, 0.5f), V(0.66f, 0f), V(0.34f, 0f))),
                // S11 도끼 전사 — 굵은 X 윤곽
                D(SpellIds.S11_AxeWarrior, "AxeWarrior",
                  Poly(V(0.14f, 1f), V(0.5f, 0.64f), V(0.86f, 1f), V(1f, 0.86f), V(0.64f, 0.5f), V(1f, 0.14f), V(0.86f, 0f),
                       V(0.5f, 0.36f), V(0.14f, 0f), V(0f, 0.14f), V(0.36f, 0.5f), V(0f, 0.86f), V(0.14f, 1f))),
                // S12 마법사 — 육각별 (삼각형 두 개)
                D(SpellIds.S12_Mage, "Mage",
                  Poly(V(0.5f, 1f), V(0.07f, 0.25f), V(0.93f, 0.25f), V(0.5f, 1f)),
                  Poly(V(0.5f, 0f), V(0.93f, 0.75f), V(0.07f, 0.75f), V(0.5f, 0f))),
                // S13 포탑 — I 윤곽 (위아래 넓은 판 + 가는 기둥)
                D(SpellIds.S13_Turret, "Turret",
                  Poly(V(0.3f, 1f), V(0.7f, 1f), V(0.7f, 0.84f), V(0.58f, 0.84f), V(0.58f, 0.16f), V(0.7f, 0.16f), V(0.7f, 0f),
                       V(0.3f, 0f), V(0.3f, 0.16f), V(0.42f, 0.16f), V(0.42f, 0.84f), V(0.3f, 0.84f), V(0.3f, 1f))),
                // H01 빙결 — 원 + 모래시계
                D(SpellIds.H01_Freeze, "Freeze", Circle(0.5f, 0.5f, 0.5f),
                  Poly(V(0.25f, 0.82f), V(0.75f, 0.82f), V(0.25f, 0.18f), V(0.75f, 0.18f), V(0.25f, 0.82f))),
                // H02 레이저 — 8방향 별표 (선 4개)
                D(SpellIds.H02_Laser, "Laser",
                  Poly(V(0.5f, 1f), V(0.5f, 0f)), Poly(V(0f, 0.5f), V(1f, 0.5f)),
                  Poly(V(0.15f, 0.85f), V(0.85f, 0.15f)), Poly(V(0.85f, 0.85f), V(0.15f, 0.15f))),
                // H03 흡수 — 하트 + 가로지르는 심전도
                D(SpellIds.H03_Drain, "Drain", Heart(0.5f, 0.45f, 0.4f),
                  Poly(V(0f, 0.45f), V(0.38f, 0.45f), V(0.46f, 0.62f), V(0.54f, 0.3f), V(0.62f, 0.45f), V(1f, 0.45f))),
                // H04 반사 장벽 — 원 + 아래에서 위로 뚫고 나가는 화살표
                D(SpellIds.H04_ReflectBarrier, "ReflectBarrier", Ellipse(0.5f, 0.38f, 0.3f, 0.36f),
                  Poly(V(0.5f, -0.1f), V(0.5f, 1f)), Poly(V(0.36f, 0.86f), V(0.5f, 1f), V(0.64f, 0.86f))),
                // H05 분노 — 얼굴 원 + 찌푸린 눈 두 개 + 처진 입
                D(SpellIds.H05_Rage, "Rage", Circle(0.5f, 0.5f, 0.5f),
                  Poly(V(0.27f, 0.7f), V(0.44f, 0.6f)), Poly(V(0.56f, 0.6f), V(0.73f, 0.7f)),
                  Arc(0.5f, 0.2f, 0.18f, 0.12f, 180f, 0f)),
                // H06 타워 소환 — 지붕 삼각형 + 몸체
                D(SpellIds.H06_SummonTower, "SummonTower",
                  Poly(V(0f, 0.62f), V(0.5f, 1f), V(1f, 0.62f), V(0f, 0.62f)),
                  Poly(V(0.15f, 0.62f), V(0.15f, 0f), V(0.85f, 0f), V(0.85f, 0.62f))),
            };
        }

        // ── 도형 조각 ────────────────────────────────────────

        private static Design D(int spellId, string name, params Vector2[][] strokes) =>
            new Design { SpellId = spellId, Name = name, Strokes = strokes };

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        public static Vector2[] Poly(params Vector2[] corners) => RuneTemplateLibrary.Polyline(corners, Dense);

        private static Vector2[] Circle(float cx, float cy, float r) => Ellipse(cx, cy, r, r);

        private static Vector2[] Ellipse(float cx, float cy, float rx, float ry) => Arc(cx, cy, rx, ry, 90f, 450f);

        /// <summary>타원 호: fromDeg → toDeg (반시계 +)</summary>
        private static Vector2[] Arc(float cx, float cy, float rx, float ry, float fromDeg, float toDeg)
        {
            int n = Mathf.Max(8, Mathf.RoundToInt(Mathf.Abs(toDeg - fromDeg) / 7.5f));
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, (float)i / n) * Mathf.Deg2Rad;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            }
            return pts;
        }

        /// <summary>눈 모양: 왼쪽 끝 → 위 호 → 오른쪽 끝 → 아래 호 → 왼쪽 끝 (한 획)</summary>
        private static Vector2[] Almond(float cx, float cy, float halfWidth, float halfHeight)
        {
            const int n = 24;
            var pts = new Vector2[n * 2 + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                pts[i] = new Vector2(cx - halfWidth + 2f * halfWidth * t, cy + halfHeight * Mathf.Sin(Mathf.PI * t));
            }
            for (int i = 1; i <= n; i++)
            {
                float t = (float)i / n;
                pts[n + i] = new Vector2(cx + halfWidth - 2f * halfWidth * t, cy - halfHeight * Mathf.Sin(Mathf.PI * t));
            }
            return pts;
        }

        /// <summary>가운데에서 바깥으로 감기는 나선 (turns 바퀴)</summary>
        private static Vector2[] Spiral(float cx, float cy, float radius, float turns)
        {
            int n = Mathf.RoundToInt(turns * 40f);
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float a = t * turns * Mathf.PI * 2f;
                float r = radius * (0.08f + 0.92f * t);
                pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }
            return pts;
        }

        /// <summary>하트 윤곽 (아래 꼭짓점에서 시작해 한 바퀴)</summary>
        private static Vector2[] Heart(float cx, float cy, float size)
        {
            const int n = 64;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.PI * 2f * i / n + Mathf.PI; // 아래 꼭짓점부터
                float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                pts[i] = new Vector2(cx + x / 16f * size, cy + y / 16f * size);
            }
            return pts;
        }
    }
}
