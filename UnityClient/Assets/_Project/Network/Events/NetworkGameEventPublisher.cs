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
        /// <summary>VFX 재생 트리거 (월드 레인 번호 포함 — 4인 개인전은 열로 레인을 알 수 없다)</summary>
        public event Action<int, VfxPart, Vector3, int> OnSpellFxLane;
        /// <summary>4인 개인전 탈락: 진영, 순위</summary>
        public event Action<Team, int> OnPlayerEliminated;

        private readonly MatchSnapshot _prev = new MatchSnapshot();
        private ArenaGeometry _arena;
        private bool _hasPrev;
        private int _lastSecond = -1;
        private int _lastWaveSecond = -1;
        private int _lastAfkSecond = int.MinValue;
        private int _lastCountdownSecond = -1;
        /// <summary>내 경기가 이 기기에서 이미 끝남 (개인전 탈락 · 기권) — 경기 종료를 또 알리지 않는다</summary>
        private bool _localEnded;

        public NetworkGameEventPublisher(ArenaGeometry arena)
        {
            _arena = arena;
        }

        public Team LocalTeam { get; private set; } = Team.Home;

        public bool HasLocalTeam { get; private set; }

        /// <summary>가장 최근에 적용된 스냅샷 (표시 전용 — 월드 뷰가 읽는다)</summary>
        public MatchSnapshot Latest { get; private set; }

        public ArenaGeometry Arena => _arena;

        /// <summary>내 순위 — 개인전: 결정됐으면 그 순위, 아직이면 지금 살아 있는 사람 수(지금 나가면 받을 순위). 1:1 은 0</summary>
        public int LocalPlace
        {
            get
            {
                if (_arena.IsDuel || Latest == null) return 0;
                int place = Latest.Players[(int)LocalTeam].Place;
                if (place > 0) return place;
                int alive = 0;
                for (int t = 0; t < _arena.TeamCount; t++)
                    if (Latest.Players[t].Present != 0 && Latest.Players[t].Alive != 0) alive++;
                return alive;
            }
        }

        /// <summary>이 경기에서 내가 다른 플레이어에게 입힌 피해</summary>
        public int LocalDamageDealt => Latest != null ? Latest.Players[(int)LocalTeam].DamageDealt : 0;

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
            if (full) _localEnded = false;

            // 단계
            if (phase != prevPhase)
            {
                OnPhaseInfo?.Invoke(phase, s.Header.PhaseTimeLeft);
                _lastCountdownSecond = -1;
                if (phase == MatchPhase.Playing)
                {
                    OnMatchStarted?.Invoke(AnyHumanOpponent(s) ? OpponentType.Human : OpponentType.Bot);
                }
                else if (phase == MatchPhase.Ended && !_localEnded)
                {
                    _localEnded = true;
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
            int teamCount = _arena.TeamCount;
            for (int t = 0; t < teamCount; t++)
            {
                var team = (Team)t;
                ref var p = ref s.Players[t];
                ref var pp = ref _prev.Players[t];
                if (!full && p.Place != 0 && pp.Place == 0 && !_arena.IsDuel && phase == MatchPhase.Playing)
                    OnPlayerEliminated?.Invoke(team, p.Place);
                if (full || Mathf.Abs(p.AttackMultiplier - pp.AttackMultiplier) > 0.001f) OnAttackMultiplierChanged?.Invoke(team, p.AttackMultiplier);
                if (full || p.Hp != pp.Hp || p.MaxHp != pp.MaxHp) OnHpChanged?.Invoke(team, p.Hp, p.MaxHp);
                if (full || p.WorldColumn != pp.WorldColumn) OnPositionChanged?.Invoke(team, ToLocalColumn(p.WorldColumn));

                bool shieldOn = p.Shield > 0;
                bool prevShieldOn = pp.Shield > 0;
                if (full || shieldOn != prevShieldOn || (shieldOn && Mathf.Abs(p.ShieldEndTime - pp.ShieldEndTime) > 0.05f))
                {
                    float remaining = shieldOn ? Mathf.Max(0f, p.ShieldEndTime - s.Header.MatchTime) : 0f;
                    OnStatusEffectChanged?.Invoke(TeamUtil.PlayerId(team), "S08_Shield", shieldOn, remaining);
                }

                // S01 연막 · S10 집중 · H04 반사 결계 (켜짐/꺼짐만)
                if (full || p.Flags != pp.Flags)
                {
                    PublishFlag(team, p.Flags, pp.Flags, PlayerFlags.Blind, "S01_Blind", full);
                    PublishFlag(team, p.Flags, pp.Flags, PlayerFlags.Focus, "S10_Focus", full);
                    PublishFlag(team, p.Flags, pp.Flags, PlayerFlags.Reflect, "H04_Reflect", full);
                }
            }

            // 개인전: 내가 탈락하면 내 경기는 여기서 끝 (남은 사람은 계속 싸운다)
            if (!_arena.IsDuel && phase == MatchPhase.Playing && !_localEnded && s.Players[(int)LocalTeam].Place != 0)
            {
                _localEnded = true;
                OnMatchEnded?.Invoke(MatchResult.Lose, MatchEndReason.PlayerDeath, s.Header.MatchTime);
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
            for (int t = 0; t < teamCount; t++)
            {
                var team = (Team)t;
                ref var tower = ref s.Structure(team, false);
                ref var nexus = ref s.Structure(team, true);
                ref var pTower = ref _prev.Structure(team, false);
                ref var pNexus = ref _prev.Structure(team, true);
                if (full || tower.Alive != pTower.Alive || nexus.Alive != pNexus.Alive)
                    OnDefenseLayerChanged?.Invoke(team, tower.Alive != 0, nexus.Alive != 0);
                if (!full && pNexus.Alive != 0 && nexus.Alive == 0 && s.Players[t].Place == 0)
                    OnBreachStarted?.Invoke(team == LocalTeam ? TeamUtil.Opponent(team) : LocalTeam); // 개인전: 남의 넥서스 = 내 쪽 돌파 소식
                if (full || tower.Hp != pTower.Hp) OnStructureHpChanged?.Invoke(tower.Id, tower.Hp, tower.MaxHp);
                if (full || nexus.Hp != pNexus.Hp) OnStructureHpChanged?.Invoke(nexus.Id, nexus.Hp, nexus.MaxHp);
            }

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

        private bool AnyHumanOpponent(MatchSnapshot s)
        {
            for (int t = 0; t < _arena.TeamCount; t++)
                if (t != (int)LocalTeam && s.Players[t].Present != 0 && s.Players[t].IsBot == 0) return true;
            return false;
        }

        private void PublishFlag(Team team, byte flags, byte prevFlags, byte bit, string effectId, bool full)
        {
            bool on = (flags & bit) != 0;
            if (full || on != ((prevFlags & bit) != 0)) OnStatusEffectChanged?.Invoke(TeamUtil.PlayerId(team), effectId, on, 0f);
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
            _arena.TryLocalColumn(LocalTeam, worldColumn, out Column local);
            OnSpellFx?.Invoke(spellId, part, world, local);
            OnSpellFxLane?.Invoke(spellId, part, world, worldColumn);
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

        /// <summary>서버 스냅숏 없이 이 기기에서 경기를 끝낸다 (PC 서버 경기 중 기권 · 접속 포기) — 결과 화면 · 전적 저장이 같은 경로로 처리된다</summary>
        public void RaiseLocalMatchEnd(MatchResult result, MatchEndReason reason, float duration)
        {
            _localEnded = true;
            OnMatchEnded?.Invoke(result, reason, duration);
        }

        /// <summary>클라이언트 자체 사유 (예: 인식 실패)를 같은 거절 채널로 표시</summary>
        public void RaiseLocalRejection(string message)
        {
            OnCastRejected?.Invoke(message);
        }
    }
}
