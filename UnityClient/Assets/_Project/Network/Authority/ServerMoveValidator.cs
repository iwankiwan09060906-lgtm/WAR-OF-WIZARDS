// §6.5 왼손 이동 — 서버 확정. 연타 방지 재입력 제한, 끝 칸 바깥 방향 입력 무시.

using SpellboundVR.Combat;
using SpellboundVR.Core;

namespace SpellboundVR.Network
{
    public static class ServerMoveValidator
    {
        /// <summary>허용되면 true와 새 월드 열을 반환한다.</summary>
        public static bool Validate(PlayerState player, int worldDirection, float now, MatchRuleConfig rules, out int newWorldColumn)
        {
            newWorldColumn = player.WorldColumn;
            if (!player.Alive) return false;
            if (worldDirection == 0) return false;
            if (now - player.LastMoveTime < rules.MoveCooldownSeconds) return false;

            int target = player.WorldColumn + worldDirection;
            if (target < 0 || target > 2) return false;

            newWorldColumn = target;
            return true;
        }
    }
}
