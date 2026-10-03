// §10 기권(AFK) — 사람 플레이어가 30초 무활동 → 기권패 (봇은 대상 아님)
// 활동 = 룬 드로잉 시작 / 스킬 시전 성공 / 왼손 이동. 20초에 경고, 카운트다운 중 타이머 정지.

using SpellboundVR.Core;

namespace SpellboundVR.Combat
{
    public static class AfkMonitor
    {
        /// <summary>경고 중이면 남은 초, 아니면 -1</summary>
        public static float SecondsLeft(PlayerState player, float matchTime, MatchRuleConfig rules)
        {
            if (!player.IsHuman || !player.Alive) return -1f;
            float idle = matchTime - player.LastActivityTime;
            if (idle < rules.AfkWarningSeconds) return -1f;
            float left = rules.AfkForfeitSeconds - idle;
            return left < 0f ? 0f : left;
        }

        public static bool IsForfeit(PlayerState player, float matchTime, MatchRuleConfig rules)
        {
            if (!player.IsHuman || !player.Alive) return false;
            return matchTime - player.LastActivityTime >= rules.AfkForfeitSeconds;
        }
    }
}
