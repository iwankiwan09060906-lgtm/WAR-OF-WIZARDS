// INetworkRunnerCallbacks 기본 구현 (빈 가상 메서드). 서버 런처 · 클라이언트 커넥터가 필요한 것만 재정의한다.

using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace SpellboundVR.Network
{
    public abstract class FusionRunnerCallbacks : MonoBehaviour, INetworkRunnerCallbacks
    {
        public virtual void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public virtual void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public virtual void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public virtual void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public virtual void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public virtual void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public virtual void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public virtual void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
#pragma warning disable CS0618 // 인터페이스 멤버라 구현은 필요 (Fusion 2.1에서 미사용)
        public virtual void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
#pragma warning restore CS0618
        public virtual void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public virtual void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public virtual void OnInput(NetworkRunner runner, NetworkInput input) { }
        public virtual void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public virtual void OnConnectedToServer(NetworkRunner runner) { }
        public virtual void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public virtual void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public virtual void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public virtual void OnSceneLoadDone(NetworkRunner runner) { }
        public virtual void OnSceneLoadStart(NetworkRunner runner) { }
    }
}
