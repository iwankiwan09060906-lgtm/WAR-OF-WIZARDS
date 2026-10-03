// §25.2 IGameEvents 실제 발행자 (클라이언트 측)
//
// 서버 상태(스냅샷)를 직전 값과 비교해 "바뀐 것만" 게임 이벤트로 바꾼다. HUD(ㅈㅇㅈ)와 VFX(ㅊㄱㅇ)는
// 게임 로직을 직접 읽지 않고 이 이벤트만 구독한다 (Rule 12, §23.2).
// 1회성 서버 이벤트(VFX · 거절 · 피격)는 IMatchEventSink로 받는다.
//   · 로컬 모드: MatchSimulation의 싱크로 직접 연결
//   · Fusion 모드: NetworkMatchState RPC 수신부가 호출
//
// 열(Column) 규칙: 이벤트의 열은 "이 클라이언트 플레이어 시점"이다 (Away는 좌우 반전).
// 위치(Vector3)는 월드 좌표다.

using System;
using SpellboundVR.Arena;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using UnityEngine;

namespace SpellboundVR.Network
{
    public sealed class NetworkGameEventPublisher : IGameEvents, IMatchEventSink
    {
        // ── IGameEvents (계약) ───────────────────────────────
        public event Action<Team, int, int> OnHpChanged;
        public event Action<Team, Column> OnPositionChanged;
        public event Action<int, SlotState> OnSlotStateChanged;
        public event Action<int, float> OnSlotCooldownStarted;
        public event Action<int, int> OnHighPowerUsesChanged;
        public event Action<string> OnCastRejected;
        public event Action<int, string, bool, float> OnStatusEffectChanged;
        public event Action<Team, bool, bool> OnDefenseLayerChanged;
        public event Action<int, int, int> OnStructureHpChanged;
        public event Action<Team, float> OnAttackMultiplierChanged;
        public event Action<float> OnMatchTimeChanged;
        public event Action<int> OnScalingStageChanged;
        public event Action<float, bool> OnWaveTimerChanged;
        public event Action<float> OnAfkWarning;
        public event Action<Team> OnBreachStarted;
        public event Action<int, VfxPart, Vector3, Column> OnSpellFx;
        public event Action<OpponentType> OnMatchStarted;
        public event Action<MatchResult, MatchEndReason, float> OnMatchEnded;

        // ── 클라이언트 확장 이벤트 (계약 외, 표시 · 입력 보정용) ─────────
        /// <summary>단계 변경 (카운트다운 3-2-1 표시 등): phase, 남은 시간</summary>
        public event Action<MatchPhase, float> OnPhaseInfo;
        /// <summary>내 시전이 서버에서 확정됨: slot, spellId</summary>
        public event Action<int, int> OnLocalCastAccepted;
        /// <summary>내 이동이 거절됨: 서버 확정 열 (내 시점)</summary>
        public event Action<Column> OnLocalMoveRejected;
        /// <summary>피격 표시: targetId, 월드 위치, 양, 종류</summary>
        public event Action<int, Vector3, int, HitResultKind> OnHitFeedback;
        /// <summary>로컬 팀이 확정됨</summary>
        public event Action<Team> OnLocalTeamAssigned;

        private readonly MatchSnapshot _prev = new MatchSnapshot();
        private ArenaGeometry _arena;
        private bool _hasPrev;
        private int _lastSecond = -1;
        private int _lastWaveSecond = -1;
        private int _lastAfkSecond = int.MinValue;
        private int _lastCountdownSecond = -1;

        public NetworkGameEventPublisher(ArenaGeometry arena)
        {
            _arena = arena;
        }

        public Team LocalTeam { get; private set; } = Team.Home;

        public bool HasLocalTeam { get; private set; }

        /// <summary>가장 최근에 적용된 스냅샷 (표시 전용 — 월드 뷰가 읽는다)</summary>
        public MatchSnapshot Latest { get; private set; }

        public ArenaGeometry Arena => _arena;

        public void SetLocalTeam(Team team)
        {
            bool changed = !HasLocalTeam || LocalTeam != team;
            LocalTeam = team;
            HasLocalTeam = true;
            if (changed)
            {
                _hasPrev = false; // 전체 상태를 다시 발행
                OnLocalTeamAssigned?.Invoke(team);
            }
        }

        public Column ToLocalColumn(int worldColumn) => TeamUtil.ToLocalColumn(LocalTeam, worldColumn);

        // ── 스냅샷 diff ───────────────────────────────────────

        public void ApplySnapshot(MatchSnapshot s)
        {
            Latest = s;
            if (!HasLocalTeam) return;

            bool full = !_hasPrev || _prev.Header.MatchSerial != s.Header.MatchSerial;
            var phase = (MatchPhase)s.Header.Phase;
            var prevPhase = full ? (MatchPhase)255 : (MatchPhase)_prev.Header.Phase;

            // 단계
            if (phase != prevPhase)
            {
                OnPhaseInfo?.Invoke(phase, s.Header.PhaseTimeLeft);
                _lastCountdownSecond = -1;
                if (phase == MatchPhase.Playing)
                {
                    var opp = s.Players[(int)TeamUtil.Opponent(LocalTeam)];
                    OnMatchStarted?.Invoke(opp.IsBot != 0 ? OpponentType.Bot : OpponentType.Human);
                }
                else if (phase == MatchPhase.Ended)
                {
                    OnMatchEnded?.Invoke(MatchResultResolver.ToLocalResult((MatchWinner)s.Header.Winner, LocalTeam),
                                         (MatchEndReason)s.Header.EndReason, s.Header.Duration);
                }
            }
            else if (phase == MatchPhase.Countdown || phase == MatchPhase.Ended)
            {
                int sec = Mathf.CeilToInt(s.Header.PhaseTimeLeft);
                if (sec != _lastCountdownSecond)
                {
                    _lastCountdownSecond = sec;
                    OnPhaseInfo?.Invoke(phase, s.Header.PhaseTimeLeft);
                }
            }

            // 플레이어
            for (int t = 0; t < TeamUtil.TeamCount; t++)
            {
                var team = (Team)t;
                ref var p = ref s.Players[t];
                ref var pp = ref _prev.Players[t];
                if (full || p.Hp != pp.Hp || p.MaxHp != pp.MaxHp) OnHpChanged?.Invoke(team, p.Hp, p.MaxHp);
                if (full || p.WorldColumn != pp.WorldColumn) OnPositionChanged?.Invoke(team, ToLocalColumn(p.WorldColumn));

                bool shieldOn = p.Shield > 0;
                bool prevShieldOn = pp.Shield > 0;
                if (full || shieldOn != prevShieldOn || (shieldOn && Mathf.Abs(p.ShieldEndTime - pp.ShieldEndTime) > 0.05f))
                {
                    float remaining = shieldOn ? Mathf.Max(0f, p.ShieldEndTime - s.Header.MatchTime) : 0f;
                    OnStatusEffectChanged?.Invoke(TeamUtil.PlayerId(team), "S08_Shield", shieldOn, remaining);
                }
            }

            // AFK 경고 (내 것만)
            {
                ref var me = ref s.Players[(int)LocalTeam];
                int afkSec = me.AfkSecondsLeft < 0f ? -1 : Mathf.CeilToInt(me.AfkSecondsLeft);
                if (full || afkSec != _lastAfkSecond)
                {
                    _lastAfkSecond = afkSec;
                    OnAfkWarning?.Invoke(afkSec < 0 ? -1f : me.AfkSecondsLeft);
                }
            }

            // 내 슬롯
            for (int i = 0; i < DeckData.SlotCount; i++)
            {
                ref var sl = ref s.Slot(LocalTeam, i);
                ref var ps = ref _prev.Slot(LocalTeam, i);
                if (full || sl.State != ps.State) OnSlotStateChanged?.Invoke(i, (SlotState)sl.State);
                bool cdStarted = sl.State == (byte)SlotState.Cooldown &&
                                 (full || ps.State != (byte)SlotState.Cooldown || Mathf.Abs(sl.CooldownEndTime - ps.CooldownEndTime) > 0.01f);
                if (cdStarted)
                    OnSlotCooldownStarted?.Invoke(i, Mathf.Max(0f, sl.CooldownEndTime - s.Header.MatchTime));
                if (sl.SpellId > 100 && (full || sl.HighPowerUsesLeft != ps.HighPowerUsesLeft))
                    OnHighPowerUsesChanged?.Invoke(i, sl.HighPowerUsesLeft);
            }

            // 구조물 · 방어 계층 · 돌파
            for (int t = 0; t < TeamUtil.TeamCount; t++)
            {
                var team = (Team)t;
                ref var tower = ref s.Structure(team, false);
                ref var nexus = ref s.Structure(team, true);
                ref var pTower = ref _prev.Structure(team, false);
                ref var pNexus = ref _prev.Structure(team, true);
                if (full || tower.Alive != pTower.Alive || nexus.Alive != pNexus.Alive)
                    OnDefenseLayerChanged?.Invoke(team, tower.Alive != 0, nexus.Alive != 0);
                if (!full && pNexus.Alive != 0 && nexus.Alive == 0)
                    OnBreachStarted?.Invoke(TeamUtil.Opponent(team));
                if (full || tower.Hp != pTower.Hp) OnStructureHpChanged?.Invoke(tower.Id, tower.Hp, tower.MaxHp);
                if (full || nexus.Hp != pNexus.Hp) OnStructureHpChanged?.Invoke(nexus.Id, nexus.Hp, nexus.MaxHp);
            }

            // 진영 배율
            if (full || Mathf.Abs(s.Header.HomeAttackMultiplier - _prev.Header.HomeAttackMultiplier) > 0.001f)
                OnAttackMultiplierChanged?.Invoke(Team.Home, s.Header.HomeAttackMultiplier);
            if (full || Mathf.Abs(s.Header.AwayAttackMultiplier - _prev.Header.AwayAttackMultiplier) > 0.001f)
                OnAttackMultiplierChanged?.Invoke(Team.Away, s.Header.AwayAttackMultiplier);

            // 시간 · 강화 · 웨이브
            int second = Mathf.FloorToInt(s.Header.MatchTime);
            if (full || second != _lastSecond)
            {
                _lastSecond = second;
                OnMatchTimeChanged?.Invoke(s.Header.MatchTime);
            }
            if (full || s.Header.ScalingStage != _prev.Header.ScalingStage)
                OnScalingStageChanged?.Invoke(s.Header.ScalingStage);
            int waveSec = Mathf.CeilToInt(s.Header.NextWaveIn);
            if (full || waveSec != _lastWaveSecond || s.Header.NextWaveIsBrute != _prev.Header.NextWaveIsBrute)
            {
                _lastWaveSecond = waveSec;
                OnWaveTimerChanged?.Invoke(s.Header.NextWaveIn, s.Header.NextWaveIsBrute != 0);
            }

            CopyToPrev(s);
            _hasPrev = true;
        }

        private void CopyToPrev(MatchSnapshot s)
        {
            _prev.Header = s.Header;
            Array.Copy(s.Players, _prev.Players, s.Players.Length);
            Array.Copy(s.Slots, _prev.Slots, s.Slots.Length);
            Array.Copy(s.Structures, _prev.Structures, s.Structures.Length);
        }

        // ── IMatchEventSink (1회성 서버 이벤트 수신) ───────────────

        void IMatchEventSink.OnSpellFx(int spellId, VfxPart part, Vector3 arenaPosition, int worldColumn)
        {
            Vector3 world = _arena.ToWorld(arenaPosition.x, arenaPosition.z, arenaPosition.y);
            OnSpellFx?.Invoke(spellId, part, world, ToLocalColumn(worldColumn));
        }

        void IMatchEventSink.OnCastAccepted(Team team, int slotIndex, int spellId)
        {
            if (HasLocalTeam && team == LocalTeam) OnLocalCastAccepted?.Invoke(slotIndex, spellId);
        }

        void IMatchEventSink.OnCastRejected(Team team, int slotIndex, CastRejectReason reason)
        {
            if (HasLocalTeam && team == LocalTeam) OnCastRejected?.Invoke(CastRejectReasonText.ToMessage(reason));
        }

        void IMatchEventSink.OnMoveRejected(Team team, int confirmedWorldColumn)
        {
            if (HasLocalTeam && team == LocalTeam) OnLocalMoveRejected?.Invoke(ToLocalColumn(confirmedWorldColumn));
        }

        void IMatchEventSink.OnHit(int targetId, Vector3 arenaPosition, int amount, HitResultKind kind)
        {
            Vector3 world = _arena.ToWorld(arenaPosition.x, arenaPosition.z, arenaPosition.y);
            OnHitFeedback?.Invoke(targetId, world, amount, kind);
        }

        /// <summary>클라이언트 자체 사유 (예: 인식 실패)를 같은 거절 채널로 표시</summary>
        public void RaiseLocalRejection(string message)
        {
            OnCastRejected?.Invoke(message);
        }
    }
}
