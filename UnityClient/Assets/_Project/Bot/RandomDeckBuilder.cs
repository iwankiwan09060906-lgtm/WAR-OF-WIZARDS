// §20.2 기본 봇 덱 — 게임마다 랜덤 8장, 고위력 0~2장 랜덤
// 현재 구현된(Executor가 있는) 스킬만 후보로 쓴다. 후보가 8장 미만이면 있는 만큼 채운다.

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using SpellboundVR.Utils;

namespace SpellboundVR.Bot
{
    public static class RandomDeckBuilder
    {
        public static int[] Build(SpellCatalog catalog, ISpellExecutorLookup executors, MatchRuleConfig rules,
                                  BotConfig config, DeterministicRandom rng)
        {
            var normals = new List<int>();
            var highs = new List<int>();
            var all = catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d == null || !executors.Has(d.SpellId)) continue;
                if (d.Tier == SpellTier.HighPower) highs.Add(d.SpellId);
                else normals.Add(d.SpellId);
            }
            Shuffle(normals, rng);
            Shuffle(highs, rng);

            int maxHigh = System.Math.Min(System.Math.Min(config.MaxHighPowerCards, rules.MaxHighPowerInDeck), highs.Count);
            int minHigh = System.Math.Min(config.MinHighPowerCards, maxHigh);
            int highCount = rng.Range(minHigh, maxHigh + 1);

            var deck = new int[DeckData.SlotCount];
            int slot = 0;
            for (int i = 0; i < highCount && slot < deck.Length; i++) deck[slot++] = highs[i];
            for (int i = 0; i < normals.Count && slot < deck.Length; i++) deck[slot++] = normals[i];

            // 슬롯 순서도 섞는다 (빈 슬롯은 뒤에 남김)
            for (int i = slot - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            return deck;
        }

        private static void Shuffle(List<int> list, DeterministicRandom rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
