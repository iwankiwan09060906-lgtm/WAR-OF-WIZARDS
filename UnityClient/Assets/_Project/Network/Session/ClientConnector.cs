// Quest(또는 에디터 2번째 플레이어) → PC 서버 세션 접속 (Fusion GameMode.Client)
//   · 접속 → NetworkMatchState를 화면 이벤트 발행자에 연결 → 내 NetworkPlayer 팀 배정 확인
//   · 팀이 배정되면 IMatchClient(FusionMatchClient)를 세션에 바인딩하고 덱을 제출한다.
//   · 접속 실패 · 끊김 시 일정 간격으로 재접속을 시도한다.

using System.Collections;
using Fusion;
using Fusion.Sockets;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Network
{
    public sealed class ClientConnector : FusionRunnerCallbacks
    {
        public float retryDelaySeconds = 3f;
        public int maxRetries = 20;

        private NetworkRunner _runner;
        private ClientSession _session;
        private int[] _deck;
        private string _sessionName;
        private bool _viewBound;
        private bool _clientBound;
        private int _attempts;
        private bool _connecting;

        public string Status { get; private set; } = "Idle";
        public int Attempts => _attempts;
        public int MaxAttempts => maxRetries;
        /// <summary>마지막 접속 실패를 사람이 읽을 수 있게 (대기 화면에 표시)</summary>
        public string LastFailure { get; private set; } = "";
        public bool IsConnected => _runner != null && _runner.IsRunning && _clientBound;

        /// <summary>서버 왕복 지연 (ms), 미접속 시 -1 (§21.3 실측 지연 확인용)</summary>
        public float PingMs => _runner != null && _runner.IsRunning
            ? (float)(_runner.GetPlayerRtt(_runner.LocalPlayer) * 1000.0)
            : -1f;

        public void Connect(string sessionName, ClientSession session, int[] deck)
        {
            _sessionName = sessionName;
            _session = session;
            _deck = deck;
            StartCoroutine(ConnectLoop());
        }

        private IEnumerator ConnectLoop()
        {
            while (_attempts < maxRetries)
            {
                _attempts++;
                _connecting = true;
                var task = StartRunner();
                while (!task.IsCompleted) yield return null;
                _connecting = false;
                if (task.Result) yield break;
                Status = "Connect failed, retry " + _attempts + "/" + maxRetries;
                _session.Events.RaiseLocalRejection("Server not found - retrying");
                yield return new WaitForSeconds(retryDelaySeconds);
            }
            Status = "Gave up connecting";
        }

        private static string Explain(ShutdownReason reason)
        {
            switch (reason)
            {
                case ShutdownReason.GameNotFound:
                    return "PC 게임 서버를 찾지 못했습니다. PC에서 SpellboundServer(RunServer.bat)를 먼저 켜 주세요.";
                case ShutdownReason.GameIsFull: return "서버 방이 가득 찼습니다.";
                case ShutdownReason.GameClosed: return "서버 방이 닫혀 있습니다.";
                case ShutdownReason.PhotonCloudTimeout:
                case ShutdownReason.ConnectionTimeout: return "Photon 서버에 연결하지 못했습니다. 인터넷 연결을 확인하세요.";
                case ShutdownReason.InvalidAuthentication:
                case ShutdownReason.AuthenticationTicketExpired: return "Photon 인증에 실패했습니다. AppId 설정을 확인하세요.";
                default: return "서버 접속 실패 (" + reason + ")";
            }
        }

        private async System.Threading.Tasks.Task<bool> StartRunner()
        {
            if (_runner != null)
            {
                await _runner.Shutdown();
                Destroy(_runner);
                var oldScene = GetComponent<NetworkSceneManagerDefault>();
                if (oldScene != null) Destroy(oldScene);
                await System.Threading.Tasks.Task.Yield();
            }

            _viewBound = false;
            _clientBound = false;
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
            _runner.AddCallbacks(this);
            var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

            Status = "Connecting to '" + _sessionName + "'...";
            Debug.Log("[ClientConnector] " + Status);
            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = _sessionName,
                SceneManager = sceneManager,
            });
            if (!result.Ok)
            {
                Debug.LogWarning("[ClientConnector] 접속 실패: " + result.ShutdownReason + " " + result.ErrorMessage);
                LastFailure = Explain(result.ShutdownReason);
                return false;
            }
            Status = "Connected";
            return true;
        }

        private void Update()
        {
            if (_session == null || _runner == null || !_runner.IsRunning) return;

            var state = NetworkMatchState.Instance;
            if (!_viewBound && state != null)
            {
                state.BindView(_session.Events);
                _viewBound = true;
            }

            var local = NetworkPlayer.Local;
            if (!_clientBound && local != null && local.Object != null && local.HasTeam)
            {
                _session.Events.SetLocalTeam(local.Team);
                var client = new FusionMatchClient(local);
                _session.BindClient(client);
                client.SubmitDeck(_deck);
                _clientBound = true;
                Status = "In match as " + local.Team;
                Debug.Log("[ClientConnector] " + Status);
            }
        }

        /// <summary>접속을 시도하지 못하고 포기했는지 (서버가 꺼져 있음)</summary>
        public bool GaveUp => !_left && _attempts >= maxRetries && !_connecting && (_runner == null || !_runner.IsRunning);

        private bool _left;

        /// <summary>경기를 떠난다 (기권 · 나가기) — 다시 접속하지 않는다</summary>
        public void Leave()
        {
            _left = true;
            _attempts = maxRetries;
            StopAllCoroutines();
            if (_runner != null && _runner.IsRunning) _ = _runner.Shutdown();
            Status = "Left";
        }

        public override void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Status = "Disconnected: " + reason;
            Debug.LogWarning("[ClientConnector] " + Status);
            _session?.Events.RaiseLocalRejection("Disconnected from server");
        }

        public override void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (runner != _runner || _connecting) return;
            Status = "Shutdown: " + shutdownReason;
            _clientBound = false;
            _viewBound = false;
            if (isActiveAndEnabled && !_left && _attempts < maxRetries) StartCoroutine(ReconnectLater());
        }

        private IEnumerator ReconnectLater()
        {
            yield return new WaitForSeconds(retryDelaySeconds);
            yield return ConnectLoop();
        }

        private void OnApplicationQuit()
        {
            if (_runner != null && _runner.IsRunning) _runner.Shutdown();
        }
    }
}
