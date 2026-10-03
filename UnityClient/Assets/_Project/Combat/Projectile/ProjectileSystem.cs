// 서버 투사체 시스템 (§11.2, §21.3)
//   · 플레이어 대상 투사체는 "조준한 열"의 상대 발판으로 날아가고, 도착 틱의 서버 위치로 회피 판정한다.
//   · 유닛 대상 투사체는 대상을 추적하며, 대상이 먼저 죽으면 마지막 위치에서 소멸한다.
//   · 풀 배열 고정 크기 (전투 중 할당 없음, §17)

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed class ProjectileSystem
    {
        private const int LostTarget = -1;

        private readonly ProjectileState[] _pool = new ProjectileState[MatchSnapshot.MaxProjectiles];
        private int _nextId = 1;

        public ProjectileSystem()
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new ProjectileState();
        }

        public ProjectileState[] Pool => _pool;

        public void Clear()
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i].Active = false;
        }

        public bool Launch(in ProjectileLaunch l)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                var p = _pool[i];
                if (p.Active) continue;
                p.Active = true;
                p.Id = _nextId++;
                p.SpellId = l.SpellId;
                p.Team = l.CasterTeam;
                p.WorldColumn = l.WorldColumn;
                p.Position = l.From;
                p.Height = l.Height;
                p.Speed = l.Speed;
                p.Damage = l.Damage;
                p.TargetUnitId = l.TargetUnitId;
                p.LastKnownTarget = l.From;
                p.Dodgeable = l.Dodgeable;
                p.IsPlayerSkill = l.IsPlayerSkill;
                p.FocusBonus = l.FocusBonus;
                return true;
            }
            Debug.LogWarning("[ProjectileSystem] 투사체 풀 부족 — 발사 생략");
            return false;
        }

        public void Tick(MatchSimulation sim, float dt)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                var p = _pool[i];
                if (!p.Active) continue;

                Team enemy = TeamUtil.Opponent(p.Team);
                Vector2 target;
                if (p.TargetsPlayer)
                {
                    target = sim.Arena.PlayerPosition(enemy, p.WorldColumn);
                }
                else if (p.TargetUnitId != LostTarget && sim.TryGetUnitInfo(p.TargetUnitId, out Vector2 unitPos, out _, out _))
                {
                    target = unitPos;
                    p.LastKnownTarget = unitPos;
                }
                else
                {
                    p.TargetUnitId = LostTarget;
                    target = p.LastKnownTarget;
                }

                Vector2 delta = target - p.Position;
                float dist = delta.magnitude;
                float step = p.Speed * dt;
                if (dist <= step + 0.05f)
                {
                    p.Position = target;
                    Arrive(sim, p, enemy);
                    p.Active = false;
                }
                else
                {
                    p.Position += delta * (step / dist);
                }
            }
        }

        private static void Arrive(MatchSimulation sim, ProjectileState p, Team enemy)
        {
            sim.PublishFx(p.SpellId, VfxPart.Impact, p.Position, p.TargetsPlayer ? 1.2f : 0.6f, p.WorldColumn);
            if (p.TargetUnitId == LostTarget) return;

            var ctx = new DamageContext
            {
                SpellId = p.SpellId,
                AttackerTeam = p.Team,
                AttackerKind = TargetKind.Player,
                AttackerId = TeamUtil.PlayerId(p.Team),
                BaseDamage = p.Damage,
                IsPlayerSkill = p.IsPlayerSkill,
                FocusBonusMultiplier = p.FocusBonus,
                IsDodgeable = p.Dodgeable,
                AimedWorldColumn = p.WorldColumn,
            };

            if (p.TargetsPlayer)
            {
                ctx.TargetKind = TargetKind.Player;
                ctx.TargetId = TeamUtil.PlayerId(enemy);
            }
            else
            {
                if (!sim.TryGetUnitInfo(p.TargetUnitId, out _, out TargetKind kind, out _)) return;
                ctx.TargetKind = kind;
                ctx.TargetId = p.TargetUnitId;
                ctx.IsDodgeable = false;
            }
            sim.Resolver.ResolveDamage(ctx);
        }

        /// <summary>봇 관찰용: team을 향해 날아오는 플레이어 대상 투사체</summary>
        public int CountIncoming(MatchSimulation sim, Team team, out int nearestWorldColumn, out float nearestEta)
        {
            int n = 0;
            nearestWorldColumn = -1;
            nearestEta = float.MaxValue;
            for (int i = 0; i < _pool.Length; i++)
            {
                var p = _pool[i];
                if (!p.Active || !p.TargetsPlayer || p.Team == team || !p.Dodgeable) continue;
                n++;
                Vector2 target = sim.Arena.PlayerPosition(team, p.WorldColumn);
                float eta = Vector2.Distance(target, p.Position) / Mathf.Max(0.01f, p.Speed);
                if (eta < nearestEta)
                {
                    nearestEta = eta;
                    nearestWorldColumn = p.WorldColumn;
                }
            }
            return n;
        }
    }
}
