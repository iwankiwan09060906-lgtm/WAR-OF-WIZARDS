// §14.2 GlobalExecutor — 조준 없음(주먹 즉시 시전) / 스킬: H01 프리즈, H03 흡혈, H05 분노

using SpellboundVR.Contracts;
using SpellboundVR.Spells.Behaviors;

namespace SpellboundVR.Spells.Executor
{
    public sealed class GlobalExecutor : ISpellExecutor
    {
        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public GlobalExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            float focus = _world.CastFocus;
            switch (_def.Behavior)
            {
                case SpellBehaviorType.FreezeAll:
                    FreezeAll.Apply(_def, _world, casterTeam, focus);
                    break;
                case SpellBehaviorType.DrainLife:
                    DrainLife.Apply(_def, _world, casterTeam, focus);
                    break;
                case SpellBehaviorType.RageField:
                    _world.StartRoutine(new RageRoutine(_def, _world, casterTeam, focus));
                    break;
            }
        }
    }
}
