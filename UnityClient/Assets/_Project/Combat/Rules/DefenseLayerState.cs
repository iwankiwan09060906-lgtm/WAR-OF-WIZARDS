// §12.1 방어 계층
//   타워 ○ / 넥서스 ○ : 상대 스킬 피해 20%, HP 회복 ○, 상대 유닛의 나 공격 ✕
//   타워 ✕ / 넥서스 ○ : 100%, 회복 ○, ✕
//   타워 ✕ / 넥서스 ✕ : 100%, 회복 ✕, 상대 유닛 공격 ○

using SpellboundVR.Contracts;
using SpellboundVR.Core;

namespace SpellboundVR.Combat
{
    public sealed class DefenseLayerState
    {
        private readonly StructureState[] _structures;

        public DefenseLayerState(StructureState[] structuresHomeTowerHomeNexusAwayTowerAwayNexus)
        {
            _structures = structuresHomeTowerHomeNexusAwayTowerAwayNexus;
        }

        public StructureState Tower(Team team) => _structures[(int)team * 2];

        public StructureState Nexus(Team team) => _structures[(int)team * 2 + 1];

        public bool TowerAlive(Team team) => Tower(team).Alive;

        public bool NexusAlive(Team team) => Nexus(team).Alive;

        /// <summary>§12.5 ⑥ 상대 플레이어 스킬이 이 팀 플레이어에게 주는 피해 비율</summary>
        public float SkillDamageRatio(Team defender, MatchRuleConfig rules)
        {
            return TowerAlive(defender) ? rules.TowerAliveSkillDamageRatio : 1f;
        }

        /// <summary>§19 넥서스가 살아있는 동안 10초마다 회복</summary>
        public bool RegenAllowed(Team team) => NexusAlive(team);

        /// <summary>§11.1 넥서스 파괴 후에만 상대 유닛이 이 팀 플레이어를 공격 (돌파)</summary>
        public bool IsBreached(Team team) => !NexusAlive(team);
    }
}
