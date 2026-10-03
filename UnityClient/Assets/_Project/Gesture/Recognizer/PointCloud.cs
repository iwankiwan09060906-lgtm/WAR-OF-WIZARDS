// $P+ Point-Cloud 표현 (Vatavu 2017, "Improving Gesture Recognition Accuracy on Touch Screens
// for Users with Low Vision"). 각 점은 정규화된 좌표 + 정규화된 회전각(turning angle)을 가진다.

using System;
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
            var pts = new GesturePoint[raw.Length];
            for (int i = 0; i < raw.Length; i++) pts[i] = new GesturePoint(raw[i].x, raw[i].y, 0);
            var resampled = GestureResampler.Resample(pts, numPoints);
            GestureNormalizer.Normalize(resampled);
            return new PointCloud(spellId, name, resampled);
        }
    }
}
