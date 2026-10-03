// §6.5 클라이언트 즉시 예측 표시 → 서버 확정 → 거절 시 되돌림
//   · 끝 칸 바깥 방향 입력은 보내지 않는다.
//   · 예측 이동 후 서버 확정 열이 PendingTimeout 안에 오지 않거나 다르면 서버 위치로 되돌린다.

using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Input
{
    public sealed class LocalMovePredictor : MonoBehaviour
    {
        public float pendingTimeout = 0.6f;

        private ClientSession _session;
        private ISpellInputSource _input;
        private CommanderPlatform _platform;
        private int _confirmedWorld = 1;
        private int _predictedWorld = 1;
        private bool _pending;
        private float _pendingSince;
        private float _lastSendTime = -999f;

        public void Initialize(ClientSession session, ISpellInputSource input, CommanderPlatform platform)
        {
            _session = session;
            _input = input;
            _platform = platform;
            _input.OnMoveGesture += HandleMoveGesture;
            _session.Events.OnPositionChanged += HandlePositionChanged;
            _session.Events.OnLocalMoveRejected += HandleRejected;
            _session.Events.OnLocalTeamAssigned += HandleTeamAssigned;
        }

        private void OnDestroy()
        {
            if (_input != null) _input.OnMoveGesture -= HandleMoveGesture;
            if (_session != null)
            {
                _session.Events.OnPositionChanged -= HandlePositionChanged;
                _session.Events.OnLocalMoveRejected -= HandleRejected;
                _session.Events.OnLocalTeamAssigned -= HandleTeamAssigned;
            }
        }

        private void HandleTeamAssigned(Team team)
        {
            _pending = false;
            _platform.SetTeam(team, _confirmedWorld);
        }

        private void HandleMoveGesture(sbyte localDirection)
        {
            if (_session == null || !_session.HasClient) return;
            if (Time.time - _lastSendTime < _session.Rules.MoveCooldownSeconds) return;

            Team team = _session.Events.LocalTeam;
            int worldDir = TeamUtil.ToWorldDirection(team, localDirection);
            int from = _pending ? _predictedWorld : _confirmedWorld;
            int target = from + worldDir;
            if (target < 0 || target > 2) return; // §6.5 끝 칸 바깥 방향 무시

            _session.Client.RequestMove(new MoveRequest { Direction = localDirection });
            _lastSendTime = Time.time;
            _predictedWorld = target;
            _pending = true;
            _pendingSince = Time.time;
            _platform.Place(target, true);
        }

        private void HandlePositionChanged(Team team, Column localColumn)
        {
            if (_session == null || !_session.Events.HasLocalTeam || team != _session.Events.LocalTeam) return;
            _confirmedWorld = TeamUtil.ToWorldColumn(team, localColumn);
            if (_pending)
            {
                if (_confirmedWorld == _predictedWorld) _pending = false;
                return;
            }
            if (_platform.WorldColumn != _confirmedWorld) _platform.Place(_confirmedWorld, true);
        }

        private void HandleRejected(Column localColumn)
        {
            _confirmedWorld = TeamUtil.ToWorldColumn(_session.Events.LocalTeam, localColumn);
            _pending = false;
            _platform.Place(_confirmedWorld, true);
        }

        private void Update()
        {
            if (!_pending) return;
            if (Time.time - _pendingSince > pendingTimeout)
            {
                _pending = false;
                if (_platform.WorldColumn != _confirmedWorld) _platform.Place(_confirmedWorld, true);
            }
        }
    }
}
