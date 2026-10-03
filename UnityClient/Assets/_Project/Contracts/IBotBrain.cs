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
    /// 날아오는 투사체 목록).
    /// 열(Column) 값은 모두 "봇 자신의 시점" 기준이다(사람 플레이어의 L/C/R과 같은 규칙).
    /// v6.0 초안에서 슬롯 · 구조물 · 투사체 항목을 확장했다 — 전원 합의 대상(§25.1).
    /// </summary>
    public struct BotObservation
    {
        public int SelfHp;
        public int OpponentHp;
        public Column SelfColumn;
        public Column OpponentColumn;
        public float MatchTimeSeconds;

        /// <summary>bit i = 슬롯 i 가 Ready (§13.3). 덱 내용은 봇 생성 시 두뇌에 전달된다.</summary>
        public byte SelfSlotReadyMask;

        public bool SelfTowerAlive;
        public bool SelfNexusAlive;
        public bool OpponentTowerAlive;
        public bool OpponentNexusAlive;

        /// <summary>봇을 향해 날아오는(회피 가능한) 투사체 수</summary>
        public int IncomingProjectileCount;

        /// <summary>가장 먼저 도착할 투사체가 노리는 열 (IncomingProjectileCount &gt; 0 일 때만 유효)</summary>
        public Column NearestIncomingProjectileColumn;

        /// <summary>가장 먼저 도착할 투사체의 남은 비행 시간(초)</summary>
        public float NearestIncomingProjectileEta;
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
