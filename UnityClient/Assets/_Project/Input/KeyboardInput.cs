// §6.6 IPlayerInput 구현체 — 에디터 개발 · 테스트용 키보드/마우스 대체 입력
//   헤드셋 없이 개발하거나, 에디터를 두 번째 사람 플레이어로 쓴 PvP · 친선전 테스트에 사용한다.
//
//   1 ~ 8          : 슬롯 번호로 바로 시전 (룬 드로잉 생략, §6.6)
//   마우스 왼쪽 드래그 : 룬 그리기 (= 핀치 유지)
//   F              : 주먹 (= Armed / 즉시 시전 스킬은 시전)
//   G              : 검지 포인팅 (= Aiming 시작)
//   X / 마우스 오른쪽 : 손바닥 펴기 (= 취소)
//   A / D          : 왼쪽 / 오른쪽 이동
//   마우스 위치      : 조준 레이
// 봇 수동 조종 키(§20.3)는 Alt를 누른 채 입력하므로 충돌하지 않는다.

using System;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Input
{
    public sealed class KeyboardInput : MonoBehaviour, ISpellInputSource
    {
        private const int MaxDrawPoints = 512;

        [Tooltip("조준 · 그리기 기준 카메라 (비우면 Camera.main)")]
        public Camera viewCamera;
        [Tooltip("그린 궤적을 카메라 앞 몇 m에 표시할지")]
        public float drawDisplayDistance = 1.2f;
        public float minStrokeSizePixels = 40f;

        public event Action OnPinchStarted;
        public event Action<Vector2[]> OnPinchEnded;
        public event Action OnFistDetected;
        public event Action OnPointDetected;
        public event Action OnPalmOpenDetected;
        public event Action<sbyte> OnMoveGesture;
        public event Action<int> OnDirectSlotCast;

        private readonly Vector2[] _screenPoints = new Vector2[MaxDrawPoints];
        private readonly Vector3[] _worldPoints = new Vector3[MaxDrawPoints];
        private int _count;
        private bool _drawing;

        public bool IsRightHandTracked => true;
        public bool IsLeftHandTracked => true;
        public bool IsDrawing => _drawing;
        public Vector3[] DrawPoints => _worldPoints;
        public int DrawPointCount => _count;
        public float MinStrokeSize => minStrokeSizePixels;

        private Camera Cam => viewCamera != null ? viewCamera : Camera.main;

        private void Update()
        {
            if (KeyInput.AltHeld()) return; // Alt 조합은 봇 수동 조종 전용

            int slot = KeyInput.SlotKeyDown();
            if (slot >= 0) OnDirectSlotCast?.Invoke(slot);

            if (KeyInput.Down(Key.F)) OnFistDetected?.Invoke();
            if (KeyInput.Down(Key.G)) OnPointDetected?.Invoke();
            if (KeyInput.Down(Key.X) || KeyInput.MouseDown(1)) OnPalmOpenDetected?.Invoke();
            if (KeyInput.Down(Key.A)) OnMoveGesture?.Invoke(-1);
            if (KeyInput.Down(Key.D)) OnMoveGesture?.Invoke(1);

            UpdateDrawing();
        }

        private void UpdateDrawing()
        {
            bool held = KeyInput.MouseHeld(0);
            Vector2 mouse = KeyInput.MousePosition();

            if (held && !_drawing)
            {
                _drawing = true;
                _count = 0;
                AddPoint(mouse);
                OnPinchStarted?.Invoke();
            }
            else if (held && _drawing)
            {
                if (_count == 0 || (mouse - _screenPoints[_count - 1]).sqrMagnitude >= 9f) AddPoint(mouse);
            }
            else if (!held && _drawing)
            {
                _drawing = false;
                var stroke = new Vector2[_count];
                Array.Copy(_screenPoints, stroke, _count);
                OnPinchEnded?.Invoke(stroke);
            }
        }

        private void AddPoint(Vector2 screen)
        {
            if (_count >= MaxDrawPoints) return;
            _screenPoints[_count] = screen;
            var cam = Cam;
            _worldPoints[_count] = cam != null
                ? cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, drawDisplayDistance))
                : new Vector3(screen.x * 0.001f, screen.y * 0.001f, drawDisplayDistance);
            _count++;
        }

        // ── 테스트 · 자동화용 시뮬레이션 (Spellbound > Debug 메뉴, 키 입력과 같은 이벤트를 발생) ──

        public void SimulateSlotCast(int slot) => OnDirectSlotCast?.Invoke(slot);

        public void SimulateFist() => OnFistDetected?.Invoke();

        public void SimulatePoint() => OnPointDetected?.Invoke();

        public void SimulatePalmOpen() => OnPalmOpenDetected?.Invoke();

        public void SimulateMove(sbyte direction) => OnMoveGesture?.Invoke(direction);

        /// <summary>화면 좌표(px) 궤적 한 획을 그린 것처럼 핀치 시작 → 종료 이벤트를 낸다.</summary>
        public void SimulateStroke(Vector2[] screenPoints)
        {
            if (screenPoints == null || screenPoints.Length < 2) return;
            _count = 0;
            for (int i = 0; i < screenPoints.Length; i++) AddPoint(screenPoints[i]);
            OnPinchStarted?.Invoke();
            var stroke = new Vector2[_count];
            Array.Copy(_screenPoints, stroke, _count);
            OnPinchEnded?.Invoke(stroke);
        }

        /// <summary>조준 레이를 고정 화면 좌표로 강제 (null이면 마우스 사용)</summary>
        public Vector2? AimScreenOverride { get; set; }

        public bool TryGetAimRay(out Ray ray)
        {
            var cam = Cam;
            if (cam == null)
            {
                ray = default;
                return false;
            }
            Vector2 m = AimScreenOverride ?? KeyInput.MousePosition();
            ray = cam.ScreenPointToRay(new Vector3(m.x, m.y, 0f));
            return true;
        }

        public bool TryGetRightHandPosition(out Vector3 position)
        {
            var cam = Cam;
            if (cam == null)
            {
                position = default;
                return false;
            }
            Transform t = cam.transform;
            position = t.position + t.forward * 0.8f + t.right * 0.3f - t.up * 0.25f;
            return true;
        }
    }
}
