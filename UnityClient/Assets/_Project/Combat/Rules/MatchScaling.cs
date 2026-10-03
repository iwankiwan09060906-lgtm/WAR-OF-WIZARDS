// §12.3 5분 강화 — 경기 시간 5:00, 10:00 … 마다 양측 모두 영구 강화

using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed class MatchScaling
    {
        public int Stage { get; private set; }

        /// <summary>테스트 시나리오(§20.3 F키)로 앞당긴 단계 수</summary>
        public int ForcedStages { get; private set; }

        public void Reset()
        {
            Stage = 0;
            ForcedStages = 0;
        }

        /// <summary>단계가 바뀌면 true</summary>
        public bool Update(float matchTime, MatchRuleConfig rules)
        {
            int natural = rules.ScalingInterval > 0f ? Mathf.FloorToInt(matchTime / rules.ScalingInterval) : 0;
            int next = natural + ForcedStages;
            if (next == Stage) return false;
            Stage = next;
            return true;
        }

        public float NextStageIn(float matchTime, MatchRuleConfig rules)
        {
            if (rules.ScalingInterval <= 0f) return -1f;
            float elapsedInStage = matchTime % rules.ScalingInterval;
            return rules.ScalingInterval - elapsedInStage;
        }

        public void ForceNextStage()
        {
            ForcedStages++;
        }
    }
}
