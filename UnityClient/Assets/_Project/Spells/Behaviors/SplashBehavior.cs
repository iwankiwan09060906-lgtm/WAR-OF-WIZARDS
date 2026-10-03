// S03 파이어볼 Behavior (§14.3) — 작은 범위 집중 피해
//   · 범위 공격(즉발): 시전 확정 순간 즉시 적용, 회피 불가 (§11.2)
//   · 반경 안 적 유닛 전부 + 반경 안에 서 있는 상대 플레이어 (구조물 제외, Rule 5)

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    public static class SplashBehavior
    {
        /// <summary>플레이어 몸 반경 (m) — 범위 판정 시 더해준다</summary>
        public const float PlayerBodyRadius = 0.5f;

        private static readonly List<int> s_buffer = new List<int>(48);

        public static void Apply(SpellDefinition def, ISpellWorld world, Team casterTeam, Vector2 center, int worldColumn)
        {
            Team enemy = casterTeam == Team.Home ? Team.Away : Team.Home;

            world.PublishFx(def.SpellId, VfxPart.Impact, center, 0f, worldColumn);

            world.CollectEnemyUnitsInRadius(casterTeam, center, def.Radius, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                int id = s_buffer[i];
                if (!world.TryGetUnitInfo(id, out _, out TargetKind kind, out _)) continue;
                var ctx = new DamageContext
                {
                    SpellId = def.SpellId,
                    AttackerTeam = casterTeam,
                    AttackerKind = TargetKind.Player,
                    AttackerId = world.GetPlayerId(casterTeam),
                    TargetKind = kind,
                    TargetId = id,
                    BaseDamage = def.BaseDamage,
                    IsPlayerSkill = true,
                    FocusBonusMultiplier = 1f,
                    IsDodgeable = false,
                };
                world.Damage.ResolveDamage(ctx);
            }

            if (world.IsPlayerAlive(enemy))
            {
                Vector2 enemyPos = world.GetPlayerPosition(enemy);
                if ((enemyPos - center).sqrMagnitude <= (def.Radius + PlayerBodyRadius) * (def.Radius + PlayerBodyRadius))
                {
                    var ctx = new DamageContext
                    {
                        SpellId = def.SpellId,
                        AttackerTeam = casterTeam,
                        AttackerKind = TargetKind.Player,
                        AttackerId = world.GetPlayerId(casterTeam),
                        TargetKind = TargetKind.Player,
                        TargetId = world.GetPlayerId(enemy),
                        BaseDamage = def.BaseDamage,
                        IsPlayerSkill = true,
                        FocusBonusMultiplier = 1f,
                        IsDodgeable = false,
                    };
                    world.Damage.ResolveDamage(ctx);
                }
            }
        }
    }
}
