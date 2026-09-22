// §25.1 계약서(Contracts) — 제공자/사용자: ㅈㅈㅇ 내부, 개발 중 Mock: TimerRandomBrain(Bot/Brains/)
// §20.1 봇은 "서버 안의 가상 플레이어"다

namespace SpellboundVR.Contracts
{
    /// <summary>
    /// 봇 두뇌 인터페이스(§20.1 원문). 두뇌만 바꾸면 기본 봇 → FSM 봇 → 실제 사람으로
    /// 상대를 교체할 수 있다 — 스킬·판정 코드는 바뀌지 않는다.
    /// </summary>
    public interface IBotBrain
    {
        /// <summary>서버 틱마다 호출된다. 관찰 정보를 보고 이번 틱의 명령을 채운다.</summary>
        void Tick(in BotObservation obs, ref BotCommand cmd);
    }

    /// <summary>
    /// 봇이 매 틱 관찰하는 게임 상태(§20.1: 양측 HP·위치·슬롯 상태·구조물 상태·경기 시간·
    /// 날아오는 투사체 목록). 실제 필드는 Bot/BotObservation.cs(ㅈㅈㅇ)에서 확장 관리하며,
    /// 여기서는 계약서 전원 합의 전 초안 형태만 잡아둔다(§25.1).
    /// </summary>
    public struct BotObservation
    {
        public int SelfHp;
        public int OpponentHp;
        public Column SelfColumn;
        public Column OpponentColumn;
        public float MatchTimeSeconds;

        // TODO(ㅈㅈㅇ, 전원 합의 필요): 슬롯 상태 배열, 구조물 상태, 날아오는 투사체 목록 등
        // §20.1 나머지 관찰 항목을 채운다.
    }

    /// <summary>이번 틱의 봇 명령(§20.1: 이번 틱의 시전 요청(선택)·이동 요청(선택))</summary>
    public struct BotCommand
    {
        public bool HasCastRequest;
        public SpellCastRequest CastRequest;

        public bool HasMoveRequest;
        public MoveRequest MoveRequest;
    }
}
