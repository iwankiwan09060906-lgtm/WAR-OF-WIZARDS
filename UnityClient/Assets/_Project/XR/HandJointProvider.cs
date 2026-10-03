// Meta XR 핸드트래킹 관절 제공자 (OVRHand + OVRSkeleton 래퍼)
//   · OVR 스켈레톤(Hand_*)과 OpenXR 스켈레톤(XRHand_*) 두 버전을 모두 지원한다.
//   · 관절 Transform은 스켈레톤 초기화 시 한 번만 캐시한다 (매 프레임 할당 0, §33).
//   · 트래킹 손실은 짧은 유예(LostGraceSeconds) 후에만 보고해 깜빡임으로 인한 오취소를 줄인다.
//   · 씬에 OVRHand가 없으면 손 앵커 아래에 런타임으로 생성한다 (에디터 셋업 도구가 기본 경로).

using System.Reflection;
using UnityEngine;

namespace SpellboundVR.XR
{
    public enum HandJoint
    {
        Wrist = 0,
        Palm,
        ThumbTip,
        IndexProximal,
        IndexIntermediate,
        IndexTip,
        MiddleProximal,
        MiddleTip,
        RingProximal,
        RingTip,
        LittleProximal,
        LittleTip,
        Count,
    }

    public sealed class HandJointProvider : MonoBehaviour
    {
        [Tooltip("true = 오른손")]
        public bool isRightHand = true;
        public OVRHand hand;
        public OVRSkeleton skeleton;
        [Tooltip("트래킹 손실을 보고하기 전 유예 시간 (초)")]
        public float lostGraceSeconds = 0.2f;

        private readonly Transform[] _joints = new Transform[(int)HandJoint.Count];
        private int _cachedBoneCount = -1;
        private float _lastTrackedTime = -999f;
        private bool _rawTracked;

        /// <summary>유예 시간을 적용한 안정 트래킹 상태</summary>
        public bool IsTracked => _rawTracked || Time.time - _lastTrackedTime <= lostGraceSeconds;

        /// <summary>이번 프레임 실제 데이터 유효 여부</summary>
        public bool HasFreshData => _rawTracked;

        public float HandScale => hand != null && hand.HandScale > 0.1f ? hand.HandScale : 1f;

        private void Update()
        {
            RefreshCache();
            _rawTracked = hand != null && hand.IsTracked && hand.IsDataValid &&
                          skeleton != null && skeleton.IsInitialized && skeleton.IsDataValid && _cachedBoneCount > 0;
            if (_rawTracked) _lastTrackedTime = Time.time;
        }

        public bool TryGetJoint(HandJoint joint, out Vector3 position)
        {
            var t = _joints[(int)joint];
            if (t == null || !_rawTracked)
            {
                position = default;
                return false;
            }
            position = t.position;
            return true;
        }

        public bool TryGetJointPose(HandJoint joint, out Pose pose)
        {
            var t = _joints[(int)joint];
            if (t == null || !_rawTracked)
            {
                pose = default;
                return false;
            }
            pose = new Pose(t.position, t.rotation);
            return true;
        }

        /// <summary>엄지 · 검지 끝 중간점 (핀치 지점, 룬 그리기 펜 끝)</summary>
        public bool TryGetPinchPoint(out Vector3 point)
        {
            if (TryGetJoint(HandJoint.ThumbTip, out var a) && TryGetJoint(HandJoint.IndexTip, out var b))
            {
                point = (a + b) * 0.5f;
                return true;
            }
            point = default;
            return false;
        }

        /// <summary>OVR 시스템 핀치 강도 (0~1). 없으면 -1</summary>
        public float GetIndexPinchStrength()
        {
            if (hand == null || !_rawTracked) return -1f;
            return hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        }

        public bool TryGetSystemPointerPose(out Pose pose)
        {
            if (hand != null && _rawTracked && hand.IsPointerPoseValid && hand.PointerPose != null)
            {
                pose = new Pose(hand.PointerPose.position, hand.PointerPose.rotation);
                return true;
            }
            pose = default;
            return false;
        }

        private void RefreshCache()
        {
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            {
                _cachedBoneCount = -1;
                return;
            }
            int count = skeleton.Bones.Count;
            if (count == _cachedBoneCount) return;
            _cachedBoneCount = count;

            for (int i = 0; i < _joints.Length; i++) _joints[i] = null;
            var type = skeleton.GetSkeletonType();
            bool openXr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
            var bones = skeleton.Bones;
            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null || bone.Transform == null) continue;
                int slot = openXr ? MapOpenXrBone(bone.Id) : MapOvrBone(bone.Id);
                if (slot >= 0) _joints[slot] = bone.Transform;
            }
            // OVR 스켈레톤에는 Palm 관절이 없다 → 중지 근위 관절로 대체
            if (_joints[(int)HandJoint.Palm] == null) _joints[(int)HandJoint.Palm] = _joints[(int)HandJoint.MiddleProximal];
        }

        private static int MapOpenXrBone(OVRSkeleton.BoneId id)
        {
            switch (id)
            {
                case OVRSkeleton.BoneId.XRHand_Wrist: return (int)HandJoint.Wrist;
                case OVRSkeleton.BoneId.XRHand_Palm: return (int)HandJoint.Palm;
                case OVRSkeleton.BoneId.XRHand_ThumbTip: return (int)HandJoint.ThumbTip;
                case OVRSkeleton.BoneId.XRHand_IndexProximal: return (int)HandJoint.IndexProximal;
                case OVRSkeleton.BoneId.XRHand_IndexIntermediate: return (int)HandJoint.IndexIntermediate;
                case OVRSkeleton.BoneId.XRHand_IndexTip: return (int)HandJoint.IndexTip;
                case OVRSkeleton.BoneId.XRHand_MiddleProximal: return (int)HandJoint.MiddleProximal;
                case OVRSkeleton.BoneId.XRHand_MiddleTip: return (int)HandJoint.MiddleTip;
                case OVRSkeleton.BoneId.XRHand_RingProximal: return (int)HandJoint.RingProximal;
                case OVRSkeleton.BoneId.XRHand_RingTip: return (int)HandJoint.RingTip;
                case OVRSkeleton.BoneId.XRHand_LittleProximal: return (int)HandJoint.LittleProximal;
                case OVRSkeleton.BoneId.XRHand_LittleTip: return (int)HandJoint.LittleTip;
                default: return -1;
            }
        }

        /// <summary>OVR(레거시) 스켈레톤 — BoneId 숫자값이 OpenXR 값과 겹치므로 스켈레톤 타입으로 분기한다</summary>
        private static int MapOvrBone(OVRSkeleton.BoneId id)
        {
            int v = (int)id;
            if (v == (int)OVRPlugin.BoneId.Hand_WristRoot) return (int)HandJoint.Wrist;
            if (v == (int)OVRPlugin.BoneId.Hand_ThumbTip) return (int)HandJoint.ThumbTip;
            if (v == (int)OVRPlugin.BoneId.Hand_Index1) return (int)HandJoint.IndexProximal;
            if (v == (int)OVRPlugin.BoneId.Hand_Index2) return (int)HandJoint.IndexIntermediate;
            if (v == (int)OVRPlugin.BoneId.Hand_IndexTip) return (int)HandJoint.IndexTip;
            if (v == (int)OVRPlugin.BoneId.Hand_Middle1) return (int)HandJoint.MiddleProximal;
            if (v == (int)OVRPlugin.BoneId.Hand_MiddleTip) return (int)HandJoint.MiddleTip;
            if (v == (int)OVRPlugin.BoneId.Hand_Ring1) return (int)HandJoint.RingProximal;
            if (v == (int)OVRPlugin.BoneId.Hand_RingTip) return (int)HandJoint.RingTip;
            if (v == (int)OVRPlugin.BoneId.Hand_Pinky1) return (int)HandJoint.LittleProximal;
            if (v == (int)OVRPlugin.BoneId.Hand_PinkyTip) return (int)HandJoint.LittleTip;
            return -1;
        }

        // ── 런타임 자동 구성 ───────────────────────────────────

        /// <summary>
        /// 손 앵커(LeftHandAnchor / RightHandAnchor) 아래에서 OVRHand · OVRSkeleton을 찾고,
        /// 없으면 생성해 HandJointProvider를 붙여 반환한다.
        /// </summary>
        public static HandJointProvider EnsureForAnchor(Transform anchor, bool isRight)
        {
            if (anchor == null) return null;

            var existing = anchor.GetComponentInChildren<HandJointProvider>(true);
            if (existing != null) return existing;

            OVRHand ovrHand = anchor.GetComponentInChildren<OVRHand>(true);
            OVRSkeleton ovrSkeleton = ovrHand != null ? ovrHand.GetComponent<OVRSkeleton>() : null;

            if (ovrHand == null)
            {
                var go = new GameObject(isRight ? "OVRHand_Right (Runtime)" : "OVRHand_Left (Runtime)");
                go.SetActive(false);
                go.transform.SetParent(anchor, false);
                ovrHand = go.AddComponent<OVRHand>();
                SetPrivateField(ovrHand, "HandType", isRight ? OVRHand.Hand.HandRight : OVRHand.Hand.HandLeft);
                ovrSkeleton = go.AddComponent<OVRSkeleton>();
                ovrHand.OnValidate(); // 스켈레톤 타입을 HandType · 전역 스켈레톤 버전에 맞춘다
                go.SetActive(true);
                Debug.Log($"[HandJointProvider] {(isRight ? "오른손" : "왼손")} OVRHand/OVRSkeleton 런타임 생성 (셋업 도구로 프리팹을 넣는 것을 권장)");
            }
            else if (ovrSkeleton == null)
            {
                ovrSkeleton = ovrHand.gameObject.AddComponent<OVRSkeleton>();
                ovrHand.OnValidate();
            }

            var provider = ovrHand.gameObject.AddComponent<HandJointProvider>();
            provider.isRightHand = isRight;
            provider.hand = ovrHand;
            provider.skeleton = ovrSkeleton;
            return provider;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(target, value);
            else Debug.LogWarning($"[HandJointProvider] {target.GetType().Name}.{fieldName} 필드를 찾지 못함 — SDK 버전 확인 필요");
        }
    }
}
