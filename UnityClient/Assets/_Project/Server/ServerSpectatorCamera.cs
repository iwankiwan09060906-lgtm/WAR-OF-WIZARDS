// PC 서버 창 관전 카메라 — 판정과 무관한 보기 전용.
//   대기 · 카운트다운: Home 플레이어 뒤에서 내려다보기 (봇 수동 조종 키 Q/W/E 가 Home 기준 좌/중/우).
//   경기 시작(Playing): 전장 옆에서 비스듬히 내려다보는 사이드뷰로 부드럽게 전환 — 왼쪽 Home(파랑) · 오른쪽 Away(빨강),
//     위에서 약 42° 내려다보며 전장 길이 전체(넥서스 ~ 넥서스)가 화면 가로에 들어오도록 거리를 맞춘다.
//   키: V = 사이드뷰 → 탑뷰 → Home 시점 순환, 마우스 휠 = 확대 · 축소, B = 반대편 옆으로.
//   옵저버(전시 시연 BeginObserver): 1 ~ 6번 플레이어 등 뒤 시점을 돌아가며 본다 — 숫자 1 ~ 6 = 그 사람, Tab = 다음,
//     O = 자동 순환 켜기 · 끄기 (observerSeconds 마다 다음 사람), V = 전체 보기. 방 안 입장 알림도 화면에 띄운다.

using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Server
{
    public sealed class ServerSpectatorCamera : MonoBehaviour
    {
        public enum View { Home, Side, Top, Seat }

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
        [Tooltip("옵저버 자동 순환: 한 사람을 보는 시간 (초)")]
        public float observerSeconds = 8f;
        [Tooltip("옵저버 시점: 플레이어 뒤 거리 · 높이 (m) — 날아오는 스킬 · 다른 상대까지 보이게 멀찍이")]
        public float seatBack = 9f;
        public float seatHeight = 7.5f;

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
        private GUIStyle _style, _noticeStyle;
        private bool _observer;
        private bool _autoCycle;
        private Team _seat = Team.Home;
        private float _nextCycle;

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
            if (phase == MatchPhase.Playing && prev != MatchPhase.Playing) SetView(_observer ? View.Seat : View.Side, instant: false);
            else if (!_observer && (phase == MatchPhase.WaitingForPlayers || phase == MatchPhase.Countdown) && prev == MatchPhase.Ended) SetView(View.Home, instant: false);
        }

        /// <summary>전시 시연 옵저버: 1번부터 자동 순환으로 플레이어 시점을 돈다</summary>
        public void BeginObserver()
        {
            _observer = true;
            _autoCycle = true;
            _seat = Team.Home;
            _nextCycle = Time.unscaledTime + observerSeconds;
            SetView(View.Seat, instant: true);
        }

        /// <summary>자리 번호 순(1 ~ 6)으로 다음 · 이전 자리 (경기에 있는 자리만)</summary>
        private void StepSeat(int dir)
        {
            int start = TeamUtil.ClockIndex(_seat);
            for (int i = 1; i <= TeamUtil.TeamCount; i++)
            {
                Team t = TeamUtil.TeamAtSeat(start + dir * i);
                if (!_g.HasTeam(t)) continue;
                WatchSeat(t);
                return;
            }
        }

        private void WatchSeat(Team team)
        {
            _seat = team;
            _nextCycle = Time.unscaledTime + observerSeconds;
            SetView(View.Seat, instant: false);
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
                case View.Seat:
                {
                    // 그 사람 등 뒤 위에서 전장 가운데를 내려다본다 (그 사람 + 상대들 + 오가는 스킬이 한 화면에) — 지금 선 칸을 따라간다
                    var latest = _events != null ? _events.Latest : null;
                    int column = latest != null ? latest.Players[(int)_seat].WorldColumn : 1;
                    Vector3 fwd = _g.ToWorldDirection(_g.SeatForward(_seat));
                    pos = _g.ToWorld(_g.PlayerPosition(_seat, column), seatHeight * _zoom) - fwd * seatBack * _zoom;
                    rot = Quaternion.LookRotation(_g.ToWorld(_g.Center, 0f) - pos, Vector3.up);
                    break;
                }
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
                SetView(_view == View.Side ? View.Top : _view == View.Top ? (_observer ? View.Seat : View.Home) : View.Side, instant: false);
            if (_observer)
            {
                for (int i = 0; i < TeamUtil.TeamCount; i++)
                    if (KeyInput.Down(Key.Digit1 + i) && _g.HasTeam(TeamUtil.TeamAtSeat(i))) WatchSeat(TeamUtil.TeamAtSeat(i));
                if (KeyInput.Down(Key.Tab)) StepSeat(+1);
                if (KeyInput.Down(Key.O))
                {
                    _autoCycle = !_autoCycle;
                    _nextCycle = Time.unscaledTime + observerSeconds;
                }
                if (_autoCycle && _view == View.Seat && Time.unscaledTime >= _nextCycle) StepSeat(+1);
                // 자리 시점은 그 사람이 칸을 옮기면 따라간다
                if (_view == View.Seat && _blend >= 1f)
                {
                    Target(View.Seat, out _toPos, out _toRot);
                    Apply(Vector3.Lerp(_head.position, _toPos, Time.unscaledDeltaTime * 4f), Quaternion.Slerp(_head.rotation, _toRot, Time.unscaledDeltaTime * 4f));
                }
            }
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
            string name = _view == View.Side ? "사이드뷰" : _view == View.Top ? "탑뷰" : _view == View.Seat ? SeatLabel(_seat) + " 시점" : "Home 시점";
            string keys = _observer ? "1~6 플레이어 · Tab 다음 · O 자동 순환 " + (_autoCycle ? "켜짐" : "꺼짐") + " · V 전체 보기 · 휠 확대" : "V 전환 · B 반대편 · 휠 확대";
            GUI.Label(new Rect(12, Screen.height - 28, 900, 24), $"카메라: {name}   ({keys})", _style);
            if (!_observer || _events == null || _events.Latest == null) return;
            var h = _events.Latest.Header;
            if (h.JoinSeat == 0) return;
            if (_noticeStyle == null)
            {
                _noticeStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _noticeStyle.normal.textColor = new Color(1f, 0.78f, 0.3f, 1f);
            }
            GUI.Label(new Rect(0, 40, Screen.width, 50), $"새로운 플레이어가 {Mathf.Max(1, Mathf.CeilToInt(h.JoinIn))}초 뒤 {h.JoinSeat}번 자리에 들어옵니다!", _noticeStyle);
        }

        private string SeatLabel(Team team)
        {
            var latest = _events != null ? _events.Latest : null;
            bool bot = latest == null || latest.Players[(int)team].IsBot != 0;
            string name = latest != null ? latest.Names[(int)team] : "";
            return TeamUtil.SeatNumber(team) + "번 " + (bot ? "(AI)" : string.IsNullOrEmpty(name) ? "(플레이어)" : "(" + name + ")");
        }
    }
}
