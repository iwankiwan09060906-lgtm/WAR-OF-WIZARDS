// §6.6 IPlayerInput 구현체 — Quest 핸드트래킹
//   오른손: 핀치 → 룬 드로잉 / 주먹 · 포인팅 · 손바닥 펴기 이벤트 / 검지 조준 레이
//   왼손  : 손바닥 펴고 휘두르기 → 이동
// 매 프레임 동작 경로는 할당 0 (획 종료 시 1회 배열 생성만 허용, §33).

using System;
using SpellboundVR.Gesture;
using SpellboundVR.XR;
using UnityEngine;

namespace SpellboundVR.Input
{
    public sealed class HandTrackingInput : MonoBehaviour, ISpellInputSource
    {
        [Header("참조 (비우면 OVRCameraRig에서 자동 탐색)")]
        public HandJointProvider rightHand;
        public HandJointProvider leftHand;
        public Transform head;

        [Header("조준 레이")]
        [Tooltip("조준 방향 평활 (0 = 없음)")]
        [Range(0f, 0.95f)] public float aimSmoothing = 0.6f;
        [Tooltip("true: 검지 근위→끝 방향 / false: OVR 시스템 포인터 포즈")]
        public bool useIndexFingerRay = true;

        [Header("룬 드로잉")]
        public float minStrokeSizeMeters = 0.06f;

        public event Action OnPinchStarted;
        public event Action<Vector2[]> OnPinchEnded;
        public event Action OnFistDetected;
        public event Action OnPointDetected;
        public event Action OnPalmOpenDetected;
        public event Action<sbyte> OnMoveGesture;
        public event Action<int> OnDirectSlotCast { add { } remove { } }

        private readonly PinchDetector _pinch = new PinchDetector();
        private readonly HandPoseDetector _rightPose = new HandPoseDetector();
        private readonly HandPoseDetector _leftPose = new HandPoseDetector();
        private readonly MoveGestureDetector _move = new MoveGestureDetector();
        private readonly GestureRecorder _recorder = new GestureRecorder();
        private Vector3 _aimDir;
        private Vector3 _aimOrigin;
        private bool _hasAim;

        public bool IsRightHandTracked => rightHand != null && rightHand.IsTracked;
        public bool IsLeftHandTracked => leftHand != null && leftHand.IsTracked;
        public bool IsDrawing => _recorder.IsRecording;
        public Vector3[] DrawPoints => _recorder.WorldPoints;
        public int DrawPointCount => _recorder.Count;
        public float MinStrokeSize => minStrokeSizeMeters;
        public HandPose RightPose => _rightPose.Current;
        public HandPose LeftPose => _leftPose.Current;
        public HandPoseDetector RightPoseDetector => _rightPose;

        public void Configure(HandJointProvider left, HandJointProvider right, Transform headTransform)
        {
            leftHand = left;
            rightHand = right;
            head = headTransform;
        }

        private void Update()
        {
            float now = Time.time;
            UpdateRightHand(now);
            UpdateLeftHand(now);
        }

        private void UpdateRightHand(float now)
        {
            if (rightHand == null) return;

            int pinchChange = _pinch.Update(rightHand);
            if (pinchChange > 0 && rightHand.TryGetPinchPoint(out var start))
            {
                Transform h = head != null ? head : transform;
                _recorder.Begin(start, h.right, h.up);
                OnPinchStarted?.Invoke();
            }
            else if (_pinch.IsPinching && _recorder.IsRecording)
            {
                if (rightHand.TryGetPinchPoint(out var p)) _recorder.AddPoint(p);
            }
            else if (pinchChange < 0 && _recorder.IsRecording)
            {
                var stroke = _recorder.End();
                OnPinchEnded?.Invoke(stroke);
            }

            // 핀치 중에는 자세 이벤트를 내지 않는다 (드로잉과 주먹 혼동 방지)
            if (!_pinch.IsPinching && _rightPose.Update(rightHand, now))
            {
                switch (_rightPose.Current)
                {
                    case HandPose.Fist: OnFistDetected?.Invoke(); break;
                    case HandPose.Point: OnPointDetected?.Invoke(); break;
                    case HandPose.OpenPalm: OnPalmOpenDetected?.Invoke(); break;
                }
            }

            UpdateAim();
        }

        private void UpdateLeftHand(float now)
        {
            if (leftHand == null) return;
            _leftPose.Update(leftHand, now);
            if (!leftHand.TryGetJoint(HandJoint.Palm, out var palm))
            {
                _move.Reset();
                return;
            }
            Vector3 right = head != null ? head.right : Vector3.right;
            sbyte dir = _move.Update(_leftPose.Current == HandPose.OpenPalm, palm, right, now);
            if (dir != 0) OnMoveGesture?.Invoke(dir);
        }

        private void UpdateAim()
        {
            Vector3 origin, dir;
            if (useIndexFingerRay &&
                rightHand.TryGetJoint(HandJoint.IndexProximal, out var prox) &&
                rightHand.TryGetJoint(HandJoint.IndexTip, out var tip) &&
                (tip - prox).sqrMagnitude > 1e-6f)
            {
                origin = tip;
                dir = (tip - prox).normalized;
            }
            else if (rightHand.TryGetSystemPointerPose(out var pose))
            {
                origin = pose.position;
                dir = pose.rotation * Vector3.forward;
            }
            else
            {
                return;
            }

            if (!_hasAim)
            {
                _aimDir = dir;
                _hasAim = true;
            }
            else
            {
                _aimDir = Vector3.Slerp(dir, _aimDir, aimSmoothing).normalized;
            }
            _aimOrigin = origin;
        }

        public bool TryGetAimRay(out Ray ray)
        {
            if (!_hasAim || !IsRightHandTracked)
            {
                ray = default;
                return false;
            }
            ray = new Ray(_aimOrigin, _aimDir);
            return true;
        }

        public bool TryGetRightHandPosition(out Vector3 position)
        {
            if (rightHand != null && rightHand.TryGetJoint(HandJoint.Palm, out position)) return true;
            position = default;
            return false;
        }
    }
}
