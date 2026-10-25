// §9 룬 인식기 v2 — $P+ 점 구름 + 획 방향 · 획 수 + 증강 템플릿
//   v1($P+ 32점 · 템플릿 4개)은 "선 여러 개" 룬끼리(S07 가로줄 ↔ H02 별표 ↔ S09 #)를 자주 헷갈렸다.
//   v2 점수 (작을수록 비슷함):
//     ① $P+ 점 구름 거리 (64점, 점당 평균)      × 1.00 — 전체 모양
//     ② 획 방향 히스토그램 (0~180°, 8칸, L1)     × 0.10 — 가로줄만 / 4방향 별표 / 원(고른 분포)
//     ③ 획 수 차이 비율                          × 0.06 — 이어 그리기 · 끊어 그리기를 허용하도록 약하게
//   그리고 기본 도형마다 사람 손처럼 흔든 변형 20개를 템플릿으로 더 넣는다 (RuneAugmenter).
//   오프라인 벤치마크 (사람 손 노이즈 궤적, 19종 전부 후보): 정답률 v1 95% → v2 99.4%.
//   획 순서 · 방향과 무관하다. 회전은 보정하지 않는다 (룬의 방향도 모양의 일부, ∧ ≠ V).

using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Gesture
{
    /// <summary>템플릿 · 입력 궤적 한 개의 특징 묶음 (한 번 계산해 재사용)</summary>
    public sealed class RuneFeatures
    {
        public const int CloudPoints = 64;
        public const int OrientBins = 8;

        public int SpellId;
        public string Name;
        public GesturePoint[] Cloud;
        public readonly float[] Orient = new float[OrientBins];
        public int StrokeCount;

        public static RuneFeatures Compute(int spellId, string name, IReadOnlyList<Vector2[]> strokes)
        {
            var f = new RuneFeatures { SpellId = spellId, Name = name };

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int total = 0;
            for (int s = 0; s < strokes.Count; s++)
            {
                var st = strokes[s];
                if (st == null) continue;
                for (int i = 0; i < st.Length; i++)
                {
                    minX = Mathf.Min(minX, st[i].x); maxX = Mathf.Max(maxX, st[i].x);
                    minY = Mathf.Min(minY, st[i].y); maxY = Mathf.Max(maxY, st[i].y);
                }
                total += st.Length;
            }
            float size = Mathf.Max(maxX - minX, maxY - minY);
            if (total == 0 || size < 1e-6f) size = 1f;
            float tiny = size * 0.04f; // 이보다 짧은 획 = 손 떨림 · 클릭 → 버린다

            var pts = new List<GesturePoint>(total);
            int strokeId = 0;
            for (int pass = 0; pass < 2 && pts.Count < 2; pass++)
            {
                pts.Clear();
                strokeId = 0;
                for (int s = 0; s < strokes.Count; s++)
                {
                    var st = strokes[s];
                    if (st == null || st.Length == 0) continue;
                    if (pass == 0 && StrokeLength(st) < tiny) continue; // 두 번째 시도는 짧은 획도 쓴다
                    for (int i = 0; i < st.Length; i++) pts.Add(new GesturePoint(st[i].x, st[i].y, strokeId));
                    strokeId++;
                }
            }
            f.StrokeCount = Mathf.Max(1, strokeId);

            var resampled = GestureResampler.Resample(pts.ToArray(), CloudPoints);
            ComputeOrientation(resampled, f.Orient);
            GestureNormalizer.Normalize(resampled);
            f.Cloud = resampled;
            return f;
        }

        private static float StrokeLength(Vector2[] s)
        {
            float d = 0f;
            for (int i = 1; i < s.Length; i++) d += Vector2.Distance(s[i - 1], s[i]);
            return d;
        }

        /// <summary>획 방향(방향 무시, 0~180°)을 길이 가중으로 8칸에 나눠 담는다 — 이웃 칸에 선형 분배</summary>
        private static void ComputeOrientation(GesturePoint[] p, float[] bins)
        {
            float sum = 0f;
            for (int i = 1; i < p.Length; i++)
            {
                if (p[i].StrokeId != p[i - 1].StrokeId) continue;
                float dx = p[i].X - p[i - 1].X, dy = p[i].Y - p[i - 1].Y;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6f) continue;
                float a = Mathf.Atan2(dy, dx);
                if (a < 0f) a += Mathf.PI;
                float pos = a / Mathf.PI * OrientBins; // 0 ~ 8
                int b0 = Mathf.FloorToInt(pos) % OrientBins;
                float t = pos - Mathf.Floor(pos);
                bins[b0] += len * (1f - t);
                bins[(b0 + 1) % OrientBins] += len * t;
                sum += len;
            }
            if (sum > 0f) for (int i = 0; i < bins.Length; i++) bins[i] /= sum;
        }
    }

    public sealed class RuneRecognizer
    {
        public const float WeightCloud = 1.0f;
        public const float WeightOrient = 0.10f;
        public const float WeightStrokes = 0.06f;

        /// <summary>이 점수 이하일 때만 수락 (GestureValidationSettings 기본값)</summary>
        public const float DefaultAcceptScore = 0.17f;
        /// <summary>2순위(다른 스킬) 점수 / 1순위 점수 ≥ 이 값</summary>
        public const float DefaultMinRatio = 1.08f;

        private readonly List<RuneFeatures> _templates = new List<RuneFeatures>(512);
        private bool[] _matched = new bool[RuneFeatures.CloudPoints];

        public int TemplateCount => _templates.Count;

        public void Clear() => _templates.Clear();

        public void AddTemplates(IReadOnlyList<RuneFeatures> features)
        {
            for (int i = 0; i < features.Count; i++) if (features[i] != null) _templates.Add(features[i]);
        }

        /// <summary>
        /// 한 룬의 샘플들 → 템플릿 특징 목록. 기본 도형("base")은 변형 20개, 녹화 샘플은 변형 3개를 덧붙인다.
        /// RuneTemplate.GetFeatures와 벤치마크가 같은 규칙을 쓴다.
        /// </summary>
        public static List<RuneFeatures> BuildTemplateFeatures(int spellId, string runeName, IReadOnlyList<(string label, Vector2[][] strokes)> samples)
        {
            var list = new List<RuneFeatures>(samples.Count + RuneAugmenter.DefaultVariants + 8);
            for (int i = 0; i < samples.Count; i++)
            {
                var (label, strokes) = samples[i];
                if (strokes == null || strokes.Length == 0) continue;
                list.Add(RuneFeatures.Compute(spellId, runeName + "/" + label, strokes));
                int variants = label == "base" ? RuneAugmenter.DefaultVariants
                             : label.StartsWith("recorded") ? 3
                             : label.StartsWith("style") ? 12 // 다른 획 구성은 변형을 넉넉히 (사람마다 이어 그리는 모양이 다르다)
                             : 0; // wide · tall · italic은 이미 변형
                int seed = spellId * 7919 + i * 104729 + 17;
                var aug = RuneAugmenter.Variants(strokes, variants, seed);
                for (int k = 0; k < aug.Count; k++) list.Add(RuneFeatures.Compute(spellId, runeName + "/" + label + "~" + k, aug[k]));
            }
            // 같은 룬을 다르게 그리는 방식 (획 구성) — 저장된 템플릿 에셋에 없어도 항상 포함
            var extra = SpellboundVR.Gesture.RuneDesigns.ExtraStyles(spellId);
            for (int e = 0; e < extra.Count; e++)
            {
                var (label, strokes) = extra[e];
                list.Add(RuneFeatures.Compute(spellId, runeName + "/" + label, strokes));
                var aug = RuneAugmenter.Variants(strokes, 12, spellId * 7919 + (e + 1) * 15485863 + 29);
                for (int k = 0; k < aug.Count; k++) list.Add(RuneFeatures.Compute(spellId, runeName + "/" + label + "~" + k, aug[k]));
            }
            return list;
        }

        /// <param name="candidates">null이면 모든 템플릿</param>
        public GestureMatch Recognize(IReadOnlyList<Vector2[]> strokes, ICollection<int> candidates = null)
        {
            return Recognize(RuneFeatures.Compute(0, "candidate", strokes), candidates);
        }

        public GestureMatch Recognize(RuneFeatures c, ICollection<int> candidates = null)
        {
            var result = new GestureMatch { Distance = float.MaxValue, SecondDistance = float.MaxValue };
            for (int i = 0; i < _templates.Count; i++)
            {
                var t = _templates[i];
                if (candidates != null && !candidates.Contains(t.SpellId)) continue;
                float d = Score(c, t);
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

        public float Score(RuneFeatures a, RuneFeatures b)
        {
            float cloud = Mathf.Min(CloudDistance(a.Cloud, b.Cloud), CloudDistance(b.Cloud, a.Cloud)) / RuneFeatures.CloudPoints;
            float orient = 0f;
            for (int i = 0; i < RuneFeatures.OrientBins; i++) orient += Mathf.Abs(a.Orient[i] - b.Orient[i]);
            float strokes = Mathf.Abs(a.StrokeCount - b.StrokeCount) / (float)Mathf.Max(a.StrokeCount, b.StrokeCount);
            return WeightCloud * cloud + WeightOrient * orient + WeightStrokes * strokes;
        }

        /// <summary>$P+ 탐욕 매칭 거리 (점 거리에 회전각 차이 포함)</summary>
        private float CloudDistance(GesturePoint[] p1, GesturePoint[] p2)
        {
            if (_matched.Length < p2.Length) _matched = new bool[p2.Length];
            for (int k = 0; k < p2.Length; k++) _matched[k] = false;
            float sum = 0f;
            for (int i = 0; i < p1.Length; i++)
            {
                int index = -1;
                float min = float.MaxValue;
                for (int j = 0; j < p2.Length; j++)
                {
                    float dx = p1[i].X - p2[j].X, dy = p1[i].Y - p2[j].Y, da = p1[i].Angle - p2[j].Angle;
                    float d = dx * dx + dy * dy + da * da;
                    if (d < min) { min = d; index = j; }
                }
                if (index >= 0) _matched[index] = true;
                sum += Mathf.Sqrt(min);
            }
            for (int j = 0; j < p2.Length; j++)
            {
                if (_matched[j]) continue;
                float min = float.MaxValue;
                for (int i = 0; i < p1.Length; i++)
                {
                    float dx = p1[i].X - p2[j].X, dy = p1[i].Y - p2[j].Y, da = p1[i].Angle - p2[j].Angle;
                    float d = dx * dx + dy * dy + da * da;
                    if (d < min) min = d;
                }
                sum += Mathf.Sqrt(min);
            }
            return sum;
        }
    }
}
