// §11 타겟팅 규칙 매트릭스 ★
//
// | 공격자 ↓ \ 대상 →  | 플레이어           | 미니언·소환수 | 설치물 | 타워 | 넥서스              |
// | 플레이어 스킬      | ○ (§12 피해 비율)  | ○            | ✕     | ✕   | ✕                   |
// | 미니언 · 소환수    | 상대 넥서스 파괴 후 ○ | ○          | ○     | ○   | 상대 타워 파괴 후 ○ |
// | 타워              | ✕                  | ○            | ✕     | —   | —                   |
// | 터렛              | ✕                  | ○            | ✕     | ✕   | ✕                   |

using SpellboundVR.Combat;
using SpellboundVR.Contracts;

namespace SpellboundVR.Network
{
    public sealed class TargetRuleValidator : ITargetRules
    {
        private readonly DefenseLayerState _defense;

        public TargetRuleValidator(DefenseLayerState defense)
        {
            _defense = defense;
        }

        public bool IsTargetAllowed(TargetKind attackerKind, TargetKind targetKind)
        {
            switch (attackerKind)
            {
                case TargetKind.Player:
                    // Rule 5: 플레이어 스킬은 구조물에 데미지를 줄 수 없다
                    return targetKind == TargetKind.Player || targetKind == TargetKind.Minion || targetKind == TargetKind.Summon;

                case TargetKind.Minion:
                case TargetKind.Summon:
                    return true; // 플레이어 · 넥서스 조건은 CanUnitAttackPlayer / IsNexusLocked가 추가 검증

                case TargetKind.Tower:
                case TargetKind.Structure:
                    // Rule 6: 타워 · 터렛은 플레이어를 공격하지 않는다
                    return targetKind == TargetKind.Minion || targetKind == TargetKind.Summon;

                default:
                    return false; // 넥서스는 공격하지 않는다
            }
        }

        /// <summary>Rule 7: 미니언 · 소환수는 상대 넥서스 파괴 전 플레이어를 공격하지 않는다</summary>
        public bool CanUnitAttackPlayer(Team targetTeam)
        {
            return _defense.IsBreached(targetTeam);
        }

        /// <summary>§11.4 · §19 Nexus Lock — 자기 타워가 살아있는 동안 무적</summary>
        public bool IsNexusLocked(Team nexusTeam)
        {
            return _defense.TowerAlive(nexusTeam);
        }
    }
}
