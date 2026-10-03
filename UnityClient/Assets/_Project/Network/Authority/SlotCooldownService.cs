// §13.3 슬롯 상태 · §13.4 쿨타임 · §13.5 고위력
//   · 스킬별 독립 쿨타임, 서버에서 시전이 확정된 순간부터 시작
//   · 취소 · 거절된 시전은 쿨타임 미소모 (Consume는 실행 직전에만 호출)
//   · 고위력: 게임당 3회, 소진 시 Exhausted

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Spells;

namespace SpellboundVR.Network
{
    public static class SlotCooldownService
    {
        public static void Initialize(PlayerState player, int[] deck, SpellCatalog catalog, MatchRuleConfig rules)
        {
            for (int i = 0; i < DeckData.SlotCount; i++)
            {
                int id = deck != null && i < deck.Length ? deck[i] : 0;
                ref SlotRuntime s = ref player.Slots[i];
                s.SpellId = id;
                s.CooldownEndTime = 0f;
                s.CooldownDuration = 0f;
                SpellDefinition def = id > 0 ? catalog.Get(id) : null;
                s.IsHighPower = def != null && def.Tier == SpellTier.HighPower;
                s.HighPowerUsesLeft = s.IsHighPower ? rules.HighPowerUsesPerGame : 0;
                s.State = def != null ? SlotState.Ready : SlotState.Disabled;
            }
        }

        /// <summary>쿨타임 종료 슬롯을 Ready로 되돌린다.</summary>
        public static void Update(PlayerState player, float now)
        {
            for (int i = 0; i < DeckData.SlotCount; i++)
            {
                ref SlotRuntime s = ref player.Slots[i];
                if (s.State == SlotState.Cooldown && now >= s.CooldownEndTime)
                    s.State = SlotState.Ready;
            }
        }

        public static bool IsReady(PlayerState player, int slot)
        {
            return slot >= 0 && slot < DeckData.SlotCount && player.Slots[slot].State == SlotState.Ready;
        }

        /// <summary>시전 확정 → 쿨타임 시작 · 고위력 횟수 차감</summary>
        public static void Consume(PlayerState player, int slot, float now, MatchRuleConfig rules, SpellTier tier, int scalingStage)
        {
            ref SlotRuntime s = ref player.Slots[slot];
            float cd = rules.GetCooldown(tier, scalingStage);
            s.CooldownDuration = cd;
            s.CooldownEndTime = now + cd;
            if (s.IsHighPower)
            {
                s.HighPowerUsesLeft--;
                if (s.HighPowerUsesLeft <= 0)
                {
                    s.HighPowerUsesLeft = 0;
                    s.State = SlotState.Exhausted;
                    return;
                }
            }
            s.State = SlotState.Cooldown;
        }

        /// <summary>조건 불충족 (예: S09 타워 파괴 후) 슬롯 비활성</summary>
        public static void SetDisabled(PlayerState player, int slot, bool disabled)
        {
            ref SlotRuntime s = ref player.Slots[slot];
            if (disabled)
            {
                if (s.State != SlotState.Exhausted) s.State = SlotState.Disabled;
            }
            else if (s.State == SlotState.Disabled && s.SpellId > 0)
            {
                s.State = SlotState.Ready;
            }
        }
    }
}
