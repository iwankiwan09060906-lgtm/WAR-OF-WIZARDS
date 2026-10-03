// §15 미니언 웨이브 — 10초마다 각 진영 Melee ×2 + Ranged ×1, 3번째 웨이브마다 Brute ×1 (waveIndex % 3 == 0)
// 유닛은 스폰 시 열(레인)을 배정받는다. 상한 도달 시 그 진영 웨이브는 스킵(누적 금지).

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed class WaveSpawner
    {
        private float _timer;

        public int WaveIndex { get; private set; }

        public float NextWaveIn => _timer < 0f ? 0f : _timer;

        public void Reset(Core.MatchRuleConfig rules)
        {
            WaveIndex = 0;
            _timer = rules.FirstWaveDelay;
        }

        public bool NextIsBrute(Core.MatchRuleConfig rules)
        {
            return rules.BruteEveryNWaves > 0 && (WaveIndex + 1) % rules.BruteEveryNWaves == 0;
        }

        public void Tick(MatchSimulation sim, float dt)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            _timer += Mathf.Max(1f, sim.Rules.WaveInterval);
            WaveIndex++;
            SpawnWave(sim, Team.Home);
            SpawnWave(sim, Team.Away);
        }

        private void SpawnWave(MatchSimulation sim, Team team)
        {
            var rules = sim.Rules;
            if (!AliveUnitLimiter.CanSpawn(sim, team))
            {
                Debug.Log($"[WaveSpawner] {team} 웨이브 {WaveIndex} 스킵 — 생존 상한 {rules.MaxAliveUnitsPerTeam}");
                return;
            }

            int laneBase = WaveIndex % 3;
            int order = 0;
            for (int i = 0; i < rules.MeleePerWave; i++, order++)
                TrySpawn(sim, team, MinionKind.Melee, (laneBase + order) % 3, order);
            for (int i = 0; i < rules.RangedPerWave; i++, order++)
                TrySpawn(sim, team, MinionKind.Ranged, (laneBase + order) % 3, order);
            if (rules.BruteEveryNWaves > 0 && WaveIndex % rules.BruteEveryNWaves == 0)
                TrySpawn(sim, team, MinionKind.Brute, (WaveIndex / rules.BruteEveryNWaves) % 3, order);
        }

        private static void TrySpawn(MatchSimulation sim, Team team, MinionKind kind, int lane, int order)
        {
            if (!AliveUnitLimiter.CanSpawn(sim, team)) return;
            Vector2 pos = sim.Arena.SpawnPosition(team, lane);
            pos.x += ((order % 2 == 0) ? -0.4f : 0.4f);
            sim.SpawnUnit(team, sim.GetMinionDefinition(kind), lane, pos);
        }
    }
}
