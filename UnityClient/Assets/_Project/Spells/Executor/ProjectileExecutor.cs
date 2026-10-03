// §14.2 ProjectileExecutor — 조준: 열 / 스킬: S01 연막, S05 토네이도, S07 총난사, H03 흡혈
// 현재 구현: S07 총난사 (MultiShotFrontFirst). 그 외 Behavior는 단발 열 투사체로 동작한다.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Behaviors;

namespace SpellboundVR.Spells.Executor
{
    public sealed class ProjectileExecutor : ISpellExecutor
    {
        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public ProjectileExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            int worldColumn = TeamUtil.ToWorldColumn(casterTeam, request.TargetColumn);
            switch (_def.Behavior)
            {
                case SpellBehaviorType.MultiShotFrontFirst:
                    _world.StartRoutine(new MultiShotFrontFirst(_def, casterTeam, worldColumn));
                    break;

                default:
                    // 단발 열 투사체: 열 가장 앞 유닛 → 없으면 플레이어
                    _world.StartRoutine(new MultiShotFrontFirst(_def, casterTeam, worldColumn, 1));
                    break;
            }
        }
    }
}
