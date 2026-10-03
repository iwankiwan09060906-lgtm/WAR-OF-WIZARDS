// §12.5 서버 데미지 처리 순서 (변경 금지)
//   ① 타겟 규칙 검증 (§11)             ─ 불가 → 0
//   ② 넥서스 Lock 검증                  ─ 무적 → 0
//   ③ 회피 판정 (투사체 · 레이저 · 장판)  ─ 회피 → 0
//   ④ 명중률 판정 (플레이어 스킬만)       ─ MISS → 0
//   ⑤ 기본 데미지 × TeamAttackMultiplier × 집중 보너스
//   ⑥ 대상이 플레이어면 × (대상 타워 생존 ? 0.2 : 1.0)
//   ⑦ 대상 방어 효과: 반사 결계(무효 + 반사) → 쉴드 흡수
//   ⑧ HP 적용 → 0 이하면 승패 판정
// 모든 스킬 · 유닛 · 타워 코드는 HP를 직접 바꾸지 않고 이 클래스만 호출한다(Rule 8).

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Network
{
    public sealed class ServerDamageResolver : IDamageResolver
    {
        private readonly ICombatWorld _world;
        private readonly ITargetRules _rules;
        private readonly DefenseLayerState _defense;
        private readonly StatusEffectHost _status;
        private readonly AccuracyRoll _accuracy;
        private readonly MatchRuleConfig _config;

        public ServerDamageResolver(ICombatWorld world, ITargetRules rules, DefenseLayerState defense,
                                    StatusEffectHost status, AccuracyRoll accuracy, MatchRuleConfig config)
        {
            _world = world;
            _rules = rules;
            _defense = defense;
            _status = status;
            _accuracy = accuracy;
            _config = config;
        }

        public float ResolveDamage(in DamageContext context)
        {
            if (context.BaseDamage <= 0f) return 0f;
            if (!_world.TryGetTargetInfo(context.TargetId, out Team targetTeam, out TargetKind targetKind, out Vector2 pos))
                return 0f;
            if (targetTeam == context.AttackerTeam) return 0f;

            // ① 타겟 규칙 (§11) — 호출자가 넘긴 TargetKind가 아니라 서버 실제 종류로 검증
            if (!_rules.IsTargetAllowed(context.AttackerKind, targetKind))
            {
                _world.ReportHit(context.TargetId, pos, 0f, HitResultKind.Immune, context);
                return 0f;
            }
            bool attackerIsUnit = context.AttackerKind == TargetKind.Minion || context.AttackerKind == TargetKind.Summon;
            if (attackerIsUnit && targetKind == TargetKind.Player && !_rules.CanUnitAttackPlayer(targetTeam))
                return 0f;

            // ② 넥서스 Lock
            if (targetKind == TargetKind.Nexus && _rules.IsNexusLocked(targetTeam))
            {
                _world.ReportHit(context.TargetId, pos, 0f, HitResultKind.Immune, context);
                return 0f;
            }

            // ③ 회피 — 도착 시점 서버 위치 기준 (Rule 9)
            if (context.IsDodgeable && targetKind == TargetKind.Player &&
                _world.GetPlayerWorldColumn(targetTeam) != context.AimedWorldColumn)
            {
                _world.ReportHit(context.TargetId, pos, 0f, HitResultKind.Dodge, context);
                return 0f;
            }

            // ④ 명중률 (플레이어 스킬만, 대상 1개마다)
            if (context.IsPlayerSkill &&
                !_accuracy.Roll(_world.GetAccuracy(context.AttackerTeam), _world.Tick, context.TargetId))
            {
                _world.ReportHit(context.TargetId, pos, 0f, HitResultKind.Miss, context);
                return 0f;
            }

            // ⑤ 기본 × 진영 배율 × 집중 보너스
            float focus = context.FocusBonusMultiplier > 0f ? context.FocusBonusMultiplier : 1f;
            float damage = context.BaseDamage * _world.GetAttackMultiplier(context.AttackerTeam) * focus;

            // ⑥ 플레이어 대상 방어 계층 (상대 플레이어 스킬 피해만 20%)
            if (targetKind == TargetKind.Player && context.IsPlayerSkill)
                damage *= _defense.SkillDamageRatio(targetTeam, _config);

            // ⑦ 대상 방어 효과 (반사 → 쉴드)
            float beforeEffects = damage;
            damage = _status.AbsorbIncoming(context.TargetId, damage, context);
            if (damage <= 0f)
            {
                _world.ReportHit(context.TargetId, pos, beforeEffects, HitResultKind.Absorbed, context);
                return 0f;
            }

            // ⑧ HP 적용
            float applied = _world.ApplyHpChange(context.TargetId, -damage, context.AttackerTeam);
            _world.ReportHit(context.TargetId, pos, applied, HitResultKind.Damage, context);
            return applied;
        }

        /// <summary>회복 (쉴드 재생 · 넥서스 회복 · 흡혈). 실제 회복량 반환.</summary>
        public float ResolveHeal(int targetId, float amount, Team sourceTeam)
        {
            if (amount <= 0f) return 0f;
            if (!_world.TryGetTargetInfo(targetId, out Team team, out TargetKind kind, out Vector2 pos)) return 0f;
            if (team != sourceTeam) return 0f;
            return _world.ApplyHpChange(targetId, amount, sourceTeam);
        }
    }
}
