// §13 8슬롯 덱 매니저 (클라이언트 측)
//   · 덱의 8장이 게임 내내 같은 슬롯에 고정된다.
//   · 슬롯 상태(Ready / Cooldown / Exhausted / Disabled)는 서버가 결정하고, 게임 이벤트로 받아 미러링한다.
//   · $P+ 후보(ReadySlotTemplateSet)와 시전 흐름(CastStateMachine)이 이 상태를 읽는다.

using System;
using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Deck
{
    public sealed class EightSlotDeckManager : IDisposable
    {
        private readonly SlotState[] _states = new SlotState[DeckData.SlotCount];
        private readonly float[] _cooldownEnd = new float[DeckData.SlotCount];
        private readonly float[] _cooldownTotal = new float[DeckData.SlotCount];
        private readonly int[] _highPowerUses = new int[DeckData.SlotCount];
        private readonly IGameEvents _events;

        public EightSlotDeckManager(DeckData deck, IGameEvents events)
        {
            Deck = deck;
            _events = events;
            for (int i = 0; i < DeckData.SlotCount; i++)
            {
                _states[i] = SlotState.Disabled; // 서버 상태 수신 전에는 시전 불가
                _highPowerUses[i] = -1;
            }
            _events.OnSlotStateChanged += HandleSlotState;
            _events.OnSlotCooldownStarted += HandleCooldown;
            _events.OnHighPowerUsesChanged += HandleHighPower;
        }

        public DeckData Deck { get; }

        /// <summary>슬롯 상태가 바뀔 때마다 (후보 재구성 트리거)</summary>
        public event Action OnChanged;

        public SlotState GetState(int slot) => slot >= 0 && slot < DeckData.SlotCount ? _states[slot] : SlotState.Disabled;

        public bool IsReady(int slot) => GetState(slot) == SlotState.Ready;

        public int GetSpellId(int slot) => Deck[slot];

        public int FindSlot(int spellId) => Deck.IndexOf(spellId);

        public int GetHighPowerUses(int slot) => slot >= 0 && slot < DeckData.SlotCount ? _highPowerUses[slot] : -1;

        public float GetCooldownRemaining(int slot)
        {
            if (GetState(slot) != SlotState.Cooldown) return 0f;
            return Mathf.Max(0f, _cooldownEnd[slot] - Time.time);
        }

        /// <summary>0 = 막 시작, 1 = 완료</summary>
        public float GetCooldownProgress(int slot)
        {
            if (GetState(slot) != SlotState.Cooldown) return 1f;
            float total = _cooldownTotal[slot];
            if (total <= 0f) return 1f;
            return 1f - Mathf.Clamp01(GetCooldownRemaining(slot) / total);
        }

        public int ReadyCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < DeckData.SlotCount; i++) if (_states[i] == SlotState.Ready) n++;
                return n;
            }
        }

        private void HandleSlotState(int slot, SlotState state)
        {
            if (slot < 0 || slot >= DeckData.SlotCount) return;
            if (_states[slot] == state) return;
            _states[slot] = state;
            OnChanged?.Invoke();
        }

        private void HandleCooldown(int slot, float remaining)
        {
            if (slot < 0 || slot >= DeckData.SlotCount) return;
            _cooldownEnd[slot] = Time.time + remaining;
            _cooldownTotal[slot] = remaining;
        }

        private void HandleHighPower(int slot, int remaining)
        {
            if (slot < 0 || slot >= DeckData.SlotCount) return;
            _highPowerUses[slot] = remaining;
            OnChanged?.Invoke();
        }

        public void Dispose()
        {
            _events.OnSlotStateChanged -= HandleSlotState;
            _events.OnSlotCooldownStarted -= HandleCooldown;
            _events.OnHighPowerUsesChanged -= HandleHighPower;
        }
    }
}
