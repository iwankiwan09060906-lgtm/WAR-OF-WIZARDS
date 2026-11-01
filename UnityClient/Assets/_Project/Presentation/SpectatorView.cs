// 4인 개인전 관전 — 탈락한 뒤 경기가 끝날 때까지 남은 플레이어의 시점을 돌아가며 본다. 보기 전용 (판정과 무관).
//   내 리그를 보고 있는 플레이어의 발판(칸 포함) 위로 옮긴다 — 시점이 곧 그 사람이 보는 전장이다.
//   전환: Tab · → = 다음 사람, ← = 이전 사람 / 컨트롤러 A = 다음, B = 이전 / 경기 메뉴의 다음 · 이전 버튼 (핸드트래킹)
//   보던 사람이 탈락하면 다음 사람으로 넘어간다. 남은 사람이 없으면 그 자리에 머문다.

using System;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Presentation
{
    public sealed class SpectatorView : MonoBehaviour
    {
        private ClientSession _session;
        private Transform _rig;
        private float _rigHeight;
        private float _viewPitch;
        private Team _target;
        private bool _active;

        public bool Active => _active;

        /// <summary>지금 보고 있는 플레이어 (Active 일 때만 뜻이 있다)</summary>
        public Team Target => _target;

        /// <summary>키 · 컨트롤러 전환을 받을지 (경기 메뉴를 여는 동안 끈다 — A 버튼이 메뉴 클릭과 겹친다)</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>보는 사람이 바뀔 때 (HUD 안내 갱신용)</summary>
        public event Action<Team> OnTargetChanged;

        public void Initialize(ClientSession session, Transform rig, float viewPitch)
        {
            _session = session;
            _rig = rig;
            _viewPitch = viewPitch;
            _rigHeight = rig != null ? rig.position.y - session.Arena.GroundY : 0f;
        }

        /// <summary>관전을 시작한다. 볼 사람이 하나도 없으면 false</summary>
        public bool Begin()
        {
            if (_session == null || _rig == null) return false;
            if (!TryStep(_session.Events.LocalTeam, +1, out Team first)) return false;
            _active = true;
            InputEnabled = true;
            Focus(first);
            return true;
        }

        public void End() => _active = false;

        /// <summary>다음(dir=+1) · 이전(dir=-1) 사람으로 (볼 사람이 더 없으면 그대로)</summary>
        public void Step(int dir)
        {
            if (_active && TryStep(_target, dir, out Team t)) Focus(t);
        }

        private bool Watchable(MatchSnapshot s, Team team)
        {
            if (team == _session.Events.LocalTeam) return false;
            ref var p = ref s.Players[(int)team];
            return p.Present != 0 && p.Alive != 0 && p.Place == 0;
        }

        /// <summary>from 다음(dir=+1) · 이전(dir=-1) 자리부터 발판 번호 순서로 한 바퀴 돌며 볼 수 있는 첫 사람</summary>
        private bool TryStep(Team from, int dir, out Team found)
        {
            found = from;
            var s = _session.Events.Latest;
            if (s == null) return false;
            for (int i = 1; i <= TeamUtil.TeamCount; i++)
            {
                var t = TeamUtil.TeamAtClock(TeamUtil.ClockIndex(from) + dir * i);
                if (!Watchable(s, t)) continue;
                found = t;
                return true;
            }
            return false;
        }

        private void Focus(Team team)
        {
            _target = team;
            Place();
            OnTargetChanged?.Invoke(team);
        }

        /// <summary>매 프레임 놓는다 — 탈락 직전에 보낸 이동이 거절되면 CommanderPlatform 이 리그를 내 자리로 되돌리기 때문</summary>
        private void Place()
        {
            var s = _session.Events.Latest;
            if (s == null) return;
            int column = Mathf.Clamp(s.Players[(int)_target].WorldColumn, 0, 2);
            var g = _session.Arena;
            Vector3 pos = g.ToWorld(g.PlayerPosition(_target, column), _rigHeight);
            _rig.SetPositionAndRotation(pos, g.FacingRotation(_target) * Quaternion.Euler(_viewPitch, 0f, 0f));
        }

        private static bool Pressed(Key key, OVRInput.Button button)
        {
            if (KeyInput.Down(key)) return true;
            try { return OVRInput.GetDown(button); }
            catch (Exception) { return false; }
        }

        private void LateUpdate()
        {
            if (!_active || _session == null) return;
            var s = _session.Events.Latest;
            if (s == null) return;

            if (!Watchable(s, _target))
            {
                // 보던 사람이 탈락 — 다음 사람에게 (없으면 마지막 자리에 머문다)
                if (TryStep(_target, +1, out Team t)) Focus(t);
                return;
            }
            if (InputEnabled)
            {
                if (Pressed(Key.Tab, OVRInput.Button.One) || KeyInput.Down(Key.RightArrow))
                {
                    Step(+1);
                    return;
                }
                if (Pressed(Key.LeftArrow, OVRInput.Button.Two))
                {
                    Step(-1);
                    return;
                }
            }
            Place();
        }
    }
}
