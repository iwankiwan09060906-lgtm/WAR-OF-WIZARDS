// §12.2 진영 공격력 배율 — 해당 진영의 모든 공격(스킬 · 소환수 · 터렛 · 미니언)에 적용
// TeamAttackMultiplier = 1.0 + 0.2(상대 타워 파괴) + 0.4(상대 넥서스 파괴) + 강화 단계 × 단계당 증가량
// 4인 개인전: 구조물 보너스는 구조물을 잃은 그 상대를 칠 때만 붙는다 (다른 상대까지 세지지 않는다).

using SpellboundVR.Contracts;
using SpellboundVR.Core;

namespace SpellboundVR.Combat
{
    public static class TeamAttackMultiplier
    {
        public static float Compute(Team target, DefenseLayerState defense, int scalingStage, MatchRuleConfig rules)
        {
            float m = 1f;
            if (!defense.TowerAlive(target)) m += rules.TowerDestroyedAttackBonus;
            if (!defense.NexusAlive(target)) m += rules.NexusDestroyedAttackBonus;
            m += scalingStage * rules.ScalingAttackPerStage;
            return m;
        }

        /// <summary>구조물 보너스 없이 강화 단계만 (4인 개인전 HUD 표시)</summary>
        public static float ScalingOnly(int scalingStage, MatchRuleConfig rules) => 1f + scalingStage * rules.ScalingAttackPerStage;
    }
}
