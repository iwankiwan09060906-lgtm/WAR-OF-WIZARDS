// §25.1 개발 중 Mock — IDamageResolver 대역
// 실제 ServerDamageResolver(ㅈㅈㅇ)가 준비되기 전, ㅊㄱㅇ가 스킬 동작 코드를 독립적으로
// 개발·테스트할 때 쓴다. §11/§12 규칙을 검증하지 않고 BaseDamage를 그대로 적용한다.

using UnityEngine;

namespace SpellboundVR.Contracts.Mocks
{
    public class MockDamageResolver : IDamageResolver
    {
        public float ResolveDamage(in DamageContext context)
        {
            Debug.Log($"[MockDamageResolver] Spell {context.SpellId} → Target {context.TargetId} " +
                      $"({context.TargetKind}): {context.BaseDamage} dmg (타겟 규칙 · 회피 · 명중 검증 없음)");
            return context.BaseDamage;
        }
    }
}
