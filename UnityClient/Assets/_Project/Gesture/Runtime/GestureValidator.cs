// §9 Confidence 검사 + 1 · 2순위 점수 차 검사 (오발동 방지)
// 기본 임계값은 GestureTools 오프라인 측정값 기준:
//   정상 룬 최선 거리 평균 2~4.5 / 최대 ~8, 무관한 궤적 최소 ~7.5  → 수락 거리 ≤ 7.0
//   1 · 2순위 거리 비율 p5 ≈ 1.12                                → 최소 비율 1.08

using System;
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
        [Tooltip("$P+ 최선 거리가 이 값 이하일 때만 수락")]
        public float MaxAcceptDistance = 7.0f;
        [Tooltip("2순위(다른 스킬) 거리 / 1순위 거리 ≥ 이 값")]
        public float MinSecondBestRatio = 1.08f;
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
            if (raw == null || raw.Length < s.MinRawPoints) return GestureRejectReason.TooFewPoints;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < raw.Length; i++)
            {
                minX = Mathf.Min(minX, raw[i].x);
                maxX = Mathf.Max(maxX, raw[i].x);
                minY = Mathf.Min(minY, raw[i].y);
                maxY = Mathf.Max(maxY, raw[i].y);
            }
            if (Mathf.Max(maxX - minX, maxY - minY) < s.MinStrokeSize) return GestureRejectReason.TooSmall;
            return GestureRejectReason.None;
        }

        public static GestureRejectReason ValidateMatch(in GestureMatch match, GestureValidationSettings s)
        {
            if (!match.HasMatch) return GestureRejectReason.NoCandidates;
            if (match.Distance > s.MaxAcceptDistance) return GestureRejectReason.LowConfidence;
            if (match.SecondDistance < float.MaxValue && match.SecondDistance / Mathf.Max(1e-4f, match.Distance) < s.MinSecondBestRatio)
                return GestureRejectReason.Ambiguous;
            return GestureRejectReason.None;
        }
    }
}
