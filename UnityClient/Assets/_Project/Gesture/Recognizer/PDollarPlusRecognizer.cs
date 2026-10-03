// §9 $P+ Point-Cloud Recognizer (VR 클라이언트 온디바이스)
//   거리 = min(CloudDistance(C,T), CloudDistance(T,C)), 점 거리에 회전각 차이를 포함한다.
//   후보는 "사용 가능한 슬롯"의 템플릿만 (ReadySlotTemplateSet).
//   1 · 2순위 점수 차 검사는 GestureValidator에서 한다 (서로 다른 스킬 사이에서만 비교).

using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Gesture
{
    public struct GestureMatch
    {
        public bool HasMatch;
        public int SpellId;
        public string TemplateName;
        public float Distance;
        /// <summary>다른 SpellId 중 최선 거리 (없으면 float.MaxValue)</summary>
        public float SecondDistance;
        public int SecondSpellId;

        /// <summary>표시용 점수 0~1 (거리 기반)</summary>
        public float Score => HasMatch ? 1f / (1f + Distance) : 0f;
    }

    public sealed class PDollarPlusRecognizer
    {
        private bool[] _matched = new bool[GestureResampler.DefaultPointCount];

        public GestureMatch Recognize(PointCloud candidate, IReadOnlyList<PointCloud> templates)
        {
            var result = new GestureMatch
            {
                HasMatch = false,
                SpellId = 0,
                Distance = float.MaxValue,
                SecondDistance = float.MaxValue,
                SecondSpellId = 0,
            };
            if (candidate == null || templates == null || templates.Count == 0) return result;

            for (int i = 0; i < templates.Count; i++)
            {
                var t = templates[i];
                if (t == null) continue;
                float d1 = CloudDistance(candidate.Points, t.Points);
                float d2 = CloudDistance(t.Points, candidate.Points);
                float d = Mathf.Min(d1, d2);

                if (d < result.Distance)
                {
                    if (result.HasMatch && result.SpellId != t.SpellId)
                    {
                        result.SecondDistance = result.Distance;
                        result.SecondSpellId = result.SpellId;
                    }
                    result.HasMatch = true;
                    result.Distance = d;
                    result.SpellId = t.SpellId;
                    result.TemplateName = t.Name;
                }
                else if (t.SpellId != result.SpellId && d < result.SecondDistance)
                {
                    result.SecondDistance = d;
                    result.SecondSpellId = t.SpellId;
                }
            }
            return result;
        }

        public float CloudDistance(GesturePoint[] pts1, GesturePoint[] pts2)
        {
            if (_matched.Length < pts2.Length) _matched = new bool[pts2.Length];
            for (int k = 0; k < pts2.Length; k++) _matched[k] = false;

            float sum = 0f;
            for (int i = 0; i < pts1.Length; i++)
            {
                int index = -1;
                float min = float.MaxValue;
                for (int j = 0; j < pts2.Length; j++)
                {
                    float d = DistanceWithAngle(pts1[i], pts2[j]);
                    if (d < min)
                    {
                        min = d;
                        index = j;
                    }
                }
                if (index >= 0) _matched[index] = true;
                sum += min;
            }
            for (int j = 0; j < pts2.Length; j++)
            {
                if (_matched[j]) continue;
                float min = float.MaxValue;
                for (int i = 0; i < pts1.Length; i++)
                {
                    float d = DistanceWithAngle(pts1[i], pts2[j]);
                    if (d < min) min = d;
                }
                sum += min;
            }
            return sum;
        }

        private static float DistanceWithAngle(in GesturePoint a, in GesturePoint b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float da = a.Angle - b.Angle;
            return Mathf.Sqrt(dx * dx + dy * dy + da * da);
        }
    }
}
