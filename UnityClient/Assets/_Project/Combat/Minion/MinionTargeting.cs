// §16 Detect 단계 — 할당 없는 타겟 탐색 (0.25초 간격 틱)
// 우선순위: (Brute) 구조물 → 가까운 적 유닛 → 구조물 → (돌파 후) 상대 플레이어
// §11 규칙: 넥서스는 자기 타워가 살아있는 동안 대상 아님(Nexus Lock), 플레이어는 상대 넥서스 파괴 후에만.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public static class MinionTargeting
    {
        public const float RetargetInterval = 0.25f;

        public static int Acquire(MatchSimulation sim, UnitState unit)
        {
            Team enemy = TeamUtil.Opponent(unit.Team);
            float detect = unit.Def.DetectRange;

            // 구조물 후보
            int structureId = 0;
            StructureState tower = sim.Defense.Tower(enemy);
            StructureState nexus = sim.Defense.Nexus(enemy);
            if (tower.Alive && Vector2.Distance(unit.Position, tower.Position) - tower.Radius <= detect)
                structureId = tower.Id;
            else if (!tower.Alive && nexus.Alive && Vector2.Distance(unit.Position, nexus.Position) - nexus.Radius <= detect)
                structureId = nexus.Id;

            if (unit.Def.PrefersStructures && structureId != 0) return structureId;

            // 가장 가까운 적 유닛
            int bestUnit = 0;
            float bestSq = detect * detect;
            var units = sim.Units;
            for (int i = 0; i < units.Length; i++)
            {
                var o = units[i];
                if (!o.Active || o.Dead || o.Team != enemy) continue;
                float sq = (o.Position - unit.Position).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    bestUnit = o.Id;
                }
            }
            if (bestUnit != 0) return bestUnit;
            if (structureId != 0) return structureId;

            // §11.1 돌파: 상대 넥서스 파괴 후 상대 플레이어
            if (sim.Defense.IsBreached(enemy) && sim.GetPlayer(enemy).Alive) return TeamUtil.PlayerId(enemy);

            return 0;
        }
    }
}
