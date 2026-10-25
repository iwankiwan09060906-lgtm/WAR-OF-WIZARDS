// §9 템플릿 증강 — 기본 도형 한 개를 "사람이 그린 것처럼" 흔든 변형 여러 개로 늘린다.
//   최근접 템플릿 방식은 템플릿이 다양할수록 강해진다. 벤치마크(노이즈 궤적 1,140개, 19종 전부 후보)에서
//   변형 4개 → 24개로 늘리자 정답률 95.2% → 99.4%, S07 가로줄 ↔ H02 별표 ↔ S09 # 혼동이 거의 사라졌다.
//   흔드는 방법: 가로 · 세로 비율, 회전 ±11°, 기울임, 획 위치 · 크기 어긋남, 획 끝 늘리기 · 줄이기,
//               손 떨림, 모서리 둥글게, 획 순서 · 방향 섞기, 모서리에서 끊어 그리기 · 이어 그리기.
//   시드가 고정이라 모든 클라이언트에서 같은 템플릿이 나온다 (시작할 때 한 번, 수 ms).

using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Gesture
{
    public static class RuneAugmenter
    {
        /// <summary>기본 도형 하나당 만들 변형 수</summary>
        public const int DefaultVariants = 20;
        /// <summary>흔드는 세기 (1 = 보통 사람 손, 벤치마크 최적 0.9)</summary>
        public const float DefaultLevel = 0.9f;

        public static List<Vector2[][]> Variants(Vector2[][] strokes, int count, int seed, float level = DefaultLevel)
        {
            var rng = new System.Random(seed);
            var list = new List<Vector2[][]>(count);
            for (int i = 0; i < count; i++) list.Add(Humanize(strokes, level, rng));
            return list;
        }

        public static Vector2[][] Humanize(Vector2[][] design, float level, System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Gauss()
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
            }

            // 획 끊어 그리기 (긴 획의 가운데쯤에서 펜을 뗌)
            var strokes = new List<Vector2[]>(design.Length + 2);
            foreach (var s in design)
            {
                if (s == null || s.Length < 2) continue;
                if (s.Length > 20 && rng.NextDouble() < 0.12 * level)
                {
                    int cut = rng.Next(s.Length / 3, 2 * s.Length / 3);
                    strokes.Add(Slice(s, 0, cut + 1));
                    strokes.Add(Slice(s, cut, s.Length - cut));
                }
                else strokes.Add(s);
            }
            // 획 순서 섞기 · 방향 뒤집기
            if (rng.NextDouble() < 0.5)
            {
                for (int i = strokes.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (strokes[i], strokes[j]) = (strokes[j], strokes[i]);
                }
            }
            for (int i = 0; i < strokes.Count; i++)
                if (rng.NextDouble() < 0.4) strokes[i] = Reversed(strokes[i]);
            // 이어 그리기 (앞 획 끝과 다음 획 시작이 가까우면)
            var merged = new List<Vector2[]>(strokes.Count);
            foreach (var s in strokes)
            {
                if (merged.Count > 0 && (merged[merged.Count - 1][merged[merged.Count - 1].Length - 1] - s[0]).magnitude < 0.2f &&
                    rng.NextDouble() < 0.35 * level)
                    merged[merged.Count - 1] = Concat(merged[merged.Count - 1], s);
                else merged.Add(s);
            }

            // 전체 변형: 가로 · 세로 비율, 회전, 기울임
            float sx = R(1f - 0.22f * level, 1f + 0.28f * level), sy = R(1f - 0.22f * level, 1f + 0.28f * level);
            float rot = R(-12f, 12f) * level * Mathf.Deg2Rad, shear = R(-0.15f, 0.15f) * level;
            float cos = Mathf.Cos(rot), sin = Mathf.Sin(rot);
            float phase = R(0f, 100f);
            var result = new Vector2[merged.Count][];
            for (int si = 0; si < merged.Count; si++)
            {
                var s0 = merged[si];
                var off = new Vector2(Gauss(), Gauss()) * 0.045f * level;
                float ps = 1f + Gauss() * 0.08f * level;
                Vector2 c = Vector2.zero;
                for (int i = 0; i < s0.Length; i++) c += s0[i];
                c /= s0.Length;
                var s = Extend(s0, R(-0.12f, 0.12f) * level, R(-0.12f, 0.12f) * level);
                float wobA = R(0.01f, 0.03f) * level, wobF = R(2f, 6f);
                var pts = new Vector2[s.Length];
                for (int i = 0; i < s.Length; i++)
                {
                    var p = (s[i] - c) * ps + c + off;
                    float t = (float)i / Mathf.Max(1, s.Length - 1);
                    p += new Vector2(Mathf.Sin(t * wobF + phase), Mathf.Cos(t * wobF * 1.3f + phase)) * wobA;
                    p += new Vector2(Gauss(), Gauss()) * 0.004f * level;
                    p = new Vector2(p.x + shear * p.y, p.y);
                    p = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
                    pts[i] = new Vector2(p.x * sx, p.y * sy);
                }
                result[si] = Smooth(pts, rng.Next(0, 3));
            }
            return result;
        }

        private static Vector2[] Slice(Vector2[] s, int start, int length)
        {
            var r = new Vector2[length];
            System.Array.Copy(s, start, r, 0, length);
            return r;
        }

        private static Vector2[] Reversed(Vector2[] s)
        {
            var r = new Vector2[s.Length];
            for (int i = 0; i < s.Length; i++) r[i] = s[s.Length - 1 - i];
            return r;
        }

        private static Vector2[] Concat(Vector2[] a, Vector2[] b)
        {
            var r = new Vector2[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        /// <summary>획 앞 · 뒤를 길이 비율만큼 늘리거나(+) 줄인다(-)</summary>
        private static Vector2[] Extend(Vector2[] s, float startFrac, float endFrac)
        {
            if (s.Length < 3) return s;
            float len = 0f;
            for (int i = 1; i < s.Length; i++) len += (s[i] - s[i - 1]).magnitude;
            var list = new List<Vector2>(s);
            Vector2 d0 = (s[0] - s[2]).normalized, d1 = (s[s.Length - 1] - s[s.Length - 3]).normalized;
            if (startFrac > 0f) list.Insert(0, s[0] + d0 * startFrac * len * 0.5f);
            else Trim(list, -startFrac * 0.5f, true);
            if (endFrac > 0f) list.Add(list[list.Count - 1] + d1 * endFrac * len * 0.5f);
            else Trim(list, -endFrac * 0.5f, false);
            return list.ToArray();
        }

        private static void Trim(List<Vector2> l, float frac, bool start)
        {
            int n = (int)(l.Count * frac);
            if (n <= 0 || l.Count - n < 3) return;
            l.RemoveRange(start ? 0 : l.Count - n, n);
        }

        private static Vector2[] Smooth(Vector2[] pts, int window)
        {
            if (window <= 0) return pts;
            var sm = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = Vector2.zero;
                int n = 0;
                for (int k = -window; k <= window; k++)
                {
                    a += pts[Mathf.Clamp(i + k, 0, pts.Length - 1)];
                    n++;
                }
                sm[i] = a / n;
            }
            return sm;
        }
    }
}
