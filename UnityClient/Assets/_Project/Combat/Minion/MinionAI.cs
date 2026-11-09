// §16 미니언 · 소환수 · 설치물 AI (PC 서버 전용)
//   Spawn → Move Forward → Detect → Attack → Death
//              ↓ (상대 넥서스 파괴 후)
//        Breach Point → 상대 플레이어 공격
// 프리즈(H01) · 토네이도(S05) 중 이동 · 공격 정지. 분노(H05) 중 공격력 · 공격 속도 · 이동 속도 증가. 프레임당 할당 0.
// 설치물(S13 터렛 · H06 소환탑)은 이동하지 않는다. 터렛은 사거리 안 적 미니언 · 소환수만 쏜다 (§11 터렛 행).
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
            var def = u.Def;

            float lifetime = def.Lifetime > 0f ? def.Lifetime : sim.Rules.MinionMaxLifetime;
            if (now - u.SpawnTime > lifetime)
            {
                u.Dead = true;
                return;
            }

            if (u.FrozenUntil > now)
            {
                u.State = UnitVisualState.Frozen;
                return;
            }
            if (u.StunnedUntil > now)
            {
                u.State = UnitVisualState.Stunned;
                return;
            }

            bool raged = u.IsRaged(now);
            float moveMul = raged ? u.RageMove : 1f;
            float attackSpeedMul = raged ? u.RageAttackSpeed : 1f;

            if (u.IsStructure)
            {
                TickStructure(sim, u, dt, attackSpeedMul);
                return;
            }

            u.RetargetTimer -= dt;
            if (u.RetargetTimer <= 0f || !sim.IsTargetAliveFor(u, u.TargetId))
            {
                u.RetargetTimer = MinionTargeting.RetargetInterval;
                u.TargetId = MinionTargeting.Acquire(sim, u);
            }

            if (u.TargetId != 0 && sim.TryGetTargetBody(u, u.TargetId, out Vector2 targetPos, out float targetRadius))
            {
                bool targetIsPlayer = TeamUtil.IsPlayerId(u.TargetId);
                float reach = def.AttackRange + targetRadius + def.BodyRadius;
                Vector2 delta = targetPos - u.Position;
                float dist = delta.magnitude;

                if (dist > reach)
                {
                    float step = def.MoveSpeed * moveMul * dt;
                    u.Position += dist > 1e-4f ? delta * (Mathf.Min(step, dist - reach * 0.9f) / dist) : Vector2.zero;
                    u.State = targetIsPlayer ? UnitVisualState.Breaching : UnitVisualState.Moving;
                }
                else
                {
                    u.State = UnitVisualState.Attacking;
                    u.AttackTimer -= dt * attackSpeedMul;
                    if (u.AttackTimer <= 0f)
                    {
                        u.AttackTimer += Mathf.Max(0.1f, def.AttackInterval);
                        Attack(sim, u, targetIsPlayer, targetPos);
                    }
                }
            }
            else if (u.ReturnLane != -1)
            {
                Detour(sim, u, def.MoveSpeed * moveMul * dt);
            }
            else
            {
                // 전진: 상대 진영 방향 + 레인 중심 복귀
                var lane = sim.Arena.GetLane(u.Team, u.Lane);
                float along = lane.Along(u.Position) + def.MoveSpeed * moveMul * dt;
                float lateral = Mathf.MoveTowards(lane.Lateral(u.Position), 0f, def.MoveSpeed * 0.5f * dt);
                u.Position = lane.Point(Mathf.Min(along, lane.Length), lateral);
                u.State = UnitVisualState.Moving;
                u.AttackTimer = Mathf.Min(u.AttackTimer, def.AttackInterval * 0.5f);
            }

            u.Position = sim.Arena.ClampToField(u.Position);
        }

        /// <summary>4인 개인전 우회 — 예전 레인을 따라 내 발판으로 돌아가고, 새 레인의 내 발판으로 옮긴 뒤 평소처럼 전진한다</summary>
        private static void Detour(MatchSimulation sim, UnitState u, float step)
        {
            Vector2 goal = u.ReturnLane >= 0 ? sim.Arena.GetLane(u.Team, u.ReturnLane).Origin : sim.Arena.GetLane(u.Team, u.Lane).Origin;
            u.Position = Vector2.MoveTowards(u.Position, goal, step);
            u.State = UnitVisualState.Moving;
            u.AttackTimer = Mathf.Min(u.AttackTimer, u.Def.AttackInterval * 0.5f);
            if ((u.Position - goal).sqrMagnitude > 0.04f) return;
            u.ReturnLane = u.ReturnLane >= 0 ? UnitState.Detouring : -1;
        }

        /// <summary>설치물: 소환(H06) · 고정 사격(S13)</summary>
        private static void TickStructure(MatchSimulation sim, UnitState u, float dt, float attackSpeedMul)
        {
            var def = u.Def;
            if (def.SpawnInterval > 0f)
            {
                u.SpawnTimer -= dt;
                if (u.SpawnTimer <= 0f)
                {
                    u.SpawnTimer += def.SpawnInterval;
                    if (AliveUnitLimiter.CanSpawn(sim, u.Team))
                    {
                        Vector2 pos = u.Position + sim.Arena.GetLane(u.Team, u.Lane).Dir * (def.BodyRadius + 0.8f);
                        if (sim.SpawnUnit(u.Team, sim.GetMinionDefinition(def.SpawnKind), u.Lane, pos))
                            u.AttackCount++; // 표시: 소환 연출
                    }
                }
            }

            if (!def.CanAttack)
            {
                u.State = UnitVisualState.Moving; // 대기 (표시: Idle)
                return;
            }

            u.RetargetTimer -= dt;
            if (u.RetargetTimer <= 0f || FindUnit(sim, u.TargetId) == null || !InRange(sim, u, u.TargetId))
            {
                u.RetargetTimer = MinionTargeting.RetargetInterval;
                u.TargetId = AcquireTurretTarget(sim, u);
            }

            if (u.TargetId == 0)
            {
                u.State = UnitVisualState.Moving;
                u.AttackTimer = Mathf.Min(u.AttackTimer, def.AttackInterval * 0.5f);
                return;
            }

            u.State = UnitVisualState.Attacking;
            u.AttackTimer -= dt * attackSpeedMul;
            if (u.AttackTimer > 0f) return;
            u.AttackTimer += Mathf.Max(0.1f, def.AttackInterval);
            u.AttackCount++;

            var target = FindUnit(sim, u.TargetId);
            var ctx = new DamageContext
            {
                SpellId = 0,
                AttackerTeam = u.Team,
                AttackerKind = TargetKind.Structure,
                AttackerId = u.Id,
                TargetKind = target.TargetKindSelf,
                TargetId = target.Id,
                BaseDamage = def.Damage * (u.IsRaged(sim.Time) ? u.RageAttack : 1f),
                IsPlayerSkill = false,
                FocusBonusMultiplier = 1f,
            };
            sim.Resolver.ResolveDamage(ctx);
        }

        private static UnitState FindUnit(MatchSimulation sim, int id) => id != 0 ? sim.FindUnit(id) : null;

        private static bool InRange(MatchSimulation sim, UnitState turret, int targetId)
        {
            var t = sim.FindUnit(targetId);
            if (t == null) return false;
            float r = turret.Def.AttackRange + turret.Def.BodyRadius + t.Def.BodyRadius;
            return (t.Position - turret.Position).sqrMagnitude <= r * r;
        }

        /// <summary>§11 터렛: 적 미니언 · 소환수만 (설치물 · 구조물 · 플레이어 ✕), 가장 가까운 대상</summary>
        private static int AcquireTurretTarget(MatchSimulation sim, UnitState turret)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            var units = sim.Units;
            for (int i = 0; i < units.Length; i++)
            {
                var o = units[i];
                if (!o.Active || o.Dead || o.Team == turret.Team || o.IsStructure) continue;
                float r = turret.Def.AttackRange + turret.Def.BodyRadius + o.Def.BodyRadius;
                float sq = (o.Position - turret.Position).sqrMagnitude;
                if (sq <= r * r && sq < bestSq)
                {
                    bestSq = sq;
                    best = o.Id;
                }
            }
            return best;
        }

        private static void Attack(MatchSimulation sim, UnitState u, bool targetIsPlayer, Vector2 targetPos)
        {
            TargetKind kind;
            if (targetIsPlayer) kind = TargetKind.Player;
            else if (TeamUtil.IsStructureId(u.TargetId)) kind = sim.GetStructureById(u.TargetId).Kind;
            else
            {
                var t = sim.FindUnit(u.TargetId);
                kind = t != null ? t.TargetKindSelf : TargetKind.Minion;
            }

            float dmg = u.Def.Damage * u.AttackMultiplier * (u.IsRaged(sim.Time) ? u.RageAttack : 1f);
            float structureDmg = dmg * u.Def.StructureDamageMultiplier;
            u.AttackCount++;

            var ctx = new DamageContext
            {
                SpellId = 0,
                AttackerTeam = u.Team,
                AttackerKind = u.TargetKindSelf,
                AttackerId = u.Id,
                TargetKind = kind,
                TargetId = u.TargetId,
                BaseDamage = kind == TargetKind.Tower || kind == TargetKind.Nexus || kind == TargetKind.Structure ? structureDmg : dmg,
                IsPlayerSkill = false,
                FocusBonusMultiplier = 1f,
                IsDodgeable = false, // §11.2 미니언 · 소환수 공격은 회피 불가
            };
            sim.Resolver.ResolveDamage(ctx);

            // 범위 공격 (S11 도끼 휘두르기 · S12 화염): 대상 주변 다른 적 유닛도 같은 피해
            if (u.Def.SplashRadius <= 0f) return;
            var units = sim.Units;
            for (int i = 0; i < units.Length; i++)
            {
                var o = units[i];
                if (!o.Active || o.Dead || o.Team == u.Team || o.Id == u.TargetId) continue;
                float r = u.Def.SplashRadius + o.Def.BodyRadius;
                if ((o.Position - targetPos).sqrMagnitude > r * r) continue;
                ctx.TargetId = o.Id;
                ctx.TargetKind = o.TargetKindSelf;
                ctx.BaseDamage = o.IsStructure ? structureDmg : dmg;
                sim.Resolver.ResolveDamage(ctx);
            }
        }
    }
}
