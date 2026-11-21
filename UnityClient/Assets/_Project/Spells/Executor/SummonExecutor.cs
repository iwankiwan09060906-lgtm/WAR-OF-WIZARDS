// §14.2 SummonExecutor — 조준: 소환 라인(열) / 스킬: S11 도끼 전사(오크), S12 드래곤
// §14.5 소환수는 미니언과 같은 타겟팅 규칙. 진영 생존 상한은 MatchSimulation이 시전 전에 거절한다(쿨타임 미소모).
// S10 집중: 소환수 체력 × 집중 배율.

using SpellboundVR.Contracts;
using SpellboundVR.Spells.Behaviors;
using UnityEngine;

namespace SpellboundVR.Spells.Executor
{
    public sealed class SummonExecutor : ISpellExecutor
    {
        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public SummonExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            int lane = _world.Arena.ResolveLane(casterTeam, request.TargetColumn);
            var arena = _world.Arena;
            Vector2 pos = arena.SpawnPosition(casterTeam, lane);
            if (arena.FilledPolygon)
            {
                // 시연 전장: 조준한 깊이에 내린다 (조준선의 절반까지, 발판 앞보다 가깝게는 안 된다)
                var frame = arena.GetLane(casterTeam, lane);
                float along = Mathf.Clamp(request.TargetDepth * frame.Length, arena.SpawnDepth, frame.Length * arena.PlacementMaxDepth);
                pos = frame.Point(along);
            }
            float hp = SpellDamage.HpFocus(_def, _world.CastFocus);
            if (_def.FromOffMap)
            {
                SummonDrop.Launch(_def, _world, casterTeam, lane, pos, hp);
                return;
            }
            _world.PublishFx(_def.SpellId, VfxPart.Cast, pos, 0.05f, lane);
            if (!_world.SpawnUnit(casterTeam, _def.SummonKind, lane, pos, hp, out _))
                Debug.LogWarning($"[SummonExecutor] {_def.Code} 소환 실패 (생존 상한 · 풀 부족)");
        }
    }
}
