// §25.1 계약서(Contracts) — 제공자: ㅈㅈㅇ(발행), 사용자: ㅈㅇㅈ · ㅊㄱㅇ(VFX), 개발 중 Mock: MockGameEvents
// §25.2 게임 이벤트 목록
//
// HUD(ㅈㅇㅈ)와 VFX 연결(ㅊㄱㅇ)은 게임 로직을 직접 읽지 않고 이 이벤트만 구독한다(§23.2).

using System;
using UnityEngine;

namespace SpellboundVR.Contracts
{
    public interface IGameEvents
    {
        /// <summary>OnHpChanged(team, current, max)</summary>
        event Action<Team, int, int> OnHpChanged;

        /// <summary>OnPositionChanged(team, column)</summary>
        event Action<Team, Column> OnPositionChanged;

        /// <summary>OnSlotStateChanged(slot, state) — Ready/Cooldown/Exhausted/Disabled</summary>
        event Action<int, SlotState> OnSlotStateChanged;

        /// <summary>OnSlotCooldownStarted(slot, duration)</summary>
        event Action<int, float> OnSlotCooldownStarted;

        /// <summary>OnHighPowerUsesChanged(slot, remaining)</summary>
        event Action<int, int> OnHighPowerUsesChanged;

        /// <summary>OnCastRejected(reason)</summary>
        event Action<string> OnCastRejected;

        /// <summary>OnStatusEffectChanged(target, effectId, active, remaining)</summary>
        event Action<int, string, bool, float> OnStatusEffectChanged;

        /// <summary>OnDefenseLayerChanged(team, towerAlive, nexusAlive)</summary>
        event Action<Team, bool, bool> OnDefenseLayerChanged;

        /// <summary>OnStructureHpChanged(structureId, current, max)</summary>
        event Action<int, int, int> OnStructureHpChanged;

        /// <summary>OnAttackMultiplierChanged(team, value)</summary>
        event Action<Team, float> OnAttackMultiplierChanged;

        /// <summary>OnMatchTimeChanged(seconds)</summary>
        event Action<float> OnMatchTimeChanged;

        /// <summary>OnScalingStageChanged(stage) — 5분 강화 단계(§12.3)</summary>
        event Action<int> OnScalingStageChanged;

        /// <summary>OnWaveTimerChanged(nextInSeconds, isBruteWave)</summary>
        event Action<float, bool> OnWaveTimerChanged;

        /// <summary>OnAfkWarning(secondsLeft)</summary>
        event Action<float> OnAfkWarning;

        /// <summary>OnBreachStarted(team) — §11.1 돌파</summary>
        event Action<Team> OnBreachStarted;

        /// <summary>OnSpellFx(spellId, fxPart, position, column) — VFX 재생 트리거(§25.3)</summary>
        event Action<int, VfxPart, Vector3, Column> OnSpellFx;

        /// <summary>OnMatchStarted(opponentType)</summary>
        event Action<OpponentType> OnMatchStarted;

        /// <summary>OnMatchEnded(result, reason, duration)</summary>
        event Action<MatchResult, MatchEndReason, float> OnMatchEnded;
    }
}
