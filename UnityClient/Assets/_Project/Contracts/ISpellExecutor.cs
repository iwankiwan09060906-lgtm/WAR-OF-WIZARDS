// §25.1 계약서(Contracts) — 제공자: ㅊㄱㅇ, 사용자: ㅈㅈㅇ, 개발 중 Mock: MockSpellExecutor
// §14 스킬 시스템 — 19종 / §14.2 Executor 7종

namespace SpellboundVR.Contracts
{
    /// <summary>
    /// Executor 7종(Projectile/Area/Beam/Summon/Structure/Buff/Global)의 공통 인터페이스(§14.2).
    /// 스킬 코드는 PC 서버 빌드에서 실행되며, 플레이어와 봇이 같은 코드를 사용한다(§14.1).
    /// HP는 직접 바꾸지 않고 IDamageResolver만 호출한다.
    /// </summary>
    public interface ISpellExecutor
    {
        /// <summary>ServerSpellValidator 통과 후 서버에서 호출되는 스킬 동작 실행 진입점(§7 전투 흐름)</summary>
        void Execute(in SpellCastRequest request, Team casterTeam);
    }
}
