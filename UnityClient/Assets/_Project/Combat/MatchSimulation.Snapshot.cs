// MatchSimulation — 상태 출력 (스냅샷 · 봇 관찰)

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Network;
using SpellboundVR.Spells.Effects;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed partial class MatchSimulation
    {
        public void WriteSnapshot(MatchSnapshot s)
        {
            ref var h = ref s.Header;
            h.MatchSerial = _matchSerial;
            h.Phase = (byte)_phase;
            h.PhaseTimeLeft = _phaseTimer;
            h.MatchTime = _matchTime;
            h.ScalingStage = _scaling.Stage;
            h.NextScalingIn = _scaling.NextStageIn(_matchTime, Rules);
            h.WaveIndex = _waves.WaveIndex;
            h.NextWaveIn = _waves.NextWaveIn;
            h.NextWaveIsBrute = (byte)(_waves.NextIsBrute(Rules) ? 1 : 0);
            h.HomeAttackMultiplier = GetAttackMultiplier(Team.Home);
            h.AwayAttackMultiplier = GetAttackMultiplier(Team.Away);
            h.Winner = (byte)(_phase == MatchPhase.Ended ? _outcome.Winner : MatchWinner.None);
            h.EndReason = (byte)_outcome.Reason;
            h.Duration = _phase == MatchPhase.Ended ? _duration : _matchTime;

            for (int t = 0; t < TeamUtil.TeamCount; t++)
            {
                var p = Players[t];
                ref var ps = ref s.Players[t];
                ps.Present = (byte)(p.Present ? 1 : 0);
                ps.IsBot = (byte)(p.IsBot ? 1 : 0);
                ps.Connected = (byte)(p.Connected ? 1 : 0);
                ps.WorldColumn = (byte)p.WorldColumn;
                ps.Alive = (byte)(p.Alive ? 1 : 0);
                ps.Hp = Mathf.CeilToInt(p.Hp);
                ps.MaxHp = Mathf.CeilToInt(p.MaxHp);
                ps.Accuracy = p.Accuracy;
                ps.AfkSecondsLeft = _phase == MatchPhase.Playing ? AfkMonitor.SecondsLeft(p, _matchTime, Rules) : -1f;

                if (_status.TryGet(p.Id, "S08_Shield", out IStatusEffect effect) && effect is ShieldRegenEffect shield)
                {
                    ps.Shield = Mathf.CeilToInt(shield.ShieldPoints);
                    ps.ShieldEndTime = _matchTime + shield.Remaining;
                }
                else
                {
                    ps.Shield = 0;
                    ps.ShieldEndTime = 0f;
                }

                for (int i = 0; i < DeckData.SlotCount; i++)
                {
                    ref readonly SlotRuntime sr = ref p.Slots[i];
                    ref var ss = ref s.Slots[t * DeckData.SlotCount + i];
                    ss.SpellId = (short)sr.SpellId;
                    ss.State = (byte)sr.State;
                    ss.HighPowerUsesLeft = (byte)Mathf.Max(0, sr.HighPowerUsesLeft);
                    ss.CooldownEndTime = sr.CooldownEndTime;
                    ss.CooldownDuration = sr.CooldownDuration;
                }
            }

            for (int i = 0; i < Structures.Length; i++)
            {
                var st = Structures[i];
                ref var ss = ref s.Structures[i];
                ss.Id = st.Id;
                ss.Team = (byte)st.Team;
                ss.IsNexus = (byte)(st.IsNexus ? 1 : 0);
                ss.Alive = (byte)(st.Alive ? 1 : 0);
                ss.Locked = (byte)(st.IsNexus && NexusLock.IsLocked(Defense, st.Team) ? 1 : 0);
                ss.Hp = Mathf.CeilToInt(st.Hp);
                ss.MaxHp = Mathf.CeilToInt(st.MaxHp);
                ss.U = st.Position.x;
                ss.V = st.Position.y;
                ss.ShotCount = st.ShotCount;
                ss.ShotTargetId = st.LastTargetId;
            }

            int uc = 0;
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (!u.Active || u.Dead) continue;
                ref var us = ref s.Units[uc++];
                us.Id = u.Id;
                us.Team = (byte)u.Team;
                us.Kind = (byte)u.Kind;
                us.State = (byte)u.State;
                us.U = u.Position.x;
                us.V = u.Position.y;
                us.Hp = Mathf.CeilToInt(u.Hp);
                us.MaxHp = Mathf.CeilToInt(u.MaxHp);
            }
            h.UnitCount = (byte)uc;

            int pc = 0;
            var pool = _projectiles.Pool;
            for (int i = 0; i < pool.Length; i++)
            {
                var p = pool[i];
                if (!p.Active) continue;
                ref var ps = ref s.Projectiles[pc++];
                ps.Id = p.Id;
                ps.SpellId = (short)p.SpellId;
                ps.Team = (byte)p.Team;
                ps.TargetsPlayer = (byte)(p.TargetsPlayer ? 1 : 0);
                ps.U = p.Position.x;
                ps.V = p.Position.y;
                ps.Height = p.Height;
            }
            h.ProjectileCount = (byte)pc;
        }

        /// <summary>§20.1 BotObservation — 열은 봇 자신의 시점</summary>
        public BotObservation BuildObservation(Team team)
        {
            Team enemy = TeamUtil.Opponent(team);
            var self = Players[(int)team];
            var opp = Players[(int)enemy];

            byte mask = 0;
            for (int i = 0; i < DeckData.SlotCount; i++)
                if (self.Slots[i].State == SlotState.Ready) mask |= (byte)(1 << i);

            int incoming = _projectiles.CountIncoming(this, team, out int nearestCol, out float eta);

            return new BotObservation
            {
                SelfHp = Mathf.CeilToInt(self.Hp),
                OpponentHp = Mathf.CeilToInt(opp.Hp),
                SelfColumn = TeamUtil.ToLocalColumn(team, self.WorldColumn),
                OpponentColumn = TeamUtil.ToLocalColumn(team, opp.WorldColumn),
                MatchTimeSeconds = _matchTime,
                SelfSlotReadyMask = mask,
                SelfTowerAlive = Defense.TowerAlive(team),
                SelfNexusAlive = Defense.NexusAlive(team),
                OpponentTowerAlive = Defense.TowerAlive(enemy),
                OpponentNexusAlive = Defense.NexusAlive(enemy),
                IncomingProjectileCount = incoming,
                NearestIncomingProjectileColumn = incoming > 0 ? TeamUtil.ToLocalColumn(team, nearestCol) : Column.Center,
                NearestIncomingProjectileEta = incoming > 0 ? eta : -1f,
            };
        }
    }
}
