// §6.2 엄지 + 검지 핀치 판정 (오른손 드로잉 시작/종료)
//   · 1순위: OVR 시스템 핀치 강도, 2순위: 엄지 · 검지 끝 거리 (손 크기 보정)
//   · 시작/종료 임계값을 다르게 둔 히스테리시스로 떨림에 의한 끊김을 막는다.

using UnityEngine;

namespace SpellboundVR.XR
{
    public sealed class PinchDetector
    {
        public float StartStrength = 0.85f;
        public float EndStrength = 0.45f;
        public float StartDistance = 0.02f;
        public float EndDistance = 0.04f;

        public bool IsPinching { get; private set; }

        /// <summary>이번 프레임 상태 변화: +1 시작, -1 종료, 0 없음</summary>
        public int Update(HandJointProvider hand)
        {
            if (hand == null || !hand.HasFreshData)
            {
                if (IsPinching && (hand == null || !hand.IsTracked))
                {
                    IsPinching = false;
                    return -1;
                }
                return 0;
            }

            bool start, end;
            float strength = hand.GetIndexPinchStrength();
            if (strength >= 0f)
            {
                start = strength >= StartStrength;
                end = strength <= EndStrength;
            }
            else if (hand.TryGetJoint(HandJoint.ThumbTip, out var thumb) && hand.TryGetJoint(HandJoint.IndexTip, out var index))
            {
                float d = Vector3.Distance(thumb, index) / hand.HandScale;
                start = d <= StartDistance;
                end = d >= EndDistance;
            }
            else
            {
                return 0;
            }

            if (!IsPinching && start)
            {
                IsPinching = true;
                return 1;
            }
            if (IsPinching && end)
            {
                IsPinching = false;
                return -1;
            }
            return 0;
        }

        public void Reset() => IsPinching = false;
    }
}
