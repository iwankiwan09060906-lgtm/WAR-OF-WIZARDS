// §13 덱 · 8슬롯 — 덱의 8장이 게임 내내 같은 슬롯에 고정된다(§13.2).
// SpellId 0 = 빈 슬롯 (19종 미완성 기간 임시 허용, MatchRuleConfig.AllowPartialDeck)

using System;
using UnityEngine;

namespace SpellboundVR.Deck
{
    [Serializable]
    public sealed class DeckData
    {
        public const int SlotCount = 8;

        [SerializeField] private int[] _spellIds = new int[SlotCount];

        public DeckData()
        {
        }

        public DeckData(params int[] spellIds)
        {
            for (int i = 0; i < SlotCount; i++)
                _spellIds[i] = spellIds != null && i < spellIds.Length ? spellIds[i] : 0;
        }

        public int this[int slot]
        {
            get => slot >= 0 && slot < SlotCount ? _spellIds[slot] : 0;
            set { if (slot >= 0 && slot < SlotCount) _spellIds[slot] = value; }
        }

        public int[] ToArray()
        {
            var copy = new int[SlotCount];
            Array.Copy(_spellIds, copy, SlotCount);
            return copy;
        }

        public void CopyFrom(int[] spellIds)
        {
            for (int i = 0; i < SlotCount; i++)
                _spellIds[i] = spellIds != null && i < spellIds.Length ? spellIds[i] : 0;
        }

        public int IndexOf(int spellId)
        {
            if (spellId <= 0) return -1;
            for (int i = 0; i < SlotCount; i++) if (_spellIds[i] == spellId) return i;
            return -1;
        }

        public int FilledCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SlotCount; i++) if (_spellIds[i] > 0) n++;
                return n;
            }
        }

        public override string ToString()
        {
            var parts = new string[SlotCount];
            for (int i = 0; i < SlotCount; i++) parts[i] = Spells.SpellIds.Code(_spellIds[i]);
            return "[" + string.Join(", ", parts) + "]";
        }

        /// <summary>현재 구현된 3종 기본 덱 (S03 / S07 / S08)</summary>
        public static DeckData CreateDefault()
        {
            return new DeckData(Spells.SpellIds.S03_Fireball, Spells.SpellIds.S07_Gatling, Spells.SpellIds.S08_Shield);
        }
    }
}
