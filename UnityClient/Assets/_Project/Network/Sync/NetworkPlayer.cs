// 접속한 사람 플레이어 1명당 1개 (서버가 InputAuthority = 그 플레이어로 스폰).
// 클라이언트 → 서버 요청 RPC 창구: SpellCastRequest / MoveRequest / 덱 제출 / 활동 보고 (§7, §25.1)
// 클라이언트는 Origin / Direction을 보내지 않는다 — 서버가 계산한다 (§26).
//
// 프리팹: Prefabs/Network/NetworkPlayer.prefab (NetworkObject + 이 컴포넌트) — 셋업 도구가 생성.

using System;
using Fusion;
using SpellboundVR.Contracts;
using SpellboundVR.Deck;

namespace SpellboundVR.Network
{
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        /// <summary>0 = 미배정(관전), 1 = Home, 2 = Away</summary>
        [Networked] public byte TeamPlusOne { get; set; }

        public static NetworkPlayer Local { get; private set; }

        public static event Action<NetworkPlayer> OnLocalSpawned;

        public bool HasTeam => TeamPlusOne != 0;

        public Team Team => TeamPlusOne == 2 ? Team.Away : Team.Home;

        public override void Spawned()
        {
            if (Object.HasInputAuthority)
            {
                Local = this;
                OnLocalSpawned?.Invoke(this);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this) Local = null;
        }

        // ── 클라이언트 → 서버 ──────────────────────────────────

        public void SubmitDeck(int[] deck)
        {
            int Get(int i) => deck != null && i < deck.Length ? deck[i] : 0;
            RPC_SubmitDeck(Get(0), Get(1), Get(2), Get(3), Get(4), Get(5), Get(6), Get(7));
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_SubmitDeck(int s0, int s1, int s2, int s3, int s4, int s5, int s6, int s7)
        {
            var state = NetworkMatchState.Instance;
            if (state == null) return;
            var deck = new int[DeckData.SlotCount] { s0, s1, s2, s3, s4, s5, s6, s7 };
            state.ServerSubmitDeck(Object.InputAuthority, deck);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestCast(int spellId, byte slotIndex, byte localColumn, float depth)
        {
            var state = NetworkMatchState.Instance;
            if (state == null) return;
            var request = new SpellCastRequest
            {
                SpellId = spellId,
                SlotIndex = slotIndex,
                TargetColumn = (Column)localColumn,
                TargetDepth = depth,
            };
            state.ServerRequestCast(Object.InputAuthority, request);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestMove(sbyte direction)
        {
            var state = NetworkMatchState.Instance;
            if (state == null) return;
            state.ServerRequestMove(Object.InputAuthority, new MoveRequest { Direction = direction });
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_ReportActivity()
        {
            var state = NetworkMatchState.Instance;
            if (state == null) return;
            state.ServerReportActivity(Object.InputAuthority);
        }
    }
}
