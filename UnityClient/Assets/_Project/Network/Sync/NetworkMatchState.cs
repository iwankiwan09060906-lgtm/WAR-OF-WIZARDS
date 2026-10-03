// §21 네트워크 — PC 서버(State Authority)의 MatchSimulation 상태를 모든 클라이언트에 복제한다.
//   · 지속 상태: [Networked] 헤더 + 고정 용량 배열 (플레이어 · 슬롯 · 구조물 · 유닛 · 투사체)
//   · 1회성 이벤트: RPC (VFX · 시전 거절/확정 · 이동 거절 · 피격 표시)
//   · 서버만 시뮬레이션을 돌린다. 클라이언트는 받은 상태를 게임 이벤트로 바꿔 표시만 한다 (Rule 4).
//   · 매칭(§22.1): 사람 1명 입장 후 BotFillWaitSeconds 안에 두 번째 사람이 없으면 봇으로 채운다.
//
// 프리팹: Prefabs/Network/NetworkMatchState.prefab (NetworkObject + 이 컴포넌트) — 셋업 도구가 생성.

using System;
using System.Collections.Generic;
using Fusion;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Network
{
    public sealed class NetworkMatchState : NetworkBehaviour, IMatchEventSink
    {
        [Networked] public MatchHeaderNet Header { get; set; }
        [Networked, Capacity(MatchSnapshot.PlayerCount)] public NetworkArray<PlayerSnap> NetPlayers => default;
        [Networked, Capacity(MatchSnapshot.PlayerCount * MatchSnapshot.SlotsPerPlayer)] public NetworkArray<SlotSnap> NetSlots => default;
        [Networked, Capacity(MatchSnapshot.StructureCount)] public NetworkArray<StructureSnap> NetStructures => default;
        [Networked, Capacity(MatchSnapshot.MaxUnits)] public NetworkArray<UnitSnap> NetUnits => default;
        [Networked, Capacity(MatchSnapshot.MaxProjectiles)] public NetworkArray<ProjectileSnap> NetProjectiles => default;

        public static NetworkMatchState Instance { get; private set; }

        /// <summary>스폰 완료 (서버 · 클라이언트 모두)</summary>
        public static event Action<NetworkMatchState> OnInstanceSpawned;

        private readonly MatchSnapshot _serverSnapshot = new MatchSnapshot();
        private readonly MatchSnapshot _clientSnapshot = new MatchSnapshot();
        private readonly Dictionary<PlayerRef, Team> _playerTeams = new Dictionary<PlayerRef, Team>();
        private MatchSimulation _sim;
        private NetworkGameEventPublisher _viewEvents;
        private float _botFillTimer;

        public MatchSimulation Simulation => _sim;

        public override void Spawned()
        {
            Instance = this;
            OnInstanceSpawned?.Invoke(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        // ── 서버 ─────────────────────────────────────────────

        public void InitializeServer(MatchSimulation sim)
        {
            _sim = sim;
            _sim.SetEventSink(this);
            Debug.Log("[NetworkMatchState] 서버 시뮬레이션 연결 완료");
        }

        /// <summary>이 머신에서 상태를 표시할 이벤트 발행자 (클라이언트 HUD / 서버 디버그 뷰)</summary>
        public void BindView(NetworkGameEventPublisher events)
        {
            _viewEvents = events;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || _sim == null) return;

            TickBotFill(Runner.DeltaTime);
            _sim.Tick(Runner.DeltaTime);
            _sim.WriteSnapshot(_serverSnapshot);
            WriteNetworked(_serverSnapshot);
        }

        private void Update()
        {
            if (_sim != null && Object != null && Object.HasStateAuthority) _sim.PollFrameInput();
        }

        private void TickBotFill(float dt)
        {
            if (_sim.Phase != MatchPhase.WaitingForPlayers)
            {
                _botFillTimer = 0f;
                return;
            }
            bool homePresent = !_sim.IsSlotFree(Team.Home);
            bool awayPresent = !_sim.IsSlotFree(Team.Away);
            if (homePresent == awayPresent)
            {
                _botFillTimer = 0f;
                return;
            }
            _botFillTimer += dt;
            if (_botFillTimer >= _sim.Rules.BotFillWaitSeconds)
            {
                _botFillTimer = 0f;
                Team free = homePresent ? Team.Away : Team.Home;
                Debug.Log($"[NetworkMatchState] {_sim.Rules.BotFillWaitSeconds:0}초 동안 상대 없음 → 봇 매칭 ({free})");
                _sim.JoinBot(free);
            }
        }

        /// <summary>사람 입장 → 빈 팀 배정. 자리가 없으면 false (관전).</summary>
        public bool ServerAssignHuman(PlayerRef player, out Team team)
        {
            team = Team.Home;
            if (_sim == null) return false;
            if (_playerTeams.TryGetValue(player, out team)) return true;

            // 대기 중 봇이 자리를 차지했다면 사람에게 양보 (§22.1 사람 우선)
            if (!_sim.TryGetFreeTeam(out team))
            {
                if (_sim.Phase == MatchPhase.WaitingForPlayers || _sim.Phase == MatchPhase.Ended)
                {
                    for (int t = 0; t < 2; t++)
                    {
                        if (_sim.GetPlayer((Team)t).IsBot)
                        {
                            _sim.RemovePlayer((Team)t);
                            team = (Team)t;
                            break;
                        }
                    }
                }
                if (!_sim.IsSlotFree(team)) return false;
            }

            _sim.JoinHuman(team, Deck.DeckData.CreateDefault().ToArray());
            _playerTeams[player] = team;
            Debug.Log($"[NetworkMatchState] Player {player} → {team}");
            return true;
        }

        public void ServerPlayerLeft(PlayerRef player)
        {
            if (_sim == null || !_playerTeams.TryGetValue(player, out var team)) return;
            _playerTeams.Remove(player);
            _sim.NotifyDisconnected(team);
        }

        public bool TryGetTeam(PlayerRef player, out Team team) => _playerTeams.TryGetValue(player, out team);

        public void ServerSubmitDeck(PlayerRef player, int[] deck)
        {
            if (_sim != null && _playerTeams.TryGetValue(player, out var team)) _sim.SetDeck(team, deck);
        }

        public void ServerRequestCast(PlayerRef player, in SpellCastRequest request)
        {
            if (_sim != null && _playerTeams.TryGetValue(player, out var team)) _sim.SubmitCast(team, request);
        }

        public void ServerRequestMove(PlayerRef player, in MoveRequest request)
        {
            if (_sim != null && _playerTeams.TryGetValue(player, out var team)) _sim.SubmitMove(team, request);
        }

        public void ServerReportActivity(PlayerRef player)
        {
            if (_sim != null && _playerTeams.TryGetValue(player, out var team)) _sim.ReportActivity(team);
        }

        private void WriteNetworked(MatchSnapshot s)
        {
            Header = s.Header;
            var players = NetPlayers;
            for (int i = 0; i < MatchSnapshot.PlayerCount; i++) players.Set(i, s.Players[i]);
            var slots = NetSlots;
            for (int i = 0; i < s.Slots.Length; i++) slots.Set(i, s.Slots[i]);
            var structures = NetStructures;
            for (int i = 0; i < s.Structures.Length; i++) structures.Set(i, s.Structures[i]);
            var units = NetUnits;
            for (int i = 0; i < s.UnitCount; i++) units.Set(i, s.Units[i]);
            var projectiles = NetProjectiles;
            for (int i = 0; i < s.ProjectileCount; i++) projectiles.Set(i, s.Projectiles[i]);
        }

        // ── 표시 (클라이언트 + 서버 디버그 뷰) ───────────────────

        public override void Render()
        {
            if (_viewEvents == null) return;

            if (Object.HasStateAuthority)
            {
                if (_sim != null) _viewEvents.ApplySnapshot(_serverSnapshot);
                return;
            }

            ReadNetworked(_clientSnapshot);
            _viewEvents.ApplySnapshot(_clientSnapshot);
        }

        private void ReadNetworked(MatchSnapshot s)
        {
            s.Header = Header;
            var players = NetPlayers;
            for (int i = 0; i < MatchSnapshot.PlayerCount; i++) s.Players[i] = players.Get(i);
            var slots = NetSlots;
            for (int i = 0; i < s.Slots.Length; i++) s.Slots[i] = slots.Get(i);
            var structures = NetStructures;
            for (int i = 0; i < s.Structures.Length; i++) s.Structures[i] = structures.Get(i);
            int uc = Mathf.Min(s.Header.UnitCount, MatchSnapshot.MaxUnits);
            var units = NetUnits;
            for (int i = 0; i < uc; i++) s.Units[i] = units.Get(i);
            int pc = Mathf.Min(s.Header.ProjectileCount, MatchSnapshot.MaxProjectiles);
            var projectiles = NetProjectiles;
            for (int i = 0; i < pc; i++) s.Projectiles[i] = projectiles.Get(i);
        }

        // ── IMatchEventSink (서버) → RPC → 모든 클라이언트 ─────────────

        void IMatchEventSink.OnSpellFx(int spellId, VfxPart part, Vector3 arenaPosition, int worldColumn)
            => RPC_SpellFx(spellId, (byte)part, arenaPosition, (byte)worldColumn);

        void IMatchEventSink.OnCastAccepted(Team team, int slotIndex, int spellId)
            => RPC_CastAccepted((byte)team, (byte)slotIndex, spellId);

        void IMatchEventSink.OnCastRejected(Team team, int slotIndex, CastRejectReason reason)
            => RPC_CastRejected((byte)team, (byte)Mathf.Clamp(slotIndex, 0, 255), (byte)reason);

        void IMatchEventSink.OnMoveRejected(Team team, int confirmedWorldColumn)
            => RPC_MoveRejected((byte)team, (byte)confirmedWorldColumn);

        void IMatchEventSink.OnHit(int targetId, Vector3 arenaPosition, int amount, HitResultKind kind)
            => RPC_Hit(targetId, arenaPosition, amount, (byte)kind);

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_SpellFx(int spellId, byte part, Vector3 arenaPosition, byte worldColumn)
        {
            if (_viewEvents != null) ((IMatchEventSink)_viewEvents).OnSpellFx(spellId, (VfxPart)part, arenaPosition, worldColumn);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_CastAccepted(byte team, byte slot, int spellId)
        {
            if (_viewEvents != null) ((IMatchEventSink)_viewEvents).OnCastAccepted((Team)team, slot, spellId);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_CastRejected(byte team, byte slot, byte reason)
        {
            if (_viewEvents != null) ((IMatchEventSink)_viewEvents).OnCastRejected((Team)team, slot, (CastRejectReason)reason);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_MoveRejected(byte team, byte worldColumn)
        {
            if (_viewEvents != null) ((IMatchEventSink)_viewEvents).OnMoveRejected((Team)team, worldColumn);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_Hit(int targetId, Vector3 arenaPosition, int amount, byte kind)
        {
            if (_viewEvents != null) ((IMatchEventSink)_viewEvents).OnHit(targetId, arenaPosition, amount, (HitResultKind)kind);
        }
    }
}
