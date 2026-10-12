// 룬 템플릿 에셋 (Gesture/Templates/Data/Rune_Sxx_Name.asset)
// 한 스킬당 여러 샘플(사람이 그린 궤적)을 가질 수 있다. 좌표계: x = 오른쪽, y = 위.
// 여러 획 룬: 모든 획의 점을 Points에 이어 붙이고, 2번째 획부터 시작 인덱스를 StrokeStarts에 넣는다.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Gesture
{
    [Serializable]
    public sealed class RuneSample
    {
        public string Label = "sample";
        public Vector2[] Points = Array.Empty<Vector2>();
        [Tooltip("여러 획 룬: 2번째 획부터 각 획이 시작하는 Points 인덱스 (비우면 한 획)")]
        public int[] StrokeStarts = Array.Empty<int>();

        public int StrokeCount => StrokeStarts == null ? 1 : StrokeStarts.Length + 1;

        /// <summary>Points를 획 단위로 나눈다 (StrokeStarts 기준)</summary>
        public Vector2[][] SplitStrokes()
        {
            if (Points == null) return Array.Empty<Vector2[]>();
            if (StrokeStarts == null || StrokeStarts.Length == 0) return new[] { Points };
            var result = new List<Vector2[]>(StrokeStarts.Length + 1);
            int start = 0;
            for (int i = 0; i <= StrokeStarts.Length; i++)
            {
                int end = i < StrokeStarts.Length ? Mathf.Clamp(StrokeStarts[i], start, Points.Length) : Points.Length;
                if (end > start)
                {
                    var stroke = new Vector2[end - start];
                    Array.Copy(Points, start, stroke, 0, stroke.Length);
                    result.Add(stroke);
                }
                start = end;
            }
            return result.ToArray();
        }
    }

    [CreateAssetMenu(menuName = "Spellbound/Rune Template", fileName = "Rune_Sxx_Name")]
    public sealed class RuneTemplate : ScriptableObject
    {
        public int SpellId;
        public string RuneName = "Rune";
        [Tooltip("HUD · 손 위 표시용 아이콘 (ㅇㅎㅅ 공급, 선택)")]
        public Texture2D Icon;
        public List<RuneSample> Samples = new List<RuneSample>();

        [NonSerialized] private List<PointCloud> _clouds;

        public IReadOnlyList<PointCloud> GetClouds()
        {
            if (_clouds != null && _clouds.Count == CountValidSamples()) return _clouds;
            _clouds = new List<PointCloud>(Samples.Count);
            for (int i = 0; i < Samples.Count; i++)
            {
                var s = Samples[i];
                if (s?.Points == null || s.Points.Length < 2) continue;
                _clouds.Add(PointCloud.FromStrokes(SpellId, RuneName + "/" + s.Label, s.SplitStrokes()));
            }
            return _clouds;
        }

        /// <summary>새 샘플 추가 (에디터 녹화 · 튜닝용)</summary>
        public void AddSample(string label, Vector2[] points)
        {
            Samples.Add(new RuneSample { Label = label, Points = points });
            _clouds = null;
        }

        /// <summary>여러 획 샘플 추가 (획 순서대로)</summary>
        public void AddSample(string label, IReadOnlyList<Vector2[]> strokes)
        {
            var points = new List<Vector2>();
            var starts = new List<int>();
            for (int s = 0; s < strokes.Count; s++)
            {
                if (strokes[s] == null || strokes[s].Length == 0) continue;
                if (points.Count > 0) starts.Add(points.Count);
                points.AddRange(strokes[s]);
            }
            Samples.Add(new RuneSample { Label = label, Points = points.ToArray(), StrokeStarts = starts.ToArray() });
            _clouds = null;
        }

        private int CountValidSamples()
        {
            int n = 0;
            for (int i = 0; i < Samples.Count; i++)
                if (Samples[i]?.Points != null && Samples[i].Points.Length >= 2) n++;
            return n;
        }

        private void OnValidate() => _clouds = null;
    }
}
