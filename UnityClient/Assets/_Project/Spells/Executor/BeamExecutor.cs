// §14.2 BeamExecutor — 조준: 열 (레인 전체) / 스킬: S05 토네이도, H02 레이저 빔, H03 흡혈

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Behaviors;

namespace SpellboundVR.Spells.Executor
{
    public sealed class BeamExecutor : ISpellExecutor
    {
        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public BeamExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            int worldColumn = TeamUtil.ToWorldColumn(casterTeam, request.TargetColumn);
            float focus = _world.CastFocus;
            switch (_def.Behavior)
            {
                case SpellBehaviorType.TornadoLane:
                    _world.StartRoutine(new TornadoRoutine(_def, _world, casterTeam, worldColumn, focus));
                    break;
                case SpellBehaviorType.DrainLife:
                    DrainLife.Apply(_def, _world, casterTeam, worldColumn, focus);
                    break;
                default:
                    _world.StartRoutine(new LaserRoutine(_def, _world, casterTeam, worldColumn, focus));
                    break;
            }
        }
    }
}
