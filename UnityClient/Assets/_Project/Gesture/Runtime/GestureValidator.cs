// §9 Confidence 검사 + 1 · 2순위 점수 차 검사 (오발동 방지)
// 기본 임계값은 RuneRecognizer v2 오프라인 벤치마크 기준 (사람 손 노이즈 궤적, 19종 전부 후보):
//   정상 룬 점수 중앙값 0.06~0.10 / p95 0.09~0.15, 무관한 낙서 p5 0.19 · 중앙값 0.23 → 수락 점수 ≤ 0.17
//   1 · 2순위 점수 비율 1.08 이상                                              → 잘못 시전 0%, 낙서 수락 ~1%

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Gesture
{
    [Serializable]
    public sealed class GestureValidationSettings
    {
        [Tooltip("궤적 최소 샘플 수")]
        public int MinRawPoints = 8;
        [Tooltip("궤적 최소 크기 (월드 m 또는 화면 px 단위 — 녹화 좌표계 기준)")]
        public float MinStrokeSize = 0.06f;
        [Tooltip("인식 점수(작을수록 비슷함)가 이 값 이하일 때만 수락 — 정상 룬은 보통 0.06~0.15")]
        public float MaxAcceptScore = RuneRecognizer.DefaultAcceptScore;
        [Tooltip("2순위(다른 스킬) 점수 / 1순위 점수 ≥ 이 값 — 두 룬 사이에서 애매하면 시전하지 않는다")]
        public float MinScoreRatio = RuneRecognizer.DefaultMinRatio;
    }

    public enum GestureRejectReason
    {
        None,
        TooFewPoints,
        TooSmall,
        NoCandidates,
        LowConfidence,
        Ambiguous,
    }

    public static class GestureValidator
    {
        public static GestureRejectReason ValidateStroke(Vector2[] raw, GestureValidationSettings s)
        {
            return ValidateStrokes(new[] { raw }, s);
        }

        /// <summary>여러 획 룬: 점 수는 전체 합, 크기는 모든 획을 감싸는 상자로 검사</summary>
        public static GestureRejectReason ValidateStrokes(IReadOnlyList<Vector2[]> strokes, GestureValidationSettings s)
        {
            int total = 0;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int k = 0; strokes != null && k < strokes.Count; k++)
            {
                var raw = strokes[k];
                if (raw == null) continue;
                total += raw.Length;
                for (int i = 0; i < raw.Length; i++)
                {
                    minX = Mathf.Min(minX, raw[i].x);
                    maxX = Mathf.Max(maxX, raw[i].x);
                    minY = Mathf.Min(minY, raw[i].y);
                    maxY = Mathf.Max(maxY, raw[i].y);
                }
            }
            if (total < s.MinRawPoints) return GestureRejectReason.TooFewPoints;
            if (Mathf.Max(maxX - minX, maxY - minY) < s.MinStrokeSize) return GestureRejectReason.TooSmall;
            return GestureRejectReason.None;
        }

        public static GestureRejectReason ValidateMatch(in GestureMatch match, GestureValidationSettings s)
        {
            if (!match.HasMatch) return GestureRejectReason.NoCandidates;
            if (match.Distance > s.MaxAcceptScore) return GestureRejectReason.LowConfidence;
            if (match.Ratio < s.MinScoreRatio) return GestureRejectReason.Ambiguous;
            return GestureRejectReason.None;
        }
    }
}
