// §25.1 계약서(Contracts) — 제공자: ㅈㅈㅇ, 사용자: ㅊㄱㅇ, 개발 중 Mock: MockDamageResolver
// §12 데미지 파이프라인 & 방어 계층 / §12.5 서버 데미지 처리 순서(변경 금지)

namespace SpellboundVR.Contracts
{
    /// <summary>
    /// §12.5 ①~⑧ 파이프라인 전체를 단일 창구로 실행한다.
    /// 모든 스킬 동작 코드는 HP를 직접 바꾸지 않고 이 인터페이스만 호출한다(§12.5 하단 원칙).
    /// </summary>
    public interface IDamageResolver
    {
        /// <summary>
        /// §12.5 순서(타겟 규칙 → 넥서스 Lock → 회피 → 명중률 → 배율 → 타워 감쇄 → 방어효과 → HP)를
        /// 실행하고 실제로 적용된 데미지를 반환한다. 불가/무적/회피/빗나감이면 0을 반환한다.
        /// </summary>
        float ResolveDamage(in DamageContext context);
    }

    /// <summary>
    /// ResolveDamage 호출에 필요한 정보.
    /// v6.0 초안에 회피(③) · 공격자 식별 필드를 추가했다 — 전원 합의 대상(§25.1).
    /// </summary>
    public struct DamageContext
    {
        /// <summary>S01~S13 → 1~13, H01~H06 → 101~106 (유닛 · 구조물 공격은 0)</summary>
        public int SpellId;

        public Team AttackerTeam;
        public TargetKind AttackerKind;
        public TargetKind TargetKind;

        /// <summary>공격자 엔티티 식별자 (반사 결계 · 흡혈 등에서 사용, 없으면 0)</summary>
        public int AttackerId;

        /// <summary>대상 NetworkObject/인스턴스 식별자</summary>
        public int TargetId;

        /// <summary>SpellDefinition SO의 기본 피해량(§26)</summary>
        public float BaseDamage;

        /// <summary>§12.5 ④ 명중률 판정은 플레이어 스킬에만 적용된다</summary>
        public bool IsPlayerSkill;

        /// <summary>§12.4 집중(S10) 등으로 이번 공격에만 적용되는 보너스 배율 (0이면 1로 간주)</summary>
        public float FocusBonusMultiplier;

        /// <summary>§11.2 회피 가능한 공격(열 지정 투사체 · 레이저 · 장판)인가</summary>
        public bool IsDodgeable;

        /// <summary>
        /// IsDodgeable일 때 공격이 도착한 "월드 열"(0=Home 기준 Left).
        /// 대상 플레이어의 서버 위치가 이 열과 다르면 ③단계에서 회피 처리된다(§11.2, Rule 9).
        /// </summary>
        public int AimedWorldColumn;
    }
}
