// §9 $P+ Pipeline — 리샘플링: 궤적을 경로 길이 기준 등간격 N개 점으로 만든다 ($P 원 논문 방식, 다중 획 지원).

using UnityEngine;

namespace SpellboundVR.Gesture
{
    public static class GestureResampler
    {
        public const int DefaultPointCount = 32;

        public static GesturePoint[] Resample(GesturePoint[] points, int n)
        {
            var result = new GesturePoint[n];
            if (points == null || points.Length == 0) return result;
            if (points.Length == 1)
            {
                for (int i = 0; i < n; i++) result[i] = points[0];
                return result;
            }

            float interval = PathLength(points) / (n - 1);
            if (interval <= 1e-6f)
            {
                for (int i = 0; i < n; i++) result[i] = points[0];
                return result;
            }

            float accumulated = 0f;
            int count = 0;
            result[count++] = points[0];
            GesturePoint prev = points[0];

            int idx = 1;
            while (idx < points.Length && count < n)
            {
                GesturePoint cur = points[idx];
                if (cur.StrokeId != prev.StrokeId)
                {
                    prev = cur;
                    idx++;
                    continue;
                }

                float d = Distance(prev, cur);
                if (accumulated + d >= interval && d > 0f)
                {
                    float t = (interval - accumulated) / d;
                    var q = new GesturePoint(prev.X + t * (cur.X - prev.X), prev.Y + t * (cur.Y - prev.Y), cur.StrokeId);
                    result[count++] = q;
                    prev = q;          // q를 새 기준점으로 같은 cur를 다시 검사
                    accumulated = 0f;
                }
                else
                {
                    accumulated += d;
                    prev = cur;
                    idx++;
                }
            }

            // 반올림 오차로 마지막 점이 빠진 경우 채운다
            GesturePoint last = points[points.Length - 1];
            while (count < n) result[count++] = last;
            return result;
        }

        public static float PathLength(GesturePoint[] points)
        {
            float d = 0f;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].StrokeId == points[i - 1].StrokeId)
                    d += Distance(points[i - 1], points[i]);
            }
            return d;
        }

        public static float Distance(in GesturePoint a, in GesturePoint b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
