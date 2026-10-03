// 클라이언트 → 서버 요청 창구 (§8 "클라이언트는 요청만 하고, 서버가 결과를 결정한다")
//   · LocalMatchHost : 같은 프로세스의 MatchSimulation에 직접 전달 (오프라인 봇전 · 에디터)
//   · FusionMatchClient : NetworkPlayer RPC로 PC 서버에 전달

using SpellboundVR.Contracts;

namespace SpellboundVR.Network
{
    public interface IMatchClient
    {
        /// <summary>서버가 팀을 배정했는가</summary>
        bool IsReady { get; }

        Team LocalTeam { get; }

        void SubmitDeck(int[] deck);

        void RequestCast(in SpellCastRequest request);

        void RequestMove(in MoveRequest request);

        /// <summary>§10 활동 보고 (룬 드로잉 시작)</summary>
        void ReportActivity();
    }
}
