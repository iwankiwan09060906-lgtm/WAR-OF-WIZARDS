// PC 서버 창 관전 카메라 — 판정과 무관한 보기 전용.
//   대기 · 카운트다운: Home 플레이어 뒤에서 내려다보기 (봇 수동 조종 키 Q/W/E 가 Home 기준 좌/중/우).
//   경기 시작(Playing): 전장 옆에서 비스듬히 내려다보는 사이드뷰로 부드럽게 전환 — 왼쪽 Home(파랑) · 오른쪽 Away(빨강),
//     위에서 약 42° 내려다보며 전장 길이 전체(넥서스 ~ 넥서스)가 화면 가로에 들어오도록 거리를 맞춘다.
//   키: V = 사이드뷰 → 탑뷰 → Home 시점 순환, 마우스 휠 = 확대 · 축소, B = 반대편 옆으로.

using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Network;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Server
{
    public sealed class ServerSpectatorCamera : MonoBehaviour
    {
        public enum View { Home, Side, Top }

        [Tooltip("사이드뷰에서 내려다보는 각도 (0 = 수평, 90 = 바로 위)")]
        [Range(10f, 80f)] public float sidePitch = 42f;
        [Tooltip("탑뷰 각도")]
        [Range(60f, 89f)] public float topPitch = 80f;
        [Tooltip("전장 양 끝 여유 (m)")]
        public float margin = 3f;
        [Tooltip("시점 전환에 걸리는 시간 (초)")]
        public float blendSeconds = 1.2f;
        [Tooltip("관전 카메라 시야각 (헤드셋이 없는 PC 서버에서만 적용)")]
        public float fieldOfView = 50f;

        private ArenaGeometry _g;
        private Transform _rig, _head;
        private Camera _cam;
        private NetworkGameEventPublisher _events;
        private View _view = View.Home;
        private bool _otherSide;
        private float _zoom = 1f;
        private Vector3 _fromPos, _toPos;
        private Quaternion _fromRot, _toRot;
        private float _blend = 1f;
        private MatchPhase _phase = MatchPhase.WaitingForPlayers;
        private GUIStyle _style;

        public View Current => _view;

        public void Initialize(ArenaGeometry geometry, Transform rig, Transform head, NetworkGameEventPublisher events)
        {
            _g = geometry;
            _rig = rig != null ? rig : head;
            _head = head != null ? head : rig;
            _cam = _head != null ? _head.GetComponent<Camera>() : null;
            if (_cam != null && !UnityEngine.XR.XRSettings.isDeviceActive) _cam.fieldOfView = fieldOfView;
            _events = events;
            if (_events != null) _events.OnPhaseInfo += HandlePhase;
            SetView(View.Home, instant: true);
        }

        private void OnDestroy()
        {
            if (_events != null) _events.OnPhaseInfo -= HandlePhase;
        }

        private void HandlePhase(MatchPhase phase, float timeLeft)
        {
            var prev = _phase;
            _phase = phase;
            // 경기가 시작되는 순간 사이드뷰로 (중간에 V로 바꿨다면 다음 경기 시작 때 다시 사이드뷰)
            if (phase == MatchPhase.Playing && prev != MatchPhase.Playing) SetView(View.Side, instant: false);
            else if ((phase == MatchPhase.WaitingForPlayers || phase == MatchPhase.Countdown) && prev == MatchPhase.Ended) SetView(View.Home, instant: false);
        }

        public void SetView(View view, bool instant)
        {
            if (_g == null || _rig == null) return;
            _view = view;
            Target(view, out _toPos, out _toRot);
            _fromPos = _head.position;
            _fromRot = _head.rotation;
            _blend = instant || blendSeconds <= 0f ? 1f : 0f;
            if (instant) Apply(_toPos, _toRot);
        }

        private void Target(View view, out Vector3 pos, out Quaternion rot)
        {
            Vector3 center = _g.ToWorld(0f, _g.FieldLength * 0.5f, 0f);
            switch (view)
            {
                case View.Side:
                case View.Top:
                {
                    float pitch = (view == View.Side ? sidePitch : topPitch) * Mathf.Deg2Rad;
                    float halfLen = _g.FieldLength * 0.5f + margin;
                    float vfov = (_cam != null ? _cam.fieldOfView : fieldOfView) * Mathf.Deg2Rad;
                    float aspect = _cam != null && _cam.aspect > 0.1f ? _cam.aspect : 16f / 9f;
                    float tanH = Mathf.Tan(vfov * 0.5f) * aspect;
                    float dist = halfLen / Mathf.Max(0.1f, tanH) * _zoom;
                    // 옆(Right 축)에서 바라보면 화면 오른쪽 = 전장 Forward → 왼쪽 Home, 오른쪽 Away. B 키는 반대편(Away가 왼쪽)
                    Vector3 side = _otherSide ? -_g.Right : _g.Right;
                    pos = center + (side * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * dist;
                    // 바로 위에 가까워도 화면 위쪽이 상대편 레인 방향이 되도록 up 힌트를 -side 로
                    rot = Quaternion.LookRotation(center - pos, view == View.Top ? -side : Vector3.up);
                    break;
                }
                default:
                {
                    // 기존 서버 시점: Home 중앙 발판 뒤 4m · 높이 6m 에서 전장을 내려다본다
                    pos = _g.ToWorld(_g.PlayerPosition(Team.Home, 1), 6f) - _g.Forward * 4f * _zoom;
                    rot = Quaternion.LookRotation(_g.Forward * 2f - Vector3.up, Vector3.up);
                    break;
                }
            }
        }

        /// <summary>머리(카메라)가 pos · rot 에 오도록 리그를 옮긴다 (OVRCameraRig 처럼 카메라가 자식이어도)</summary>
        private void Apply(Vector3 pos, Quaternion rot)
        {
            if (_head == _rig)
            {
                _rig.SetPositionAndRotation(pos, rot);
                return;
            }
            Quaternion headLocal = Quaternion.Inverse(_rig.rotation) * _head.rotation;
            _rig.rotation = rot * Quaternion.Inverse(headLocal);
            _rig.position += pos - _head.position;
        }

        private void LateUpdate()
        {
            if (_g == null || _rig == null) return;

            if (KeyInput.Down(Key.V))
                SetView(_view == View.Side ? View.Top : _view == View.Top ? View.Home : View.Side, instant: false);
            if (KeyInput.Down(Key.B) && _view != View.Home)
            {
                _otherSide = !_otherSide;
                SetView(_view, instant: false);
            }
            float scroll = KeyInput.MouseScroll();
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _zoom = Mathf.Clamp(_zoom * (scroll > 0f ? 0.9f : 1.1f), 0.4f, 2.5f);
                Target(_view, out _toPos, out _toRot);
                if (_blend >= 1f) Apply(_toPos, _toRot);
            }

            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + Time.unscaledDeltaTime / blendSeconds);
                float t = Mathf.SmoothStep(0f, 1f, _blend);
                Apply(Vector3.Lerp(_fromPos, _toPos, t), Quaternion.Slerp(_fromRot, _toRot, t));
            }
        }

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                _style.normal.textColor = new Color(0.85f, 0.82f, 1f, 0.9f);
            }
            string name = _view == View.Side ? "사이드뷰" : _view == View.Top ? "탑뷰" : "Home 시점";
            GUI.Label(new Rect(12, Screen.height - 28, 600, 24), $"카메라: {name}   (V 전환 · B 반대편 · 휠 확대)", _style);
        }
    }
}
