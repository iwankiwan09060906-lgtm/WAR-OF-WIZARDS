// §25.1 계약서(Contracts) — 제공자: ㅈㅈㅇ, 사용자: ㅊㄱㅇ, 개발 중 Mock: 없음(§12.5 내부에서 사용)
// §11 타겟팅 규칙 매트릭스 / §12.5 서버 데미지 처리 순서 ①②단계

namespace SpellboundVR.Contracts
{
    /// <summary>
    /// §11 타겟팅 규칙 매트릭스 검증. ServerDamageResolver(§12.5)의 ①②단계에서 호출된다.
    /// 이 순서(§12.5)는 변경 금지이며, 모든 스킬 동작 코드는 이 규칙을 우회할 수 없다.
    /// </summary>
    public interface ITargetRules
    {
        /// <summary>
        /// 공격자가 이 대상을 공격할 수 있는가 (§11 매트릭스 ①~③ 규칙).
        /// 예: 플레이어 스킬은 구조물을 공격할 수 없다, 타워/터렛은 플레이어를 공격하지 않는다.
        /// </summary>
        bool IsTargetAllowed(TargetKind attackerKind, TargetKind targetKind);

        /// <summary>
        /// 미니언/소환수가 상대 플레이어를 공격할 수 있는가 — 상대 넥서스 파괴 후에만 true(§11.3, §11.1 돌파).
        /// </summary>
        bool CanUnitAttackPlayer(Team targetTeam);

        /// <summary>넥서스 Lock 검증 — 자기 타워가 살아있는 동안 무적(§11.4, §19)</summary>
        bool IsNexusLocked(Team nexusTeam);
    }
}
