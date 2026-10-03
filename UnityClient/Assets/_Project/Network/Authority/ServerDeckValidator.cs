// §13.1 덱 편성 — 19종 중 8장, 중복 불가, 고위력 0~2장. 서버가 세션 입장 시 재검증한다.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Spells;

namespace SpellboundVR.Network
{
    public static class ServerDeckValidator
    {
        public static bool Validate(int[] deck, SpellCatalog catalog, MatchRuleConfig rules, out string reason)
        {
            reason = null;
            if (deck == null || deck.Length != DeckData.SlotCount)
            {
                reason = "덱은 8슬롯이어야 합니다";
                return false;
            }

            int filled = 0;
            int highPower = 0;
            for (int i = 0; i < deck.Length; i++)
            {
                int id = deck[i];
                if (id == 0) continue;
                if (!catalog.TryGet(id, out var def))
                {
                    reason = $"알 수 없는/미구현 스킬 {SpellIds.Code(id)} (슬롯 {i + 1})";
                    return false;
                }
                for (int j = 0; j < i; j++)
                {
                    if (deck[j] == id)
                    {
                        reason = $"중복 스킬 {SpellIds.Code(id)}";
                        return false;
                    }
                }
                filled++;
                if (def.Tier == SpellTier.HighPower) highPower++;
            }

            if (filled == 0)
            {
                reason = "빈 덱";
                return false;
            }
            if (!rules.AllowPartialDeck && filled != DeckData.SlotCount)
            {
                reason = "덱은 정확히 8장이어야 합니다";
                return false;
            }
            if (highPower > rules.MaxHighPowerInDeck)
            {
                reason = $"고위력은 최대 {rules.MaxHighPowerInDeck}장";
                return false;
            }
            return true;
        }
    }
}
