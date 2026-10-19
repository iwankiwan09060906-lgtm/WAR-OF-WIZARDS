using System;
using UnityEngine;

namespace WarOfWizards.Gesture
{
    public static class GestureNormalizer
    {
        public const int TARGET_POINTS = 64;

        public static Vector3[] NormalizeStroke(Vector3[] rawPoints, int targetPoints = TARGET_POINTS)
        {
            if (rawPoints == null || rawPoints.Length < 2)
            {
                Vector3[] fallback = new Vector3[targetPoints];
                Vector3 basePt = rawPoints != null && rawPoints.Length > 0 ? rawPoints[0] : Vector3.zero;
                for (int i = 0; i < targetPoints; i++) fallback[i] = basePt;
                return fallback;
            }

            // 1. Calculate cumulative arc length
            float[] cumDists = new float[rawPoints.Length];
            cumDists[0] = 0f;
            for (int i = 1; i < rawPoints.Length; i++)
            {
                cumDists[i] = cumDists[i - 1] + Vector3.Distance(rawPoints[i - 1], rawPoints[i]);
            }
            float totalLength = cumDists[cumDists.Length - 1];

            // 2. Resample equidistant points along trajectory
            Vector3[] resampled = new Vector3[targetPoints];
            resampled[0] = rawPoints[0];
            resampled[targetPoints - 1] = rawPoints[rawPoints.Length - 1];

            float step = totalLength / (targetPoints - 1);
            int currentSegment = 0;

            for (int i = 1; i < targetPoints - 1; i++)
            {
                float targetDist = i * step;
                while (currentSegment < rawPoints.Length - 2 && cumDists[currentSegment + 1] < targetDist)
                {
                    currentSegment++;
                }

                float segStartDist = cumDists[currentSegment];
                float segEndDist = cumDists[currentSegment + 1];
                float segLength = segEndDist - segStartDist;

                float t = segLength > 1e-5f ? (targetDist - segStartDist) / segLength : 0f;
                resampled[i] = Vector3.Lerp(rawPoints[currentSegment], rawPoints[currentSegment + 1], t);
            }

            // 3. Center at origin (Centroid translation)
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < targetPoints; i++)
            {
                centroid += resampled[i];
            }
            centroid /= targetPoints;

            for (int i = 0; i < targetPoints; i++)
            {
                resampled[i] -= centroid;
            }

            // 4. Uniform scaling to [-1, 1] range preserving 3D proportions
            float maxExtent = 0f;
            for (int i = 0; i < targetPoints; i++)
            {
                maxExtent = Mathf.Max(maxExtent, Mathf.Abs(resampled[i].x), Mathf.Abs(resampled[i].y), Mathf.Abs(resampled[i].z));
            }

            if (maxExtent > 1e-5f)
            {
                float invMax = 1.0f / maxExtent;
                for (int i = 0; i < targetPoints; i++)
                {
                    resampled[i] *= invMax;
                }
            }

            return resampled;
        }

        /// <summary>
        /// (64, 3) 순서 [x0,y0,z0, x1,y1,z1, ...] — AI/Training/train.py 의 LSTM 입력 (배치, 점, xyz) 과 같은 모양
        /// </summary>
        public static float[] ToSequenceTensorArray(Vector3[] normalizedPoints)
        {
            int count = normalizedPoints.Length;
            float[] tensorData = new float[3 * count];
            for (int i = 0; i < count; i++)
            {
                tensorData[i * 3] = normalizedPoints[i].x;
                tensorData[i * 3 + 1] = normalizedPoints[i].y;
                tensorData[i * 3 + 2] = normalizedPoints[i].z;
            }
            return tensorData;
        }

        public static float[] ToFlatTensorArray(Vector3[] normalizedPoints)
        {
            // Returns (3, 64) format matching PyTorch [Channels (x,y,z), SequenceLength (64)]
            int count = normalizedPoints.Length;
            float[] tensorData = new float[3 * count];

            for (int i = 0; i < count; i++)
            {
                tensorData[0 * count + i] = normalizedPoints[i].x; // X channel
                tensorData[1 * count + i] = normalizedPoints[i].y; // Y channel
                tensorData[2 * count + i] = normalizedPoints[i].z; // Z channel
            }
            return tensorData;
        }
    }
}
