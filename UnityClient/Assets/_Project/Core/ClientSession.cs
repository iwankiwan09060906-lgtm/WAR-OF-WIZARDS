// 클라이언트 측 공용 의존성 묶음. GameBootstrap이 만들어 각 컴포넌트에 주입한다 (싱글톤 대신, §36-7).

using System;
using SpellboundVR.Arena;
using SpellboundVR.Deck;
using SpellboundVR.Gesture;
using SpellboundVR.Network;
using SpellboundVR.Spells;

namespace SpellboundVR.Core
{
    public sealed class ClientSession
    {
        public ClientSession(MatchRuleConfig rules, SpellCatalog catalog, RuneTemplateLibrary runes,
                             ArenaLayout layout, NetworkGameEventPublisher events, DeckData localDeck)
        {
            Rules = rules;
            Catalog = catalog;
            Runes = runes;
            Layout = layout;
            Events = events;
            LocalDeck = localDeck;
        }

        public MatchRuleConfig Rules { get; }
        public SpellCatalog Catalog { get; }
        public RuneTemplateLibrary Runes { get; }
        public ArenaLayout Layout { get; }
        public ArenaGeometry Arena => Events.Arena;
        public NetworkGameEventPublisher Events { get; }
        public DeckData LocalDeck { get; }

        public IMatchClient Client { get; private set; }

        /// <summary>요청 창구가 준비되면 호출 (Fusion은 비동기)</summary>
        public event Action<IMatchClient> OnClientBound;

        public bool HasClient => Client != null && Client.IsReady;

        public void BindClient(IMatchClient client)
        {
            Client = client;
            OnClientBound?.Invoke(client);
        }
    }
}
