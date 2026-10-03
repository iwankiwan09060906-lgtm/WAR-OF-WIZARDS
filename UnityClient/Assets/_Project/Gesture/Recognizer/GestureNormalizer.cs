// §9 $P+ Pipeline — 정규화: 균일 스케일(최대 변 = 1) → 중심 이동(무게중심 원점) → $P+ 회전각 계산
// $P/$P+는 회전 정규화를 하지 않는다 → 룬의 방향(위/아래)이 의미를 가진다.

using UnityEngine;

namespace SpellboundVR.Gesture
{
    public static class GestureNormalizer
    {
        public static void Normalize(GesturePoint[] points)
        {
            Scale(points);
            TranslateToCentroid(points);
            ComputeNormalizedTurningAngles(points);
        }

        public static void Scale(GesturePoint[] points)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < points.Length; i++)
            {
                minX = Mathf.Min(minX, points[i].X);
                minY = Mathf.Min(minY, points[i].Y);
                maxX = Mathf.Max(maxX, points[i].X);
                maxY = Mathf.Max(maxY, points[i].Y);
            }
            float size = Mathf.Max(maxX - minX, maxY - minY);
            if (size < 1e-6f) size = 1f;
            for (int i = 0; i < points.Length; i++)
            {
                points[i].X = (points[i].X - minX) / size;
                points[i].Y = (points[i].Y - minY) / size;
            }
        }

        public static void TranslateToCentroid(GesturePoint[] points)
        {
            float cx = 0f, cy = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                cx += points[i].X;
                cy += points[i].Y;
            }
            cx /= points.Length;
            cy /= points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                points[i].X -= cx;
                points[i].Y -= cy;
            }
        }

        /// <summary>$P+: 각 점에서 진행 방향이 꺾이는 정도 (0 = 직진, 1 = 완전 반대)</summary>
        public static void ComputeNormalizedTurningAngles(GesturePoint[] points)
        {
            int n = points.Length;
            if (n == 0) return;
            points[0].Angle = 0f;
            points[n - 1].Angle = 0f;
            for (int i = 1; i < n - 1; i++)
            {
                float ax = points[i + 1].X - points[i].X;
                float ay = points[i + 1].Y - points[i].Y;
                float bx = points[i].X - points[i - 1].X;
                float by = points[i].Y - points[i - 1].Y;
                float la = Mathf.Sqrt(ax * ax + ay * ay);
                float lb = Mathf.Sqrt(bx * bx + by * by);
                if (la < 1e-6f || lb < 1e-6f)
                {
                    points[i].Angle = 0f;
                    continue;
                }
                float cos = Mathf.Clamp((ax * bx + ay * by) / (la * lb), -1f, 1f);
                points[i].Angle = Mathf.Acos(cos) / Mathf.PI;
            }
        }
    }
}
