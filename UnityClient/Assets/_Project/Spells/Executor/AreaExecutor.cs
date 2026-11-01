// §14.2 AreaExecutor — 조준: 열 + 깊이 / 스킬: S02 독 장판, S03 파이어볼, S04 화살 세례, S06 번개, H05 분노
// 서버가 Column / Depth로 지점을 계산한다(§7).

using SpellboundVR.Contracts;
using SpellboundVR.Spells.Behaviors;
using UnityEngine;

namespace SpellboundVR.Spells.Executor
{
    public sealed class AreaExecutor : ISpellExecutor
    {
        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public AreaExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            var arena = _world.Arena;
            int worldColumn = arena.ResolveLane(casterTeam, request.TargetColumn);
            float depth = Mathf.Clamp(request.TargetDepth, 0f, _def.MaxDepth);
            var lane = arena.GetLane(casterTeam, worldColumn);
            var center = lane.Point(depth * lane.Length);
            float focus = _world.CastFocus;

            switch (_def.Behavior)
            {
                case SpellBehaviorType.PoisonField:
                    PoisonFlask.Launch(_def, _world, casterTeam, center, worldColumn, focus);
                    break;

                case SpellBehaviorType.FireballLob:
                    FireballLob.Launch(_def, _world, casterTeam, center, worldColumn, focus);
                    break;

                case SpellBehaviorType.ArrowVolley:
                    _world.StartRoutine(new ArrowVolleyRoutine(_def, _world, casterTeam, center, worldColumn, focus));
                    break;

                case SpellBehaviorType.LightningStrikes:
                    _world.StartRoutine(new LightningRoutine(_def, _world, casterTeam, center, worldColumn, focus));
                    break;

                case SpellBehaviorType.RageField:
                    _world.StartRoutine(new RageRoutine(_def, _world, casterTeam, center, worldColumn, focus));
                    break;

                default:
                    _world.PublishFx(_def.SpellId, VfxPart.Cast, _world.GetPlayerPosition(casterTeam), 1.5f, _world.GetPlayerWorldColumn(casterTeam));
                    SplashBehavior.Apply(_def, _world, casterTeam, center, worldColumn, SpellDamage.DamageFocus(_def, focus));
                    break;
            }
        }
    }
}
