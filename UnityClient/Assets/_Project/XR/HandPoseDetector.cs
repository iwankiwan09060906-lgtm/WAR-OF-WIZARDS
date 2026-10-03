// §6.2 / §6.4 손 자세 판정 — 주먹(Fist) · 검지 포인팅(Point) · 손바닥 펴기(OpenPalm)
// 기준: Docs/Input/HandPoseSpec.md
//   손가락 굽힘 = 각도( 손목→근위관절 , 근위관절→손끝 )
//     펴짐: < ExtendedMaxDeg,  굽힘: > CurledMinDeg  (그 사이는 "애매" → 자세 유지)
//   Fist     : 검지 · 중지 · 약지 · 소지 모두 굽힘
//   Point    : 검지 펴짐 + 나머지 셋 굽힘
//   OpenPalm : 네 손가락 모두 펴짐
// 자세는 HoldSeconds 동안 유지되어야 확정된다 (디바운스).

using UnityEngine;

namespace SpellboundVR.XR
{
    public enum HandPose
    {
        None,
        Fist,
        Point,
        OpenPalm,
    }

    public sealed class HandPoseDetector
    {
        public float ExtendedMaxDeg = 55f;
        public float CurledMinDeg = 95f;
        public float HoldSeconds = 0.08f;

        private HandPose _candidate = HandPose.None;
        private float _candidateSince;

        public HandPose Current { get; private set; } = HandPose.None;

        /// <summary>마지막 측정 굽힘 각 (디버그 표시용): 검지, 중지, 약지, 소지</summary>
        public readonly float[] CurlDegrees = new float[4];

        /// <summary>확정 자세가 바뀌면 true</summary>
        public bool Update(HandJointProvider hand, float time)
        {
            HandPose raw = Classify(hand);
            if (raw != _candidate)
            {
                _candidate = raw;
                _candidateSince = time;
            }
            if (_candidate != Current && time - _candidateSince >= HoldSeconds)
            {
                Current = _candidate;
                return true;
            }
            return false;
        }

        public void Reset()
        {
            Current = HandPose.None;
            _candidate = HandPose.None;
        }

        private HandPose Classify(HandJointProvider hand)
        {
            if (hand == null || !hand.HasFreshData) return HandPose.None;
            if (!hand.TryGetJoint(HandJoint.Wrist, out var wrist)) return HandPose.None;

            int index = FingerState(hand, wrist, HandJoint.IndexProximal, HandJoint.IndexTip, 0);
            int middle = FingerState(hand, wrist, HandJoint.MiddleProximal, HandJoint.MiddleTip, 1);
            int ring = FingerState(hand, wrist, HandJoint.RingProximal, HandJoint.RingTip, 2);
            int little = FingerState(hand, wrist, HandJoint.LittleProximal, HandJoint.LittleTip, 3);
            if (index == -2 || middle == -2 || ring == -2 || little == -2) return HandPose.None;

            bool othersCurled = middle == 1 && ring == 1 && little == 1;
            if (index == 1 && othersCurled) return HandPose.Fist;
            if (index == -1 && othersCurled) return HandPose.Point;
            if (index == -1 && middle == -1 && ring == -1 && little == -1) return HandPose.OpenPalm;
            return Current; // 애매한 중간 자세는 직전 자세 유지
        }

        /// <returns>-1 펴짐, 1 굽힘, 0 애매, -2 데이터 없음</returns>
        private int FingerState(HandJointProvider hand, Vector3 wrist, HandJoint proximal, HandJoint tip, int slot)
        {
            if (!hand.TryGetJoint(proximal, out var p) || !hand.TryGetJoint(tip, out var t)) return -2;
            Vector3 a = p - wrist;
            Vector3 b = t - p;
            if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return -2;
            float deg = Vector3.Angle(a, b);
            CurlDegrees[slot] = deg;
            if (deg < ExtendedMaxDeg) return -1;
            if (deg > CurledMinDeg) return 1;
            return 0;
        }
    }
}
