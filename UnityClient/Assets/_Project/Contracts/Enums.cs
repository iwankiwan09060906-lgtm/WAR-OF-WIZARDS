// §25.1 계약서(Contracts) — 제공자: ㅈㅈㅇ(관리), 전원 합의 후에만 변경(§29)
// §26 권장 데이터 구조 기반. 여기 없는 값은 전부 SO(ScriptableObject)로 노출한다(§3.3).

namespace SpellboundVR.Contracts
{
    /// <summary>전장 좌/중/우 3열 (§5 전장 좌표계)</summary>
    public enum Column : byte
    {
        Left = 0,
        Center = 1,
        Right = 2,
    }

    /// <summary>스킬 등급 — 일반(쿨타임 5초) / 고위력(쿨타임 20초, 게임당 3회) (§13.4, §13.5)</summary>
    public enum SpellTier : byte
    {
        Normal = 0,
        HighPower = 1,
    }

    /// <summary>스킬 조준 방식 (§6.3, §14.2 Executor 7종과 1:1 대응)</summary>
    public enum SpellTargeting : byte
    {
        Column,
        Area,
        Beam,
        Summon,
        Structure,
        Self,
        Tower,
        Global,
    }

    /// <summary>8슬롯 상태 (§13.3)</summary>
    public enum SlotState : byte
    {
        /// <summary>사용 가능, $P+ 후보</summary>
        Ready,

        /// <summary>쿨타임 진행 중</summary>
        Cooldown,

        /// <summary>고위력 3회 소진</summary>
        Exhausted,

        /// <summary>조건 불충족 (예: 타워 파괴 후 타워 버프)</summary>
        Disabled,
    }

    /// <summary>매칭 상대 종류 (§22.1)</summary>
    public enum OpponentType : byte
    {
        Human,
        Bot,
    }

    /// <summary>경기 종료 사유 (§10 승패 조건)</summary>
    public enum MatchEndReason : byte
    {
        PlayerDeath,
        Afk,
        Disconnect,
        Draw,
        Void,
    }

    /// <summary>
    /// 진영(팀) 식별자.
    /// §26 원본 코드에는 없으나 IGameEvents(§25.2) 시그니처(team, ...)를 타입 안전하게
    /// 표현하기 위해 추가한 항목이다. 전원 합의 시 확정한다.
    /// </summary>
    public enum Team : byte
    {
        Home = 0,
        Away = 1,
    }

    /// <summary>
    /// 경기 결과. IGameEvents.OnMatchEnded(result, reason, duration)(§25.2)의 result 타입.
    /// 추가 항목이므로 전원 합의 시 확정한다.
    /// </summary>
    public enum MatchResult : byte
    {
        Win,
        Lose,
        Draw,
        Void,
    }

    /// <summary>
    /// VFX 프리팹 Part 규격 (§25.3 VFX 프리팹 규격).
    /// 이름 규칙: VFX_{SkillId}_{Name}_{Part}
    /// </summary>
    public enum VfxPart : byte
    {
        Cast,
        Projectile,
        Impact,
        Field,
        Loop,
        Telegraph,
    }

    /// <summary>
    /// 공격/피격 대상 종류 (§11 타겟팅 규칙 매트릭스).
    /// ITargetRules / IDamageResolver에서 공용으로 사용한다.
    /// </summary>
    public enum TargetKind : byte
    {
        Player,
        Minion,
        Summon,
        Structure,
        Tower,
        Nexus,
    }
}
