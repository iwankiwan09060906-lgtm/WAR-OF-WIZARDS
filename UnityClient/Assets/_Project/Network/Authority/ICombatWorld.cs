// ServerDamageResolver가 서버 시뮬레이션 상태에 접근하는 창구 (MatchSimulation이 구현).

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Network
{
    public interface ICombatWorld
    {
        uint Tick { get; }

        float Time { get; }

        /// <summary>살아있는 대상만 true</summary>
        bool TryGetTargetInfo(int targetId, out Team team, out TargetKind kind, out Vector2 position);

        int GetPlayerWorldColumn(Team team);

        float GetAccuracy(Team attackerTeam);

        float GetAttackMultiplier(Team team);

        /// <summary>
        /// HP를 바꾸는 유일한 함수 — ServerDamageResolver만 호출한다(§12.5 ⑧).
        /// delta &lt; 0 이 피해. 실제로 변한 양의 절댓값을 반환한다. 0 이하가 되면 사망 처리.
        /// </summary>
        float ApplyHpChange(int targetId, float delta, Team sourceTeam);

        void ReportHit(int targetId, Vector2 position, float amount, HitResultKind kind, in DamageContext context);
    }
}
