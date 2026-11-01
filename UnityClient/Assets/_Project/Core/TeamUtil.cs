// 진영 · 열 좌표 변환 공용 함수 (§5 전장 좌표계)
//
// 서버 시뮬레이션은 "월드 열"(0 = Home 플레이어 기준 왼쪽, 2 = 오른쪽)만 사용한다.
// 요청(SpellCastRequest.TargetColumn, MoveRequest.Direction)과 게임 이벤트(IGameEvents)의
// 열은 "그 플레이어 자신의 시점" 기준이다. Away 플레이어는 Home을 마주 보므로 좌우가 반대다.
//
// 4인 개인전 자리 번호 (시계 방향, 전장 위에서 본 그림):
//        1(Home)
//   4(Seat4)  2(Seat2)      · 맞은편끼리 Home ↔ Away, Seat2 ↔ Seat4 (1:1 은 Home · Away 만 쓴다)
//        3(Away)

using SpellboundVR.Contracts;

namespace SpellboundVR.Core
{
    public static class TeamUtil
    {
        /// <summary>진영 배열 크기 (4인 개인전 기준). 지금 경기에서 쓰는 수는 ArenaGeometry.TeamCount</summary>
        public const int TeamCount = 4;
        public const int ColumnCount = 3;

        /// <summary>맞은편 자리 (1:1 의 상대)</summary>
        public static Team Opponent(Team team)
        {
            switch (team)
            {
                case Team.Home: return Team.Away;
                case Team.Away: return Team.Home;
                case Team.Seat2: return Team.Seat4;
                default: return Team.Seat2;
            }
        }

        public static int Index(Team team) => (int)team;

        /// <summary>시계 방향 자리 순서 0 ~ 3 (Home · Seat2 · Away · Seat4)</summary>
        public static int ClockIndex(Team team)
        {
            switch (team)
            {
                case Team.Home: return 0;
                case Team.Seat2: return 1;
                case Team.Away: return 2;
                default: return 3;
            }
        }

        public static Team TeamAtClock(int clock)
        {
            switch (((clock % 4) + 4) % 4)
            {
                case 0: return Team.Home;
                case 1: return Team.Seat2;
                case 2: return Team.Away;
                default: return Team.Seat4;
            }
        }

        /// <summary>전장 그림의 발판 번호 1 ~ 4</summary>
        public static int SeatNumber(Team team) => ClockIndex(team) + 1;

        /// <summary>내 열 중 그 상대를 가리키는 열 (ArenaGeometry.ColumnTarget 의 반대) — 1:1 의 상대는 항상 가운데</summary>
        public static Column ColumnToward(Team self, Team other)
        {
            int diff = (ClockIndex(other) - ClockIndex(self) + 4) % 4;
            return diff == 0 ? Column.Center : (Column)(diff - 1);
        }

        public static int ToWorldColumn(Team viewer, Column localColumn)
        {
            int c = (int)localColumn;
            return viewer == Team.Away ? 2 - c : c;
        }

        public static Column ToLocalColumn(Team viewer, int worldColumn)
        {
            if (worldColumn < 0) worldColumn = 0;
            if (worldColumn > 2) worldColumn = 2;
            return (Column)(viewer == Team.Away ? 2 - worldColumn : worldColumn);
        }

        public static int ToWorldDirection(Team viewer, sbyte localDirection)
        {
            int d = localDirection < 0 ? -1 : (localDirection > 0 ? 1 : 0);
            return viewer == Team.Away ? -d : d;
        }

        public static string ColumnLabel(Column c)
        {
            switch (c)
            {
                case Column.Left: return "L";
                case Column.Center: return "C";
                default: return "R";
            }
        }

        /// <summary>엔티티 ID 규칙 — 플레이어: 1 + 진영 (Home 1, Away 2, Seat2 3, Seat4 4)</summary>
        public static int PlayerId(Team team) => 1 + (int)team;

        /// <summary>구조물 ID 시작값 — 구조물 ID - 이 값 = 구조물 배열 번호 (MatchSnapshot)</summary>
        public const int FirstStructureId = 10;

        /// <summary>엔티티 ID 규칙 — 구조물: 10 + team*2 + (넥서스?1:0)</summary>
        public static int StructureId(Team team, bool isNexus) => FirstStructureId + (int)team * 2 + (isNexus ? 1 : 0);

        /// <summary>엔티티 ID 규칙 — 타워: 가운데 레인은 StructureId, 개인전 왼쪽 · 오른쪽 레인은 18 + team*2 + (오른쪽?1:0)</summary>
        public static int TowerId(Team team, Column column)
        {
            if (column == Column.Center) return StructureId(team, false);
            return FirstStructureId + TeamCount * 2 + (int)team * 2 + (column == Column.Right ? 1 : 0);
        }

        public static bool IsPlayerId(int id) => id >= 1 && id <= TeamCount;

        public static bool IsStructureId(int id) => id >= FirstStructureId && id < FirstStructureId + TeamCount * 4;

        public static Team TeamOfPlayerId(int id) => (Team)(id - 1);

        public static Team TeamOfStructureId(int id) => (Team)((id - FirstStructureId) % (TeamCount * 2) / 2);

        /// <summary>유닛 ID는 이 값 이상</summary>
        public const int FirstUnitId = 1000;
    }
}
