// §25.1 개발 중 Mock — IStatusEffectHost 대역
// 실제 StatusEffectHost(ㅈㅈㅇ)가 준비되기 전, ㅊㄱㅇ가 쉴드/독/빙결 등
// 상태 효과가 붙는 스킬 동작을 독립적으로 개발·테스트할 때 쓴다.

using UnityEngine;

namespace SpellboundVR.Contracts.Mocks
{
    public class MockStatusEffectHost : IStatusEffectHost
    {
        public void ApplyStatusEffect(int targetId, string effectId, float duration) =>
            Debug.Log($"[MockStatusEffectHost] Apply {effectId} → target {targetId} for {duration}s");

        public void RemoveStatusEffect(int targetId, string effectId) =>
            Debug.Log($"[MockStatusEffectHost] Remove {effectId} → target {targetId}");

        public bool HasStatusEffect(int targetId, string effectId) => false;
    }
}
