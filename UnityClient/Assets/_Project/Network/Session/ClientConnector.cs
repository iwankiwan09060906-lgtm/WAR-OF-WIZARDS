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
            if (isActiveAndEnabled && _attempts < maxRetries) StartCoroutine(ReconnectLater());
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
