// 서버 투사체 시스템 (§11.2, §21.3)
//   · 플레이어 대상 투사체는 "조준한 열"의 상대 발판으로 날아가고, 도착 틱의 서버 위치로 회피 판정한다.
//   · 유닛 대상 투사체는 대상을 추적하며, 대상이 먼저 죽으면 마지막 위치에서 소멸한다.
//   · 지점(Point) 투사체는 그 지점에 떨어지고, 피해는 스킬의 OnHit 처리기가 준다 (파이어볼 · 화살 세례).
//   · 자기 귀환(OwnPlayer) 투사체는 시전자에게 돌아와 OnHit을 부른다 (흡혈 회복).
//   · ArcHeight > 0 이면 포물선 (높이만 표시용 — 판정은 지면 거리 기준)
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
        private const float PlayerHitHeight = 1.2f;
        private const float UnitHitHeight = 0.6f;

        private readonly ProjectileState[] _pool = new ProjectileState[MatchSnapshot.MaxProjectiles];
        private int _nextId = 1;

        public ProjectileSystem()
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new ProjectileState();
        }

        public ProjectileState[] Pool => _pool;

        public void Clear()
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                _pool[i].Active = false;
                _pool[i].OnHit = null;
            }
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
                p.StartHeight = l.Height;
                p.Speed = l.Speed;
                p.Damage = l.Damage;
                p.TargetUnitId = l.TargetUnitId;
                p.LastKnownTarget = l.From;
                p.Dodgeable = l.Dodgeable;
                p.IsPlayerSkill = l.IsPlayerSkill;
                p.FocusBonus = l.FocusBonus;
                p.Mode = l.Mode;
                p.TargetPoint = l.TargetPoint;
                p.ArcHeight = Mathf.Max(0f, l.ArcHeight);
                p.IsReflected = l.IsReflected;
                p.SuppressImpactFx = l.SuppressImpactFx;
                p.OnHit = l.OnHit;
                p.TargetHeight = l.TargetHeight > 0f ? l.TargetHeight : (p.TargetsPlayer || p.Mode == ProjectileMode.OwnPlayer ? PlayerHitHeight : (p.Mode == ProjectileMode.Point ? 0.2f : UnitHitHeight));
                p.StartDistance = -1f; // 첫 틱에 측정
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
                switch (p.Mode)
                {
                    case ProjectileMode.Point:
                        target = p.TargetPoint;
                        break;
                    case ProjectileMode.OwnPlayer:
                        target = sim.GetPlayerPosition(p.Team);
                        break;
                    default:
                        if (p.TargetsPlayer)
                        {
                            // 회피 가능 탄은 조준한 열로, 회피 불가 탄(반사 등)은 지금 서 있는 열로 따라간다
                            target = sim.Arena.PlayerPosition(enemy, p.Dodgeable ? p.WorldColumn : sim.GetPlayerWorldColumn(enemy));
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
                        break;
                }

                Vector2 delta = target - p.Position;
                float dist = delta.magnitude;
                if (p.StartDistance < 0f) p.StartDistance = Mathf.Max(0.01f, dist);
                float step = p.Speed * dt;
                if (dist <= step + 0.05f)
                {
                    p.Position = target;
                    p.Height = p.TargetHeight;
                    p.Active = false;
                    Arrive(sim, p, enemy);
                    p.OnHit = null;
                }
                else
                {
                    p.Position += delta * (step / dist);
                    float t = Mathf.Clamp01(1f - (dist - step) / p.StartDistance);
                    p.Height = Mathf.Lerp(p.StartHeight, p.TargetHeight, t) + p.ArcHeight * 4f * t * (1f - t);
                }
            }
        }

        private static void Arrive(MatchSimulation sim, ProjectileState p, Team enemy)
        {
            if (!p.SuppressImpactFx) sim.PublishFx(p.SpellId, VfxPart.Impact, p.Position, p.Height, p.WorldColumn);

            var arrival = new ProjectileArrival
            {
                SpellId = p.SpellId,
                CasterTeam = p.Team,
                WorldColumn = p.WorldColumn,
                Position = p.Position,
                Height = p.Height,
                Damage = p.Damage,
                FocusBonus = p.FocusBonus,
            };

            if (p.Mode != ProjectileMode.Auto)
            {
                p.OnHit?.OnProjectileArrived(sim, arrival);
                return;
            }
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
                IsReflected = p.IsReflected,
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
            arrival.TargetId = ctx.TargetId;
            arrival.AppliedDamage = sim.Resolver.ResolveDamage(ctx);
            p.OnHit?.OnProjectileArrived(sim, arrival);
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
