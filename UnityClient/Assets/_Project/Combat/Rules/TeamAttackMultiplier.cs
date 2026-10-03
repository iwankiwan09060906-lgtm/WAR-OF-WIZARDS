// §12.2 진영 공격력 배율 — 해당 진영의 모든 공격(스킬 · 소환수 · 터렛 · 미니언)에 적용
// TeamAttackMultiplier = 1.0 + 0.2(상대 타워 파괴) + 0.4(상대 넥서스 파괴) + 강화 단계 × 단계당 증가량

using SpellboundVR.Contracts;
using SpellboundVR.Core;

namespace SpellboundVR.Combat
{
    public static class TeamAttackMultiplier
    {
        public static float Compute(Team team, DefenseLayerState defense, int scalingStage, MatchRuleConfig rules)
        {
            Team enemy = TeamUtil.Opponent(team);
            float m = 1f;
            if (!defense.TowerAlive(enemy)) m += rules.TowerDestroyedAttackBonus;
            if (!defense.NexusAlive(enemy)) m += rules.NexusDestroyedAttackBonus;
            m += scalingStage * rules.ScalingAttackPerStage;
            return m;
        }
    }
}
