// §16 미니언 · 소환수 AI (PC 서버 전용)
//   Spawn → Move Forward → Detect → Attack → Death
//              ↓ (상대 넥서스 파괴 후)
//        Breach Point → 상대 플레이어 공격
// 프리즈 중 이동 · 공격 정지. 최대 생존 90초. 프레임당 할당 0.
// 이동은 아레나 평면(u, v) 위 직선 이동 + 레인 중심 복귀로 처리한다 (NavMesh 대체, 서버 경량화).

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public static class MinionAI
    {
        public static void Tick(MatchSimulation sim, UnitState u, float dt)
        {
            if (!u.Active || u.Dead) return;
            float now = sim.Time;

            if (now - u.SpawnTime > sim.Rules.MinionMaxLifetime)
            {
                u.Dead = true;
                return;
            }

            if (u.FrozenUntil > now)
            {
                u.State = UnitVisualState.Frozen;
                return;
            }

            u.RetargetTimer -= dt;
            if (u.RetargetTimer <= 0f || !sim.IsTargetAliveFor(u, u.TargetId))
            {
                u.RetargetTimer = MinionTargeting.RetargetInterval;
                u.TargetId = MinionTargeting.Acquire(sim, u);
            }

            Team enemy = TeamUtil.Opponent(u.Team);
            var def = u.Def;

            if (u.TargetId != 0 && sim.TryGetTargetBody(u, u.TargetId, out Vector2 targetPos, out float targetRadius))
            {
                bool targetIsPlayer = TeamUtil.IsPlayerId(u.TargetId);
                float reach = def.AttackRange + targetRadius + def.BodyRadius;
                Vector2 delta = targetPos - u.Position;
                float dist = delta.magnitude;

                if (dist > reach)
                {
                    float step = def.MoveSpeed * dt;
                    u.Position += dist > 1e-4f ? delta * (Mathf.Min(step, dist - reach * 0.9f) / dist) : Vector2.zero;
                    u.State = targetIsPlayer ? UnitVisualState.Breaching : UnitVisualState.Moving;
                }
                else
                {
                    u.State = UnitVisualState.Attacking;
                    u.AttackTimer -= dt;
                    if (u.AttackTimer <= 0f)
                    {
                        u.AttackTimer += Mathf.Max(0.1f, def.AttackInterval);
                        Attack(sim, u, enemy, targetIsPlayer);
                    }
                }
            }
            else
            {
                // 전진: 상대 진영 방향 + 레인 중심 복귀
                float forward = sim.Arena.ForwardSign(u.Team);
                float laneU = sim.Arena.ColumnU(u.Lane);
                u.Position.y += forward * def.MoveSpeed * dt;
                u.Position.x = Mathf.MoveTowards(u.Position.x, laneU, def.MoveSpeed * 0.5f * dt);
                u.State = UnitVisualState.Moving;
                u.AttackTimer = Mathf.Min(u.AttackTimer, def.AttackInterval * 0.5f);
            }

            u.Position = sim.Arena.ClampToField(u.Position);
        }

        private static void Attack(MatchSimulation sim, UnitState u, Team enemy, bool targetIsPlayer)
        {
            TargetKind kind;
            if (targetIsPlayer) kind = TargetKind.Player;
            else if (TeamUtil.IsStructureId(u.TargetId)) kind = sim.GetStructureById(u.TargetId).Kind;
            else kind = TargetKind.Minion;

            float dmg = u.Def.Damage * u.AttackMultiplier;
            if (kind == TargetKind.Tower || kind == TargetKind.Nexus || kind == TargetKind.Structure)
                dmg *= u.Def.StructureDamageMultiplier;

            var ctx = new DamageContext
            {
                SpellId = 0,
                AttackerTeam = u.Team,
                AttackerKind = u.TargetKindSelf,
                AttackerId = u.Id,
                TargetKind = kind,
                TargetId = u.TargetId,
                BaseDamage = dmg,
                IsPlayerSkill = false,
                FocusBonusMultiplier = 1f,
                IsDodgeable = false, // §11.2 미니언 · 소환수 공격은 회피 불가
            };
            sim.Resolver.ResolveDamage(ctx);
        }
    }
}
