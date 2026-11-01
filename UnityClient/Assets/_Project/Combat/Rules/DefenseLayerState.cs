// §12.1 방어 계층
//   타워 ○ / 넥서스 ○ : 상대 스킬 피해 20%, HP 회복 ○, 상대 유닛의 나 공격 ✕
//   타워 ✕ / 넥서스 ○ : 100%, 회복 ○, ✕
//   타워 ✕ / 넥서스 ✕ : 100%, 회복 ✕, 상대 유닛 공격 ○
// 4인 개인전: 타워는 세 상대로 가는 레인마다 하나씩이고, 그 레인의 상대만 막는다.
//   왼쪽 레인 타워만 무너지면 왼쪽 상대의 스킬만 100% 들어오고, 나머지 두 상대는 감소된 비율 그대로다.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;

namespace SpellboundVR.Combat
{
    public sealed class DefenseLayerState
    {
        private readonly StructureState[] _structures;

        /// <param name="structures">MatchSnapshot 구조물 배열 순서</param>
        public DefenseLayerState(StructureState[] structures)
        {
            _structures = structures;
        }

        /// <summary>그 진영이 column 쪽 레인에 세운 타워 (1:1 은 가운데만 있다)</summary>
        public StructureState Tower(Team team, Column column = Column.Center) => _structures[MatchSnapshot.TowerIndex(team, column)];

        /// <summary>attacker 의 공격을 막아 주는 타워 — 둘을 잇는 레인 위의 team 타워</summary>
        public StructureState TowerFacing(Team team, Team attacker) => Tower(team, TeamUtil.ColumnToward(team, attacker));

        public StructureState Nexus(Team team) => _structures[(int)team * 2 + 1];

        public StructureState Structure(Team team, bool isNexus) => isNexus ? Nexus(team) : Tower(team);

        /// <summary>타워가 하나라도 서 있는가</summary>
        public bool TowerAlive(Team team)
        {
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
                if (Tower(team, (Column)c).Alive) return true;
            return false;
        }

        /// <summary>attacker 쪽 레인의 타워가 서 있는가</summary>
        public bool TowerAlive(Team team, Team attacker) => TowerFacing(team, attacker).Alive;

        /// <summary>무너진 타워가 없는가 (세우지 않은 자리는 세지 않는다)</summary>
        public bool AllTowersStanding(Team team)
        {
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
            {
                var tower = Tower(team, (Column)c);
                if (tower.MaxHp > 0f && !tower.Alive) return false;
            }
            return true;
        }

        public bool NexusAlive(Team team) => Nexus(team).Alive;

        /// <summary>§12.5 ⑥ attacker 의 플레이어 스킬이 이 팀 플레이어에게 주는 피해 비율</summary>
        public float SkillDamageRatio(Team defender, Team attacker, MatchRuleConfig rules)
        {
            return TowerAlive(defender, attacker) ? rules.TowerAliveSkillDamageRatio : 1f;
        }

        /// <summary>§19 넥서스가 살아있는 동안 10초마다 회복</summary>
        public bool RegenAllowed(Team team) => NexusAlive(team);

        /// <summary>§11.1 넥서스 파괴 후에만 상대 유닛이 이 팀 플레이어를 공격 (돌파)</summary>
        public bool IsBreached(Team team) => !NexusAlive(team);
    }
}
