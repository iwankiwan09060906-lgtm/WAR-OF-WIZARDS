// §25.1 개발 중 Mock — ISpellExecutor 대역
// 실제 Executor 구현체(ㅊㄱㅇ)가 준비되기 전, ㅈㅈㅇ가 데미지 파이프라인(§12.5)을
// 독립적으로 검증할 때 쓴다. "단순 직선 투사체" 하나로 대체한다(§25.1).

using UnityEngine;

namespace SpellboundVR.Contracts.Mocks
{
    public class MockSpellExecutor : ISpellExecutor
    {
        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            Debug.Log($"[MockSpellExecutor] Team {casterTeam} casts Spell {request.SpellId} " +
                      $"→ Column {request.TargetColumn} (단순 직선 투사체로 대체, 실제 Executor 미연결)");
        }
    }
}
