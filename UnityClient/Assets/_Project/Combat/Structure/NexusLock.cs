// §19 넥서스 — 자기 타워 생존 중 무적 (Nexus Lock, 서버 검증)
// 실제 차단은 ServerDamageResolver ②단계에서 TargetRuleValidator.IsNexusLocked로 수행한다.
// 이 클래스는 스냅샷 · 디버그 표시용 조회 함수를 제공한다.

using SpellboundVR.Contracts;

namespace SpellboundVR.Combat
{
    public static class NexusLock
    {
        public static bool IsLocked(DefenseLayerState defense, Team nexusTeam)
        {
            return defense.NexusAlive(nexusTeam) && defense.TowerAlive(nexusTeam);
        }
    }
}
