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
    }

    public enum UnitVisualState : byte
    {
        Moving = 0,
        Attacking = 1,
        Breaching = 2,
        Frozen = 3,
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
        public float HomeAttackMultiplier;
        public float AwayAttackMultiplier;
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
    }

    public struct UnitSnap : INetworkStruct
    {
        public int Id;
        public byte Team;
        public byte Kind;
        public byte State;
        public byte Reserved;
        public float U;
        public float V;
        public int Hp;
        public int MaxHp;
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
        public const int PlayerCount = 2;
        public const int SlotsPerPlayer = 8;
        public const int StructureCount = 4;
        /// <summary>팀당 생존 상한 40 × 2 + 여유</summary>
        public const int MaxUnits = 96;
        public const int MaxProjectiles = 40;

        public MatchHeaderNet Header;
        public readonly PlayerSnap[] Players = new PlayerSnap[PlayerCount];
        public readonly SlotSnap[] Slots = new SlotSnap[PlayerCount * SlotsPerPlayer];
        public readonly StructureSnap[] Structures = new StructureSnap[StructureCount];
        public readonly UnitSnap[] Units = new UnitSnap[MaxUnits];
        public readonly ProjectileSnap[] Projectiles = new ProjectileSnap[MaxProjectiles];

        public MatchPhase Phase => (MatchPhase)Header.Phase;
        public int UnitCount => Header.UnitCount;
        public int ProjectileCount => Header.ProjectileCount;

        public ref PlayerSnap Player(Contracts.Team team) => ref Players[(int)team];

        public ref SlotSnap Slot(Contracts.Team team, int slot) => ref Slots[(int)team * SlotsPerPlayer + slot];

        /// <summary>구조물 배열 순서: Home Tower, Home Nexus, Away Tower, Away Nexus</summary>
        public static int StructureIndex(Contracts.Team team, bool isNexus) => (int)team * 2 + (isNexus ? 1 : 0);

        public ref StructureSnap Structure(Contracts.Team team, bool isNexus) => ref Structures[StructureIndex(team, isNexus)];
    }
}
