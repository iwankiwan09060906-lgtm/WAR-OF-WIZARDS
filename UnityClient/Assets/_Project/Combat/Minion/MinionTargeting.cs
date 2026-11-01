// §16 Detect 단계 — 할당 없는 타겟 탐색 (0.25초 간격 틱)
// 우선순위: (Brute) 구조물 → 가까운 적 유닛 → 구조물 → (돌파 후) 상대 플레이어
// §11 규칙: 넥서스는 자기 타워가 살아있는 동안 대상 아님(Nexus Lock), 플레이어는 상대 넥서스 파괴 후에만.
// 4인 개인전: 구조물 · 플레이어는 내 레인 끝의 상대만 노리고, 지나가다 만난 유닛은 누구든 적이다.
//             옆 레인의 끝은 상대 타워에서 비껴 있으므로, 레인 끝에 가까워지면 탐지 범위 밖이어도 상대 구조물로 간다.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public static class MinionTargeting
    {
        public const float RetargetInterval = 0.25f;
        /// <summary>레인 끝에서 이만큼(+ 타워 깊이) 안이면 상대 구조물로 간다 (m)</summary>
        private const float LaneEndMargin = 3f;

        public static int Acquire(MatchSimulation sim, UnitState unit)
        {
            Team enemy = sim.Arena.LaneTarget(unit.Team, unit.Lane);
            float detect = unit.Def.DetectRange;
            if (!sim.Arena.IsDuel)
            {
                var lane = sim.Arena.GetLane(unit.Team, unit.Lane);
                if (lane.Length - lane.Along(unit.Position) <= sim.Arena.TowerDepth + LaneEndMargin) detect = float.MaxValue;
            }

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
            float bestSq = unit.Def.DetectRange * unit.Def.DetectRange;
            var units = sim.Units;
            for (int i = 0; i < units.Length; i++)
            {
                var o = units[i];
                if (!o.Active || o.Dead || o.Team == unit.Team) continue;
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
