// §18 타워
//   · 플레이어를 공격하지 않음 (Rule 6), 적 유닛만
//   · Target Priority: Brute → 먼저 진입한 유닛 → 근접 → 원거리
//   · 공격: 초당 약 1.2회, 단일, 연속 타격 보너스

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public static class TowerAI
    {
        public static void Tick(MatchSimulation sim, StructureState tower, float dt)
        {
            if (tower.IsNexus || !tower.Alive) return;

            var rules = sim.Rules;
            Team enemy = TeamUtil.Opponent(tower.Team);
            float now = sim.Time;
            float rangeSq = (rules.TowerRange + tower.Radius) * (rules.TowerRange + tower.Radius);

            // 사거리 진입 시각 갱신 + 우선순위 대상 선택
            int bestId = 0;
            int bestGroup = int.MaxValue;
            float bestEntered = float.MaxValue;
            int bestPriority = int.MaxValue;
            var units = sim.Units;
            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (!u.Active || u.Dead || u.Team != enemy) continue;
                bool inRange = (u.Position - tower.Position).sqrMagnitude <= rangeSq;
                if (!inRange)
                {
                    u.EnteredEnemyTowerRangeTime = -1f;
                    continue;
                }
                if (u.EnteredEnemyTowerRangeTime < 0f) u.EnteredEnemyTowerRangeTime = now;

                int group = u.Kind == MinionKind.Brute ? 0 : 1;
                int priority = u.Def.TowerTargetPriority;
                bool better = group < bestGroup ||
                              (group == bestGroup && u.EnteredEnemyTowerRangeTime < bestEntered - 1e-4f) ||
                              (group == bestGroup && Mathf.Abs(u.EnteredEnemyTowerRangeTime - bestEntered) <= 1e-4f && priority < bestPriority);
                if (better)
                {
                    bestId = u.Id;
                    bestGroup = group;
                    bestEntered = u.EnteredEnemyTowerRangeTime;
                    bestPriority = priority;
                }
            }

            tower.AttackTimer -= dt;
            if (bestId == 0)
            {
                tower.ComboCount = 0;
                tower.LastTargetId = 0;
                if (tower.AttackTimer < 0f) tower.AttackTimer = 0f;
                return;
            }
            if (tower.AttackTimer > 0f) return;

            tower.AttackTimer += 1f / Mathf.Max(0.1f, rules.TowerAttacksPerSecond);
            tower.ComboCount = bestId == tower.LastTargetId ? Mathf.Min(tower.ComboCount + 1, rules.TowerComboMaxStacks) : 0;
            tower.LastTargetId = bestId;

            float dmg = rules.TowerDamage * (1f + tower.ComboCount * rules.TowerComboBonusPerHit);
            var ctx = new DamageContext
            {
                SpellId = 0,
                AttackerTeam = tower.Team,
                AttackerKind = TargetKind.Tower,
                AttackerId = tower.Id,
                TargetKind = TargetKind.Minion,
                TargetId = bestId,
                BaseDamage = dmg,
                IsPlayerSkill = false,
                FocusBonusMultiplier = 1f,
            };
            sim.Resolver.ResolveDamage(ctx);
        }
    }
}
