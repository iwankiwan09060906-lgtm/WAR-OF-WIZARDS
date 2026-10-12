// $P+ Point-Cloud 표현 (Vatavu 2017, "Improving Gesture Recognition Accuracy on Touch Screens
// for Users with Low Vision"). 각 점은 정규화된 좌표 + 정규화된 회전각(turning angle)을 가진다.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Gesture
{
    [Serializable]
    public struct GesturePoint
    {
        public float X;
        public float Y;
        public int StrokeId;
        /// <summary>$P+ 정규화 회전각 (0 ~ 1)</summary>
        public float Angle;

        public GesturePoint(float x, float y, int strokeId)
        {
            X = x;
            Y = y;
            StrokeId = strokeId;
            Angle = 0f;
        }

        public Vector2 ToVector2() => new Vector2(X, Y);
    }

    public sealed class PointCloud
    {
        public readonly int SpellId;
        public readonly string Name;
        public readonly GesturePoint[] Points;

        public PointCloud(int spellId, string name, GesturePoint[] normalizedPoints)
        {
            SpellId = spellId;
            Name = name;
            Points = normalizedPoints;
        }

        /// <summary>원시 2D 궤적(단일 획)으로부터 리샘플 · 정규화된 클라우드를 만든다.</summary>
        public static PointCloud FromStroke(int spellId, string name, Vector2[] raw, int numPoints = GestureResampler.DefaultPointCount)
        {
            return FromStrokes(spellId, name, new[] { raw }, numPoints);
        }

        /// <summary>여러 획 궤적 (같은 그리기 평면 좌표) → 클라우드. 획 순서 · 방향은 결과에 거의 영향이 없다 ($P 점 구름 매칭).</summary>
        public static PointCloud FromStrokes(int spellId, string name, IReadOnlyList<Vector2[]> strokes, int numPoints = GestureResampler.DefaultPointCount)
        {
            int total = 0;
            for (int s = 0; s < strokes.Count; s++)
                if (strokes[s] != null) total += strokes[s].Length;
            var pts = new GesturePoint[total];
            int k = 0;
            for (int s = 0; s < strokes.Count; s++)
            {
                var raw = strokes[s];
                if (raw == null) continue;
                for (int i = 0; i < raw.Length; i++) pts[k++] = new GesturePoint(raw[i].x, raw[i].y, s);
            }
            var resampled = GestureResampler.Resample(pts, numPoints);
            GestureNormalizer.Normalize(resampled);
            return new PointCloud(spellId, name, resampled);
        }
    }
}
