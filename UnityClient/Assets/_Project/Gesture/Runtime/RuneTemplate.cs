// 룬 템플릿 에셋 (Gesture/Templates/Data/Rune_Sxx_Name.asset)
// 한 스킬당 여러 샘플(사람이 그린 궤적)을 가질 수 있다. 좌표계: x = 오른쪽, y = 위.

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
                _clouds.Add(PointCloud.FromStroke(SpellId, RuneName + "/" + s.Label, s.Points));
            }
            return _clouds;
        }

        /// <summary>새 샘플 추가 (에디터 녹화 · 튜닝용)</summary>
        public void AddSample(string label, Vector2[] points)
        {
            Samples.Add(new RuneSample { Label = label, Points = points });
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
