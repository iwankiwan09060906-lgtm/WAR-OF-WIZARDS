// §14.2 StructureExecutor — 조준: 설치 구역(열) / 스킬: S13 터렛, H06 소환탑
// §14.4 설치 구역: 내 타워가 있으면 타워 앞, 타워가 파괴됐으면 넥서스 앞 (조준한 열). 진영당 종류별 1기 — 다시 쓰면 기존 것을 교체.
// S10 집중: 설치물 체력 × 집중 배율.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Behaviors;
using UnityEngine;

namespace SpellboundVR.Spells.Executor
{
    public sealed class StructureExecutor : ISpellExecutor
    {
        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public StructureExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            int lane = TeamUtil.ToWorldColumn(casterTeam, request.TargetColumn);
            int existing = _world.FindTeamUnit(casterTeam, _def.SummonKind);
            if (existing != 0) _world.RemoveUnit(existing);

            Vector2 pos = _world.GetPlacementPosition(casterTeam, lane);
            _world.PublishFx(_def.SpellId, VfxPart.Cast, pos, 0.05f, lane);
            if (!_world.SpawnUnit(casterTeam, _def.SummonKind, lane, pos, SpellDamage.HpFocus(_def, _world.CastFocus), out _))
                Debug.LogWarning($"[StructureExecutor] {_def.Code} 설치 실패 (생존 상한 · 풀 부족)");
        }
    }
}
