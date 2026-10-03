// §25.1 계약서(Contracts) — 제공자: ㅈㅈㅇ, 사용자: ㅊㄱㅇ, 개발 중 Mock: MockStatusEffectHost
// §14.2 BuffExecutor / 상태 효과(쉴드·독·빙결 등) 공용 틀

namespace SpellboundVR.Contracts
{
    /// <summary>
    /// "일정 시간 붙어있는 효과"를 붙이고·갱신하고·떼는 공용 틀.
    /// S08 방어 쉴드, S02 독 장판, H01 프리즈 등 상태 효과 계열 스킬이 이 위에서 동작한다.
    /// </summary>
    public interface IStatusEffectHost
    {
        /// <summary>effectId 규칙은 §14.3 스킬 ID 기준(예: "S02_Poison", "H01_Freeze")</summary>
        void ApplyStatusEffect(int targetId, string effectId, float duration);

        void RemoveStatusEffect(int targetId, string effectId);

        bool HasStatusEffect(int targetId, string effectId);
    }
}
