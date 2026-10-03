// §2.1 / §21.1 Photon Fusion 2 Server 모드 — PC 서버가 세션의 주인, Quest는 클라이언트
//   · 서버 시작 → NetworkMatchState 스폰 → MatchSimulation 연결
//   · 사람 입장 → NetworkPlayer 스폰(InputAuthority = 그 사람) + 팀 배정
//   · 사람 이탈 → 연결 끊김 패배 처리(§10) + NetworkPlayer 제거

using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using SpellboundVR.Combat;
using UnityEngine;

namespace SpellboundVR.Network
{
    public sealed class FusionServerLauncher : FusionRunnerCallbacks
    {
        private NetworkRunner _runner;
        private NetworkObject _matchStatePrefab;
        private NetworkObject _playerPrefab;
        private MatchSimulation _sim;
        private NetworkGameEventPublisher _view;
        private NetworkMatchState _state;
        private readonly Dictionary<PlayerRef, NetworkObject> _players = new Dictionary<PlayerRef, NetworkObject>();
        private readonly List<PlayerRef> _pendingJoins = new List<PlayerRef>();

        public string Status { get; private set; } = "Idle";

        public NetworkRunner Runner => _runner;

        public int ConnectedPlayers => _players.Count;

        public async void StartServer(string sessionName, ushort port, NetworkObject matchStatePrefab, NetworkObject playerPrefab,
                                      MatchSimulation sim, NetworkGameEventPublisher view)
        {
            if (matchStatePrefab == null || playerPrefab == null)
            {
                Status = "Missing network prefabs (run Spellbound > Setup)";
                Debug.LogError("[FusionServerLauncher] NetworkMatchState / NetworkPlayer 프리팹이 없습니다. 메뉴 Spellbound > 1. Setup Battle Scene 실행 필요");
                return;
            }

            _matchStatePrefab = matchStatePrefab;
            _playerPrefab = playerPrefab;
            _sim = sim;
            _view = view;

            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
            _runner.AddCallbacks(this);
            var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

            var args = new StartGameArgs
            {
                GameMode = GameMode.Server,
                SessionName = sessionName,
                SceneManager = sceneManager,
                PlayerCount = 4,
            };
            if (port > 0) args.Address = NetAddress.Any(port);

            Status = "Starting server '" + sessionName + "'...";
            Debug.Log("[FusionServerLauncher] " + Status);
            var result = await _runner.StartGame(args);
            if (!result.Ok)
            {
                Status = "Server start failed: " + result.ShutdownReason;
                Debug.LogError("[FusionServerLauncher] " + Status + " " + result.ErrorMessage);
                return;
            }

            var obj = _runner.Spawn(_matchStatePrefab, Vector3.zero, Quaternion.identity);
            _state = obj.GetComponent<NetworkMatchState>();
            _state.InitializeServer(_sim);
            _state.BindView(_view);
            Status = "Server running: " + sessionName;
            Debug.Log("[FusionServerLauncher] " + Status);

            for (int i = 0; i < _pendingJoins.Count; i++) HandleJoin(_runner, _pendingJoins[i]);
            _pendingJoins.Clear();
        }

        public override void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer) return;
            if (_state == null)
            {
                _pendingJoins.Add(player);
                return;
            }
            HandleJoin(runner, player);
        }

        private void HandleJoin(NetworkRunner runner, PlayerRef player)
        {
            if (_players.ContainsKey(player)) return;
            bool assigned = _state.ServerAssignHuman(player, out var team);
            byte teamPlusOne = assigned ? (byte)((int)team + 1) : (byte)0;
            var obj = runner.Spawn(_playerPrefab, Vector3.zero, Quaternion.identity, player,
                                   (r, o) => o.GetComponent<NetworkPlayer>().TeamPlusOne = teamPlusOne);
            _players[player] = obj;
            Debug.Log($"[FusionServerLauncher] 입장 {player} → {(assigned ? team.ToString() : "관전(자리 없음)")}");
        }

        public override void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer) return;
            _pendingJoins.Remove(player);
            if (_state != null) _state.ServerPlayerLeft(player);
            if (_players.TryGetValue(player, out var obj))
            {
                if (obj != null) runner.Despawn(obj);
                _players.Remove(player);
            }
            Debug.Log($"[FusionServerLauncher] 이탈 {player}");
        }

        public override void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Status = "Shutdown: " + shutdownReason;
            Debug.LogWarning("[FusionServerLauncher] " + Status);
            if (_sim != null) _sim.VoidMatch(); // §10 서버 오류로 세션 종료 → 경기 무효
        }

        private void OnApplicationQuit()
        {
            if (_runner != null && _runner.IsRunning) _runner.Shutdown();
        }
    }
}
