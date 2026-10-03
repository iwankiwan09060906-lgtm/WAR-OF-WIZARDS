// §19 넥서스가 존재하는 동안 자기 플레이어 10초마다 회복 (넥서스 파괴 시 회복 중단, §12.1)

using SpellboundVR.Contracts;
using SpellboundVR.Core;

namespace SpellboundVR.Combat
{
    public sealed class PlayerHealthRegen
    {
        private readonly float[] _timers = new float[TeamUtil.TeamCount];

        public void Reset()
        {
            for (int i = 0; i < _timers.Length; i++) _timers[i] = 0f;
        }

        public void Tick(MatchSimulation sim, float dt)
        {
            float interval = sim.Rules.NexusRegenInterval;
            if (interval <= 0f) return;
            for (int t = 0; t < TeamUtil.TeamCount; t++)
            {
                var team = (Team)t;
                _timers[t] += dt;
                if (_timers[t] < interval) continue;
                _timers[t] -= interval;

                var player = sim.GetPlayer(team);
                if (!player.Alive || !sim.Defense.RegenAllowed(team)) continue;
                sim.Resolver.ResolveHeal(player.Id, sim.Rules.NexusRegenAmount, team);
            }
        }
    }
}
