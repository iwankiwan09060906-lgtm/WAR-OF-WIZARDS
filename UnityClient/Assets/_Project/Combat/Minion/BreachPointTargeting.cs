// §11.1 돌파 (Breach) — 상대 넥서스가 파괴되면 아군 유닛은 상대 Commander 발판 앞 돌파 지점까지
// 전진해 상대 플레이어를 공격한다. 회피 불가, 칸과 무관하게 적용된다.

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public static class BreachPointTargeting
    {
        public const float BreachPointRadius = 0.4f;

        /// <summary>유닛 자신의 레인 앞 돌파 지점</summary>
        public static Vector2 GetBreachPoint(MatchSimulation sim, UnitState unit, Team targetTeam)
        {
            return sim.Arena.BreachPoint(targetTeam, unit.Lane, sim.Rules.BreachPointOffset);
        }
    }
}
