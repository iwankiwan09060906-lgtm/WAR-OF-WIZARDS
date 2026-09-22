// §25.1 개발 중 Mock — ISessionService 대역
// 실제 매칭/친선전 세션(ㅈㅈㅇ)이 준비되기 전, ㅈㅇㅈ가 로비/매칭/친선전 UI를
// 독립적으로 개발·테스트할 때 쓴다.

using UnityEngine;

namespace SpellboundVR.Contracts.Mocks
{
    public class MockSessionService : ISessionService
    {
        public void RequestMatchmaking() =>
            Debug.Log("[MockSessionService] RequestMatchmaking (가짜: 즉시 봇 매칭 성공으로 흉내)");

        public void CancelMatchmaking() =>
            Debug.Log("[MockSessionService] CancelMatchmaking");

        public string CreateFriendlyRoom()
        {
            Debug.Log("[MockSessionService] CreateFriendlyRoom");
            return "MOCK1234";
        }

        public void JoinFriendlyRoom(string roomCode) =>
            Debug.Log($"[MockSessionService] JoinFriendlyRoom({roomCode})");

        public void FillWithBot() =>
            Debug.Log("[MockSessionService] FillWithBot");

        public void SetReady(bool isReady) =>
            Debug.Log($"[MockSessionService] SetReady({isReady})");

        public void LeaveSession() =>
            Debug.Log("[MockSessionService] LeaveSession");
    }
}
