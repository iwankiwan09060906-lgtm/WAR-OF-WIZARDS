// §14.2 AreaExecutor — 조준: 열 + 깊이 / 스킬: S02 독 장판, S03 파이어볼, S04 화살 세례, S06 번개
// 현재 구현: S03 파이어볼 (SplashBehavior). 서버가 Column / Depth로 지점을 계산한다(§7).

using SpellboundVR.Contracts;
using SpellboundVR.Core;
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
            int worldColumn = TeamUtil.ToWorldColumn(casterTeam, request.TargetColumn);
            float depth = Mathf.Clamp(request.TargetDepth, 0f, _def.MaxDepth);
            var arena = _world.Arena;
            var center = new Vector2(arena.ColumnU(worldColumn), arena.DepthToV(casterTeam, depth));

            Vector2 casterPos = _world.GetPlayerPosition(casterTeam);
            _world.PublishFx(_def.SpellId, VfxPart.Cast, casterPos, 1.5f, _world.GetPlayerWorldColumn(casterTeam));
            _world.PublishFx(_def.SpellId, VfxPart.Projectile, center, 0f, worldColumn);

            switch (_def.Behavior)
            {
                case SpellBehaviorType.Splash:
                default:
                    SplashBehavior.Apply(_def, _world, casterTeam, center, worldColumn);
                    break;
            }
        }
    }
}
