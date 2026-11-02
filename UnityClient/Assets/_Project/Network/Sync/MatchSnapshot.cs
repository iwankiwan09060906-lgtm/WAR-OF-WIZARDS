// §21.2 동기화 대상 — 서버 시뮬레이션 상태의 직렬화 형태.
//
// 같은 구조체를 두 경로에서 사용한다.
//   · 로컬 모드: MatchSimulation → MatchSnapshot(메모리 복사) → NetworkGameEventPublisher
//   · Fusion 모드: MatchSimulation → NetworkMatchState [Networked] 배열 → 클라이언트 MatchSnapshot
// 그래서 모든 구조체는 Fusion INetworkStruct 규칙(비관리형 · bool 금지)을 지킨다.
// 위치는 아레나 공간(u, v, §5 ArenaGeometry)으로 보내고, 각 클라이언트가 월드 좌표로 바꾼다.

using Fusion;

namespace SpellboundVR.Network
{
    public enum MatchPhase : byte
    {
        WaitingForPlayers = 0,
        Countdown = 1,
        Playing = 2,
        Ended = 3,
    }

    public enum MatchWinner : byte
    {
        Home = 0,
        Away = 1,
        Draw = 2,
        None = 3,
        Void = 4,
        Seat2 = 5,
        Seat4 = 6,
    }

    public enum UnitVisualState : byte
    {
        Moving = 0,
        Attacking = 1,
        Breaching = 2,
        Frozen = 3,
        /// <summary>S05 토네이도에 붙잡힘</summary>
        Stunned = 4,
    }

    /// <summary>PlayerSnap.Flags</summary>
    public static class PlayerFlags
    {
        /// <summary>S01 연막 — 시야 암전</summary>
        public const byte Blind = 1;
        /// <summary>S10 집중 대기 중</summary>
        public const byte Focus = 2;
        /// <summary>H04 반사 결계</summary>
        public const byte Reflect = 4;
    }

    /// <summary>StructureSnap.Flags</summary>
    public static class StructureFlags
    {
        /// <summary>S09 타워 버프</summary>
        public const byte Buffed = 1;
        /// <summary>H01 프리즈</summary>
        public const byte Frozen = 2;
    }

    /// <summary>UnitSnap.Flags</summary>
    public static class UnitFlags
    {
        /// <summary>H05 분노</summary>
        public const byte Raged = 1;
    }

    /// <summary>상대에게 보이는 시전 준비 단계 (표시 전용 — 판정과 무관)</summary>
    public enum CastPreviewPhase : byte
    {
        None = 0,
        /// <summary>룬을 그리는 중 — 아직 무슨 스킬인지 모르고 펜 위치만 보인다</summary>
        Drawing = 1,
        /// <summary>스킬이 정해졌다 (룬 인식 · 조준 · 봇의 시전 준비)</summary>
        Preparing = 2,
    }

    /// <summary>시전 준비 보고 — 사람은 CastPreviewReporter, 봇은 BotPlayer 가 만든다</summary>
    public struct CastPreview
    {
        public CastPreviewPhase Phase;
        public int SpellId;
        /// <summary>TargetColumn 이 의미 있는가 (조준을 시작했는가)</summary>
        public bool Aiming;
        /// <summary>시전자 시점 열 — 어느 상대를 노리는지 아바타가 그쪽을 본다 (정확한 조준점은 보내지 않는다, §6.3)</summary>
        public Contracts.Column TargetColumn;
        /// <summary>시전까지 진행률 0~1</summary>
        public float Progress;
        /// <summary>그리는 손끝 위치 -1~1 (시전자 기준 오른쪽 · 위쪽)</summary>
        public float PenX;
        public float PenY;
        /// <summary>몇 번째 획인가</summary>
        public int Stroke;
    }

    public struct MatchHeaderNet : INetworkStruct
    {
        public int MatchSerial;
        public byte Phase;
        public float PhaseTimeLeft;
        public float MatchTime;
        public int ScalingStage;
        public float NextScalingIn;
        public int WaveIndex;
        public float NextWaveIn;
        public byte NextWaveIsBrute;
        /// <summary>Arena.ArenaMode (0 = 1:1, 1 = 4인 개인전)</summary>
        public byte ArenaMode;
        /// <summary>빈자리를 AI 가 채우기까지 남은 초 (대기 중, 아니면 -1)</summary>
        public float BotFillIn;
        /// <summary>마나 폭풍까지 남은 초, 시작했으면 0, 1:1 은 -1</summary>
        public float StormIn;
        public byte Winner;
        public byte EndReason;
        public float Duration;
        public byte UnitCount;
        public byte ProjectileCount;
    }

    public struct PlayerSnap : INetworkStruct
    {
        public byte Present;
        public byte IsBot;
        public byte Connected;
        public byte WorldColumn;
        public byte Alive;
        public int Hp;
        public int MaxHp;
        public int Shield;
        public float ShieldEndTime;
        /// <summary>AFK 경고 중이면 남은 초, 아니면 -1 (§10: 20초에 경고)</summary>
        public float AfkSecondsLeft;
        public float Accuracy;
        /// <summary>진영 공격력 배율 (HUD 표시)</summary>
        public float AttackMultiplier;
        /// <summary>PlayerFlags 비트</summary>
        public byte Flags;
        /// <summary>4인 개인전 순위 (0 = 아직 결정 안 됨)</summary>
        public byte Place;
        /// <summary>다른 플레이어에게 입힌 피해 합</summary>
        public int DamageDealt;
        /// <summary>CastPreviewPhase</summary>
        public byte CastPhase;
        /// <summary>준비 중인 스킬 (모르면 0)</summary>
        public short CastSpellId;
        /// <summary>노리는 월드 레인 (조준 전이면 NoCastLane)</summary>
        public byte CastLane;
        /// <summary>시전까지 진행률 0~255</summary>
        public byte CastProgress;
        /// <summary>그리는 손끝 위치 -100~100</summary>
        public sbyte PenX;
        public sbyte PenY;
        public byte PenStroke;

        public const byte NoCastLane = 255;
    }

    public struct SlotSnap : INetworkStruct
    {
        public short SpellId;
        public byte State;
        public byte HighPowerUsesLeft;
        public float CooldownEndTime;
        public float CooldownDuration;
    }

    public struct StructureSnap : INetworkStruct
    {
        public int Id;
        public byte Team;
        public byte IsNexus;
        public byte Alive;
        public byte Locked;
        public int Hp;
        public int MaxHp;
        public float U;
        public float V;
        /// <summary>타워 발사 누적 횟수 — 클라이언트가 늘어난 것을 보고 총알을 그린다</summary>
        public int ShotCount;
        /// <summary>마지막 발사 대상 유닛 Id</summary>
        public int ShotTargetId;
        /// <summary>StructureFlags 비트</summary>
        public byte Flags;
    }

    public struct UnitSnap : INetworkStruct
    {
        public int Id;
        public byte Team;
        public byte Kind;
        public byte State;
        /// <summary>UnitFlags 비트</summary>
        public byte Flags;
        public float U;
        public float V;
        public int Hp;
        public int MaxHp;
        /// <summary>공격 횟수 (byte 순환) — 늘면 클라이언트가 공격 이펙트를 그린다</summary>
        public byte AttackCount;
        /// <summary>현재 공격 대상 (유닛 · 구조물 Id, 없으면 0)</summary>
        public int TargetId;
    }

    public struct ProjectileSnap : INetworkStruct
    {
        public int Id;
        public short SpellId;
        public byte Team;
        public byte TargetsPlayer;
        public float U;
        public float V;
        public float Height;
    }

    /// <summary>클라이언트 쪽 상태 사본. 배열은 한 번만 할당한다(§33 GC 0).</summary>
    public sealed class MatchSnapshot
    {
        public const int PlayerCount = 4;
        public const int SlotsPerPlayer = 8;
        /// <summary>진영마다 가운데 타워 · 넥서스, 그 뒤로 진영마다 왼쪽 · 오른쪽 타워 (개인전 전용)</summary>
        public const int StructureCount = PlayerCount * 4;
        /// <summary>팀당 생존 상한 40 × 4 + 여유 (UnitCount 가 byte 라 255 이하)</summary>
        public const int MaxUnits = 176;
        public const int MaxProjectiles = 80;

        public MatchHeaderNet Header;
        public readonly PlayerSnap[] Players = new PlayerSnap[PlayerCount];
        public readonly SlotSnap[] Slots = new SlotSnap[PlayerCount * SlotsPerPlayer];
        public readonly StructureSnap[] Structures = new StructureSnap[StructureCount];
        public readonly UnitSnap[] Units = new UnitSnap[MaxUnits];
        public readonly ProjectileSnap[] Projectiles = new ProjectileSnap[MaxProjectiles];

        /// <summary>자리별 표시 이름 (없으면 빈 문자열 — "1번" · "AI 2번" 은 표시 쪽에서)</summary>
        public readonly string[] Names = { "", "", "", "" };

        public MatchPhase Phase => (MatchPhase)Header.Phase;
        public int UnitCount => Header.UnitCount;
        public int ProjectileCount => Header.ProjectileCount;

        public ref PlayerSnap Player(Contracts.Team team) => ref Players[(int)team];

        public ref SlotSnap Slot(Contracts.Team team, int slot) => ref Slots[(int)team * SlotsPerPlayer + slot];

        /// <summary>구조물 배열 앞쪽 순서: 진영마다 Tower(가운데), Nexus</summary>
        public static int StructureIndex(Contracts.Team team, bool isNexus) => (int)team * 2 + (isNexus ? 1 : 0);

        public ref StructureSnap Structure(Contracts.Team team, bool isNexus) => ref Structures[StructureIndex(team, isNexus)];

        public static int TowerIndex(Contracts.Team team, Contracts.Column column) => Core.TeamUtil.TowerId(team, column) - Core.TeamUtil.FirstStructureId;

        /// <summary>그 진영이 column 쪽 레인에 세운 타워 (1:1 은 가운데만 있다)</summary>
        public ref StructureSnap Tower(Contracts.Team team, Contracts.Column column) => ref Structures[TowerIndex(team, column)];

        /// <summary>attacker 의 공격을 막아 주는 타워 — 둘을 잇는 레인 위의 team 타워</summary>
        public ref StructureSnap TowerFacing(Contracts.Team team, Contracts.Team attacker) => ref Tower(team, Core.TeamUtil.ColumnToward(team, attacker));

        /// <summary>서 있는 타워 수</summary>
        public int TowersAlive(Contracts.Team team)
        {
            int n = 0;
            for (int c = 0; c < Core.TeamUtil.ColumnCount; c++)
                if (Tower(team, (Contracts.Column)c).Alive != 0) n++;
            return n;
        }

        /// <summary>무너진 타워가 없는가 (세우지 않은 자리는 세지 않는다)</summary>
        public bool AllTowersStanding(Contracts.Team team)
        {
            for (int c = 0; c < Core.TeamUtil.ColumnCount; c++)
            {
                ref var t = ref Tower(team, (Contracts.Column)c);
                if (t.MaxHp > 0 && t.Alive == 0) return false;
            }
            return true;
        }
    }
}
