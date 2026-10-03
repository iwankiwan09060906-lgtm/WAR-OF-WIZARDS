// 진영 · 열 좌표 변환 공용 함수 (§5 전장 좌표계)
//
// 서버 시뮬레이션은 "월드 열"(0 = Home 플레이어 기준 왼쪽, 2 = 오른쪽)만 사용한다.
// 요청(SpellCastRequest.TargetColumn, MoveRequest.Direction)과 게임 이벤트(IGameEvents)의
// 열은 "그 플레이어 자신의 시점" 기준이다. Away 플레이어는 Home을 마주 보므로 좌우가 반대다.

using SpellboundVR.Contracts;

namespace SpellboundVR.Core
{
    public static class TeamUtil
    {
        public const int TeamCount = 2;
        public const int ColumnCount = 3;

        public static Team Opponent(Team team) => team == Team.Home ? Team.Away : Team.Home;

        public static int Index(Team team) => (int)team;

        public static int ToWorldColumn(Team viewer, Column localColumn)
        {
            int c = (int)localColumn;
            return viewer == Team.Home ? c : 2 - c;
        }

        public static Column ToLocalColumn(Team viewer, int worldColumn)
        {
            if (worldColumn < 0) worldColumn = 0;
            if (worldColumn > 2) worldColumn = 2;
            return (Column)(viewer == Team.Home ? worldColumn : 2 - worldColumn);
        }

        public static int ToWorldDirection(Team viewer, sbyte localDirection)
        {
            int d = localDirection < 0 ? -1 : (localDirection > 0 ? 1 : 0);
            return viewer == Team.Home ? d : -d;
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

        /// <summary>엔티티 ID 규칙 — 플레이어: 1(Home), 2(Away)</summary>
        public static int PlayerId(Team team) => 1 + (int)team;

        /// <summary>엔티티 ID 규칙 — 구조물: 10 + team*2 + (넥서스?1:0)</summary>
        public static int StructureId(Team team, bool isNexus) => 10 + (int)team * 2 + (isNexus ? 1 : 0);

        public static bool IsPlayerId(int id) => id == 1 || id == 2;

        public static bool IsStructureId(int id) => id >= 10 && id <= 13;

        public static Team TeamOfPlayerId(int id) => id == 1 ? Team.Home : Team.Away;

        /// <summary>유닛 ID는 이 값 이상</summary>
        public const int FirstUnitId = 1000;
    }
}
