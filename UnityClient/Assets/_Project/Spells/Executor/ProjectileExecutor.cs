// §14.2 ProjectileExecutor — 조준: 열 / 스킬: S01 연막, S07 총난사

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
            float focus = SpellDamage.DamageFocus(_def, _world.CastFocus);
            switch (_def.Behavior)
            {
                case SpellBehaviorType.BlindShot:
                    BlindShot.Launch(_def, _world, casterTeam, worldColumn, _world.CastFocus);
                    break;

                case SpellBehaviorType.MultiShotFrontFirst:
                    _world.StartRoutine(new MultiShotFrontFirst(_def, casterTeam, worldColumn, 0, focus));
                    break;

                default:
                    // 단발 열 투사체: 열 가장 앞 유닛 → 없으면 플레이어
                    _world.StartRoutine(new MultiShotFrontFirst(_def, casterTeam, worldColumn, 1, focus));
                    break;
            }
        }
    }
}
