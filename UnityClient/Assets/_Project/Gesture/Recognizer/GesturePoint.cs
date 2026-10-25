// $P+ 점 표현 (Vatavu 2017, "Improving Gesture Recognition Accuracy on Touch Screens
// for Users with Low Vision"). 각 점은 정규화된 좌표 + 정규화된 회전각(turning angle)을 가진다.
// 인식 결과 GestureMatch도 여기 둔다 (RuneRecognizer · GestureValidator · CastStateMachine 공용).

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

    public struct GestureMatch
    {
        public bool HasMatch;
        public int SpellId;
        public string TemplateName;
        /// <summary>RuneRecognizer 점수 (작을수록 비슷함)</summary>
        public float Distance;
        /// <summary>다른 SpellId 중 최선 점수 (없으면 float.MaxValue)</summary>
        public float SecondDistance;
        public int SecondSpellId;

        /// <summary>2순위 / 1순위 점수 비율 (클수록 확실함)</summary>
        public float Ratio => SecondDistance < float.MaxValue ? SecondDistance / Mathf.Max(1e-4f, Distance) : float.MaxValue;
    }
}
