// 스킬 Behavior 공용 — 피해 요청 · 범위 판정 · S10 집중 규칙.
// 피해는 전부 IDamageResolver(§12.5)를 통과한다. 여기서는 DamageContext만 만든다.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    public static class SpellDamage
    {
        /// <summary>플레이어 몸 반경 (m) — 범위 판정 시 더해준다</summary>
        public const float PlayerBodyRadius = 0.5f;

        public static float HitUnit(ISpellWorld world, int spellId, Team casterTeam, int unitId, float damage, float focus)
        {
            if (!world.TryGetUnitInfo(unitId, out _, out TargetKind kind, out _)) return 0f;
            var ctx = new DamageContext
            {
                SpellId = spellId,
                AttackerTeam = casterTeam,
                AttackerKind = TargetKind.Player,
                AttackerId = world.GetPlayerId(casterTeam),
                TargetKind = kind,
                TargetId = unitId,
                BaseDamage = damage,
                IsPlayerSkill = true,
                FocusBonusMultiplier = focus,
                IsDodgeable = false,
            };
            return world.Damage.ResolveDamage(ctx);
        }

        /// <param name="dodgeable">true면 리졸버 ③이 aimedWorldColumn과 상대의 서버 열을 비교한다</param>
        public static float HitEnemyPlayer(ISpellWorld world, int spellId, Team casterTeam, float damage, float focus,
                                           bool dodgeable = false, int aimedWorldColumn = 0)
        {
            Team enemy = TeamUtil.Opponent(casterTeam);
            if (!world.IsPlayerAlive(enemy)) return 0f;
            var ctx = new DamageContext
            {
                SpellId = spellId,
                AttackerTeam = casterTeam,
                AttackerKind = TargetKind.Player,
                AttackerId = world.GetPlayerId(casterTeam),
                TargetKind = TargetKind.Player,
                TargetId = world.GetPlayerId(enemy),
                BaseDamage = damage,
                IsPlayerSkill = true,
                FocusBonusMultiplier = focus,
                IsDodgeable = dodgeable,
                AimedWorldColumn = aimedWorldColumn,
            };
            return world.Damage.ResolveDamage(ctx);
        }

        public static bool EnemyPlayerInRadius(ISpellWorld world, Team casterTeam, Vector2 center, float radius)
        {
            Team enemy = TeamUtil.Opponent(casterTeam);
            if (!world.IsPlayerAlive(enemy)) return false;
            float r = radius + PlayerBodyRadius;
            return (world.GetPlayerPosition(enemy) - center).sqrMagnitude <= r * r;
        }

        // ── S10 집중 규칙: 공격 → 피해, 방어 → 지속 시간, 소환 · 설치 → 체력 ─────────

        public static float DamageFocus(SpellDefinition def, float focus) =>
            def.FocusCategory == SpellFocusCategory.Attack ? Mathf.Max(1f, focus) : 1f;

        public static float DurationFocus(SpellDefinition def, float focus) =>
            def.FocusCategory == SpellFocusCategory.Defense ? Mathf.Max(1f, focus) : 1f;

        public static float HpFocus(SpellDefinition def, float focus) =>
            def.FocusCategory == SpellFocusCategory.Summon ? Mathf.Max(1f, focus) : 1f;

        /// <summary>시전자 앞 손 높이 (투사체 출발점)</summary>
        public static Vector2 Muzzle(ISpellWorld world, Team team, float forward = 0.6f)
        {
            Vector2 p = world.GetPlayerPosition(team);
            p.y += world.Arena.ForwardSign(team) * forward;
            return p;
        }

        /// <summary>결정적 난수 (서버 틱 기반 재현, 할당 없음)</summary>
        public static float Hash01(uint seed)
        {
            seed ^= seed >> 16;
            seed *= 0x7feb352dU;
            seed ^= seed >> 15;
            seed *= 0x846ca68bU;
            seed ^= seed >> 16;
            return (seed & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
