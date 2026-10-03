// §25.1 개발 중 Mock — IGameEvents 대역
// 서버(ㅈㅈㅇ)가 준비되기 전, ㅈㅇㅈ가 HUD를, ㅊㄱㅇ가 VFX 연결을 독립적으로 개발할 때 쓴다.
// Raise* 메서드로 임의 시점에 가짜 이벤트를 흘려보낼 수 있다.

using System;
using UnityEngine;

namespace SpellboundVR.Contracts.Mocks
{
    public class MockGameEvents : IGameEvents
    {
        public event Action<Team, int, int> OnHpChanged;
        public event Action<Team, Column> OnPositionChanged;
        public event Action<int, SlotState> OnSlotStateChanged;
        public event Action<int, float> OnSlotCooldownStarted;
        public event Action<int, int> OnHighPowerUsesChanged;
        public event Action<string> OnCastRejected;
        public event Action<int, string, bool, float> OnStatusEffectChanged;
        public event Action<Team, bool, bool> OnDefenseLayerChanged;
        public event Action<int, int, int> OnStructureHpChanged;
        public event Action<Team, float> OnAttackMultiplierChanged;
        public event Action<float> OnMatchTimeChanged;
        public event Action<int> OnScalingStageChanged;
        public event Action<float, bool> OnWaveTimerChanged;
        public event Action<float> OnAfkWarning;
        public event Action<Team> OnBreachStarted;
        public event Action<int, VfxPart, Vector3, Column> OnSpellFx;
        public event Action<OpponentType> OnMatchStarted;
        public event Action<MatchResult, MatchEndReason, float> OnMatchEnded;

        public void RaiseHpChanged(Team team, int current, int max) => OnHpChanged?.Invoke(team, current, max);
        public void RaisePositionChanged(Team team, Column column) => OnPositionChanged?.Invoke(team, column);
        public void RaiseSlotStateChanged(int slot, SlotState state) => OnSlotStateChanged?.Invoke(slot, state);
        public void RaiseSlotCooldownStarted(int slot, float duration) => OnSlotCooldownStarted?.Invoke(slot, duration);
        public void RaiseHighPowerUsesChanged(int slot, int remaining) => OnHighPowerUsesChanged?.Invoke(slot, remaining);
        public void RaiseCastRejected(string reason) => OnCastRejected?.Invoke(reason);
        public void RaiseStatusEffectChanged(int target, string effectId, bool active, float remaining) => OnStatusEffectChanged?.Invoke(target, effectId, active, remaining);
        public void RaiseDefenseLayerChanged(Team team, bool towerAlive, bool nexusAlive) => OnDefenseLayerChanged?.Invoke(team, towerAlive, nexusAlive);
        public void RaiseStructureHpChanged(int structureId, int current, int max) => OnStructureHpChanged?.Invoke(structureId, current, max);
        public void RaiseAttackMultiplierChanged(Team team, float value) => OnAttackMultiplierChanged?.Invoke(team, value);
        public void RaiseMatchTimeChanged(float seconds) => OnMatchTimeChanged?.Invoke(seconds);
        public void RaiseScalingStageChanged(int stage) => OnScalingStageChanged?.Invoke(stage);
        public void RaiseWaveTimerChanged(float nextInSeconds, bool isBruteWave) => OnWaveTimerChanged?.Invoke(nextInSeconds, isBruteWave);
        public void RaiseAfkWarning(float secondsLeft) => OnAfkWarning?.Invoke(secondsLeft);
        public void RaiseBreachStarted(Team team) => OnBreachStarted?.Invoke(team);
        public void RaiseSpellFx(int spellId, VfxPart fxPart, Vector3 position, Column column) => OnSpellFx?.Invoke(spellId, fxPart, position, column);
        public void RaiseMatchStarted(OpponentType opponentType) => OnMatchStarted?.Invoke(opponentType);
        public void RaiseMatchEnded(MatchResult result, MatchEndReason reason, float duration) => OnMatchEnded?.Invoke(result, reason, duration);
    }
}
