// §25.1 계약서(Contracts) — 제공자: ㅈㅈㅇ, 사용자: ㅈㅇㅈ, 개발 중 Mock: MockSessionService
// §22 세션 흐름: 매칭 · 친선전

namespace SpellboundVR.Contracts
{
    /// <summary>
    /// 매칭 · 친선전의 세션 생성/입장 요청. 친선전·매칭 UI(ㅈㅇㅈ)는 세션 생성·입장 자체를
    /// 직접 만들지 않고 이 서비스만 호출한다(§24 ㅈㅇㅈ 경계).
    /// </summary>
    public interface ISessionService
    {
        /// <summary>매칭 큐 등록(§22.1). 시간 초과 시 서버가 봇으로 매칭한다.</summary>
        void RequestMatchmaking();

        void CancelMatchmaking();

        /// <summary>친선전 방 생성, 방 코드 반환(§22.2)</summary>
        string CreateFriendlyRoom();

        /// <summary>코드 입력 또는 초대 수락으로 친선전 방 참가(§22.2)</summary>
        void JoinFriendlyRoom(string roomCode);

        /// <summary>두 번째 사람이 없을 때 방장이 봇으로 채움(§22.2)</summary>
        void FillWithBot();

        void SetReady(bool isReady);

        void LeaveSession();
    }
}
