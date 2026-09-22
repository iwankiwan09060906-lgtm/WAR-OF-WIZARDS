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
    /// ResolveDamage 호출에 필요한 최소 정보 (초안, §25.1 절차에 따라 팀 합의 후 필드를 확정한다).
    /// </summary>
    public struct DamageContext
    {
        /// <summary>S01~S13 → 1~13, H01~H06 → 101~106</summary>
        public int SpellId;

        public Team AttackerTeam;
        public TargetKind AttackerKind;
        public TargetKind TargetKind;

        /// <summary>대상 NetworkObject/인스턴스 식별자</summary>
        public int TargetId;

        /// <summary>SpellDefinition SO의 기본 피해량(§26)</summary>
        public float BaseDamage;

        /// <summary>§12.5 ④ 명중률 판정은 플레이어 스킬에만 적용된다</summary>
        public bool IsPlayerSkill;

        /// <summary>§12.4 집중(S10) 등으로 이번 공격에만 적용되는 보너스 배율</summary>
        public float FocusBonusMultiplier;
    }
}
