// §15.1 성능 안전장치 — 진영당 동시 생존 상한 20기 (미니언 + 소환수 + 소환탑 소환수)
// 상한 도달: 웨이브 스킵(누적 금지), 소환 스킬 거절

using SpellboundVR.Contracts;

namespace SpellboundVR.Combat
{
    public static class AliveUnitLimiter
    {
        public static int CountAlive(MatchSimulation sim, Team team)
        {
            int n = 0;
            var units = sim.Units;
            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (u.Active && !u.Dead && u.Team == team) n++;
            }
            return n;
        }

        public static bool CanSpawn(MatchSimulation sim, Team team)
        {
            return CountAlive(sim, team) < sim.Rules.MaxAliveUnitsPerTeam;
        }
    }
}
