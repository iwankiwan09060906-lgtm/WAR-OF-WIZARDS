// §20.3 F1 ~ 테스트 시나리오 — 특정 스킬 · 특정 상황을 재현하는 테스트 도구 (개발 빌드에서만 동작)
//   F1 : Home 타워 즉시 파괴        F2 : Away 타워 즉시 파괴
//   F3 : Home 넥서스 즉시 파괴(돌파)  F4 : Away 넥서스 즉시 파괴(돌파)
//   F5 : Home HP 1                  F6 : Away HP 1
//   F7 : 5분 강화 즉시 발동          F8 : 양측 쿨타임 즉시 초기화
// 키 배치 문서: Docs/Bot/ManualControlKeys.md

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Server
{
    public sealed class ServerTestScenarios : MonoBehaviour
    {
        private System.Func<MatchSimulation> _simProvider;

        public void Initialize(System.Func<MatchSimulation> simProvider)
        {
            _simProvider = simProvider;
        }

        private void Update()
        {
            if (!Debug.isDebugBuild || _simProvider == null) return;
            var sim = _simProvider();
            if (sim == null) return;

            if (KeyInput.Down(Key.F1)) Run("Home 타워 파괴", () => sim.DebugDestroyStructure(Team.Home, false));
            if (KeyInput.Down(Key.F2)) Run("Away 타워 파괴", () => sim.DebugDestroyStructure(Team.Away, false));
            if (KeyInput.Down(Key.F3)) Run("Home 넥서스 파괴", () => sim.DebugDestroyStructure(Team.Home, true));
            if (KeyInput.Down(Key.F4)) Run("Away 넥서스 파괴", () => sim.DebugDestroyStructure(Team.Away, true));
            if (KeyInput.Down(Key.F5)) Run("Home HP 1", () => sim.DebugSetHp(Team.Home, 1f));
            if (KeyInput.Down(Key.F6)) Run("Away HP 1", () => sim.DebugSetHp(Team.Away, 1f));
            if (KeyInput.Down(Key.F7)) Run("5분 강화 즉시 발동", sim.DebugForceScalingStage);
            if (KeyInput.Down(Key.F8))
                Run("쿨타임 초기화", () =>
                {
                    sim.DebugResetCooldowns(Team.Home);
                    sim.DebugResetCooldowns(Team.Away);
                });
        }

        private static void Run(string label, System.Action action)
        {
            Debug.Log("[ServerTestScenarios] " + label);
            action();
        }
    }
}
