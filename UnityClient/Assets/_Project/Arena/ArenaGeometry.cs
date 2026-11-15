// §5 전장 좌표계 — 서버 시뮬레이션이 쓰는 순수 데이터 좌표계 (MonoBehaviour 아님)
//
// 아레나 공간 (u, v):
//   u = Home 플레이어 기준 오른쪽(+) 방향 거리 (m). 열 중심 = (col - 1) * ColumnSpacing
//   v = Home 발판 → Away 발판 방향 거리 (m). Home 발판 v = 0, Away 발판 v = FieldLength
// 깊이(Depth 0~1)는 "시전자 자기 진영 → 상대 진영" 방향 정규화 거리다.
//
// 경기 종류 (ArenaMode)
//   Duel : 1:1 — Home · Away 가 마주 보고 평행한 레인 3개를 같이 쓴다. 월드 레인 번호 = 월드 열 (0 ~ 2)
//   Ffa4 : 4인 개인전 — 발판 4개가 마름모로 놓이고, 모든 쌍이 레인 1개로 이어진다 (완전 그래프, 레인 6개).
//          각자의 왼쪽 · 가운데 · 오른쪽 열 = 왼쪽 이웃 · 맞은편 · 오른쪽 이웃으로 가는 레인.
//              1 (Home, v = 0)
//        4 (Seat4)    2 (Seat2)     레인 0: 1-2 · 1: 2-3 · 2: 3-4 · 3: 4-1 · 4: 1-3 · 5: 2-4
//              3 (Away, v = FieldLength)
//   Polygon : 다인전 — 살아 있는 N명(3~6)이 정N각형 꼭짓점에 서고(링, 자리 번호 순 = 시계 방향), 레인은 두 명마다 하나 (번호 = 쌍).
//          이웃끼리 레인 = 변(소환 · 설치 · 웨이브), 이웃 아닌 레인 = 대각선(직접 공격만). 탈락하면 SetRing 으로 N-1각형이 되고,
//          2명이 남으면 1:1 처럼 마주 보고 평행 레인 3개(번호 PolygonDuelLaneBase + 0~2)를 같이 쓴다.
//          열(Column) = 상대 고르기: Left = 왼쪽 이웃, Right = 오른쪽 이웃, Center · 3 · 4 = 시계 방향으로 2 · 3 · 4번째 (있을 때만).
//
// 레인 위의 위치 계산은 LaneFrame 으로 한다 (시전자 발판에서 상대 쪽으로 along m, 시전자 오른쪽으로 lateral m).

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Arena
{
    public enum ArenaMode : byte
    {
        Duel = 0,
        Ffa4 = 1,
        /// <summary>다인전 — 정N각형 (3~6명), 탈락하면 N-1각형</summary>
        Polygon = 2,
    }

    /// <summary>스킬이 전장 바깥 어디에서 들어오나 (시전자 시점). 에셋에 정수로 저장되므로 순서를 바꾸지 않는다</summary>
    public enum OffMapOrigin : byte
    {
        /// <summary>맵 밖을 쓰지 않는다 — 시전자 손에서</summary>
        None,
        /// <summary>레인 축을 따라 시전자 등 뒤 먼 곳 — 머리 위를 넘어 전장으로 (상대에게는 정면에서 날아온다)</summary>
        Behind,
        /// <summary>1:1 은 조준 지점의 왼쪽 · 오른쪽 바깥, 개인전은 레인이 교차하므로 그 레인의 양 끝 바깥</summary>
        Flank,
        /// <summary>조준 지점 바로 위 높은 하늘</summary>
        Sky,
        /// <summary>전장 둘레 사방</summary>
        Around,
    }

    /// <summary>시전자 시점의 레인 좌표계</summary>
    public readonly struct LaneFrame
    {
        public readonly Vector2 Origin;
        public readonly Vector2 Dir;
        public readonly Vector2 Right;
        public readonly float Length;

        public LaneFrame(Vector2 origin, Vector2 dir, float length)
        {
            Origin = origin;
            Dir = dir;
            Right = new Vector2(dir.y, -dir.x);
            Length = length;
        }

        public Vector2 Point(float along, float lateral = 0f) => Origin + Dir * along + Right * lateral;

        public float Along(Vector2 p) => Vector2.Dot(p - Origin, Dir);

        public float Lateral(Vector2 p) => Vector2.Dot(p - Origin, Right);

        public float Depth01(Vector2 p) => Mathf.Clamp01(Along(p) / Length);
    }

    public sealed class ArenaGeometry
    {
        /// <summary>4인 개인전 레인 번호 → 양 끝 자리 (시계 방향 자리 순서)</summary>
        private static readonly int[,] s_ffaLanes = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }, { 0, 2 }, { 1, 3 } };
        /// <summary>다인전 쌍 레인 수 (6자리 중 두 자리) — 그 뒤 3개는 2명 남았을 때의 1:1 레인</summary>
        private const int PairLaneCount = TeamUtil.TeamCount * (TeamUtil.TeamCount - 1) / 2;
        public const int PolygonDuelLaneBase = PairLaneCount;
        /// <summary>다인전 레인 번호 → 두 진영 (작은 번호 먼저)</summary>
        private static readonly int[,] s_pairs = BuildPairs();

        private static int[,] BuildPairs()
        {
            var pairs = new int[PairLaneCount, 2];
            for (int a = 0, n = 0; a < TeamUtil.TeamCount; a++)
                for (int b = a + 1; b < TeamUtil.TeamCount; b++, n++)
                {
                    pairs[n, 0] = a;
                    pairs[n, 1] = b;
                }
            return pairs;
        }

        private static int PairLane(Team a, Team b)
        {
            int x = Mathf.Min((int)a, (int)b), y = Mathf.Max((int)a, (int)b);
            return x * (2 * TeamUtil.TeamCount - x - 1) / 2 + (y - x - 1);
        }

        // 다인전 링 — 살아 있는 자리 (진영 번호 순), 자리마다 마지막으로 선 곳 (링에서 빠져도 남긴다: 탈락 연출 · 관전)
        private readonly Team[] _ring = new Team[TeamUtil.TeamCount];
        private int _ringCount;
        private readonly Vector2[] _base = new Vector2[TeamUtil.TeamCount];
        private readonly Vector2[] _forward = new Vector2[TeamUtil.TeamCount];

        public Vector3 HomeBase { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public float GroundY { get; }
        public float FieldLength { get; }
        public float ColumnSpacing { get; }
        public float NexusDepth { get; }
        public float TowerDepth { get; }
        public float SpawnDepth { get; }
        public ArenaMode Mode { get; }
        /// <summary>다인전: 이웃 사이 거리 (변 길이, m)</summary>
        public float PolygonEdge { get; }
        /// <summary>다인전: 2명이 남았을 때 마주 보는 거리 (1:1 전장 길이, m)</summary>
        public float PolygonDuelLength { get; }

        public bool IsDuel => Mode == ArenaMode.Duel;
        public bool IsPolygon => Mode == ArenaMode.Polygon;
        /// <summary>다인전에 2명만 남아 1:1 전장으로 바뀐 상태</summary>
        public bool PolygonDuelStage => IsPolygon && _ringCount == 2;
        /// <summary>1:1 처럼 마주 보고 3열을 같이 쓰는 전장 (1:1, 또는 다인전 마지막 2명)</summary>
        public bool DuelLike => IsDuel || PolygonDuelStage;
        /// <summary>이 경기의 자리 수 — 진영 번호 0 ~ TeamCount-1 만 쓴다 (다인전은 최대 자리, 지금 링은 RingCount)</summary>
        public int TeamCount => IsDuel ? 2 : Mode == ArenaMode.Ffa4 ? 4 : TeamUtil.TeamCount;
        public int LaneCount => IsDuel ? 3 : Mode == ArenaMode.Ffa4 ? 6 : PairLaneCount + 3;
        /// <summary>열(상대 고르기) 값의 개수 — 다인전은 상대가 5명까지라 0 ~ 4</summary>
        public int ColumnSlots => IsPolygon ? 5 : TeamUtil.ColumnCount;
        /// <summary>다인전 링 인원 (다른 경기는 TeamCount)</summary>
        public int RingCount => IsPolygon ? _ringCount : TeamCount;
        /// <summary>다인전 링 구성 (진영 비트) — 바뀌면 전장을 다시 그린다</summary>
        public int RingMask { get; private set; }

        public float LaneHalfWidth => ColumnSpacing * 0.5f;
        public float MinU => IsDuel ? -ColumnSpacing * 1.5f : -FieldLength * 0.5f;
        public float MaxU => IsDuel ? ColumnSpacing * 1.5f : FieldLength * 0.5f;
        /// <summary>띠 장판의 반폭 (S02 독 장판 · H05 분노) — 1:1 은 3열 전체, 개인전은 레인이 교차하므로 그 레인 안에서만</summary>
        public float BandHalfWidth => DuelLike ? ColumnSpacing * 1.5f : LaneHalfWidth;

        /// <param name="fieldLength">1:1 · 4인 = 전장 길이, 다인전 = 변 길이 (이웃 사이)</param>
        /// <param name="polygonDuelLength">다인전 2명 남았을 때 마주 보는 거리 (0 = 변 길이)</param>
        public ArenaGeometry(Vector3 homeBase, Vector3 forward, float fieldLength, float columnSpacing,
                             float nexusDepth, float towerDepth, float spawnDepth, ArenaMode mode = ArenaMode.Duel, float polygonDuelLength = 0f)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            Forward = forward.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            HomeBase = homeBase;
            GroundY = homeBase.y;
            Mode = mode;
            ColumnSpacing = Mathf.Max(0.5f, columnSpacing);
            FieldLength = Mathf.Max(4f, fieldLength);
            if (mode == ArenaMode.Polygon)
            {
                PolygonEdge = FieldLength;
                PolygonDuelLength = polygonDuelLength > 0f ? polygonDuelLength : PolygonEdge;
                // 정육각형의 반지름 = 변 길이가 가장 크다 — 칸 · 넥서스가 들어가게 여유를 둔 정사각 전장
                FieldLength = 2f * (Mathf.Max(PolygonEdge, PolygonDuelLength * 0.5f) + ColumnSpacing * 2f);
            }
            NexusDepth = Mathf.Clamp(nexusDepth, 0.5f, FieldLength * 0.5f);
            TowerDepth = Mathf.Clamp(towerDepth, NexusDepth, FieldLength * 0.5f);
            SpawnDepth = Mathf.Clamp(spawnDepth, 0.5f, FieldLength * 0.5f);
            if (mode == ArenaMode.Polygon)
            {
                for (int t = 0; t < TeamUtil.TeamCount; t++)
                {
                    _base[t] = Center;
                    _forward[t] = Vector2.up;
                }
                SetRing(0b111); // 사람이 들어오기 전 기본 삼각형
            }
        }

        // ── 다인전 링 ─────────────────────────────────────────

        /// <summary>링을 진영 비트(mask)로 다시 잡고 정N각형으로 배치한다 (2명 이상). 바뀌었으면 true</summary>
        public bool SetRing(int mask)
        {
            if (!IsPolygon) return false;
            mask &= (1 << TeamUtil.TeamCount) - 1;
            int n = 0;
            for (int t = 0; t < TeamUtil.TeamCount; t++) if ((mask & (1 << t)) != 0) n++;
            if (n < 2 || mask == RingMask) return false;
            RingMask = mask;
            _ringCount = 0;
            // 자리 번호(TeamUtil.SeatNumber) 순 = 시계 방향 — 들어온 순서(Home · Away · Seat2 ...)대로 채우면 번호가 1 ~ N 으로 이어진다
            for (int c = 0; c < TeamUtil.TeamCount; c++)
            {
                Team t = TeamUtil.TeamAtSeat(c);
                if ((mask & (1 << (int)t)) != 0) _ring[_ringCount++] = t;
            }
            for (int i = 0; i < _ringCount; i++)
            {
                Vector2 p;
                if (_ringCount == 2)
                {
                    p = Center + new Vector2(0f, (i == 0 ? -0.5f : 0.5f) * PolygonDuelLength); // 1:1 처럼 아래 · 위
                }
                else
                {
                    // 0번 자리가 아래, 시계 방향으로 (1번 = 0번의 왼쪽 이웃)
                    float r = PolygonEdge / (2f * Mathf.Sin(Mathf.PI / _ringCount));
                    float a = -Mathf.PI * 0.5f - i * 2f * Mathf.PI / _ringCount;
                    p = Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
                int t = (int)_ring[i];
                _base[t] = p;
                _forward[t] = (Center - p).normalized;
            }
            return true;
        }

        /// <summary>링 안 순서 (없으면 -1)</summary>
        public int RingIndex(Team team)
        {
            for (int i = 0; i < _ringCount; i++) if (_ring[i] == team) return i;
            return -1;
        }

        public Team RingTeam(int index) => _ring[((index % _ringCount) + _ringCount) % _ringCount];

        /// <summary>다인전 열 값 → 링에서 시계 방향 몇 번째 상대 (Left 1 · Right N-1 · Center 2 · 3 · 4)</summary>
        private int PolygonOffset(Column local)
        {
            int v = (int)local;
            return v == 0 ? 1 : v == 2 ? _ringCount - 1 : v == 1 ? 2 : v;
        }

        /// <summary>이 열이 지금 상대 하나를 가리키는가 (다인전은 인원에 따라 0 ~ 4, 2명 남으면 1:1 처럼 0 ~ 2)</summary>
        public bool ColumnValid(Team caster, Column local)
        {
            int v = (int)local;
            if (!IsPolygon || PolygonDuelStage) return v >= 0 && v <= 2;
            if (v == 0 || v == 2) return true;
            return v >= 1 && v <= 4 && PolygonOffset(local) <= _ringCount - 2;
        }

        /// <summary>소환 · 설치 · 웨이브가 다닐 수 있는 레인인가 — 다인전은 이웃끼리 잇는 변만 (대각선은 직접 공격 전용)</summary>
        public bool IsSpawnLane(Team team, int lane)
        {
            if (!IsPolygon || PolygonDuelStage) return true;
            if (lane < 0 || lane >= PolygonDuelLaneBase || !TouchesLane(team, lane)) return false;
            int i = RingIndex(team), j = RingIndex(LaneTarget(team, lane));
            if (i < 0 || j < 0) return false;
            int off = ((j - i) % _ringCount + _ringCount) % _ringCount;
            return off == 1 || off == _ringCount - 1;
        }

        /// <summary>
        /// 지금 전장에 있는 레인인가 — 1:1 · 4인은 모든 레인, 다인전은 링 위의 변(chords 면 대각선도), 2명이면 1:1 레인 3개
        /// </summary>
        public bool LaneInUse(int lane, bool chords = true)
        {
            if (!IsPolygon) return lane >= 0 && lane < LaneCount;
            if (lane >= PolygonDuelLaneBase) return PolygonDuelStage && lane < PolygonDuelLaneBase + 3;
            if (PolygonDuelStage || lane < 0) return false;
            Team a = (Team)s_pairs[lane, 0], b = (Team)s_pairs[lane, 1];
            if (!HasTeam(a) || !HasTeam(b)) return false;
            return chords || IsSpawnLane(a, lane);
        }

        /// <summary>2명 남은 1:1 전장: 0번 자리 월드 열 c 와 이어지는 1번 자리 월드 열 (두 자리의 열 축이 같은 쪽이면 c, 반대면 2-c)</summary>
        private int DuelStageMirror(int c)
        {
            bool aligned = Vector2.Dot(ColumnAxis(_ring[0]), ColumnAxis(_ring[1])) > 0f;
            return aligned ? c : 2 - c;
        }

        public static ArenaGeometry CreateDefault()
        {
            return new ArenaGeometry(Vector3.zero, Vector3.forward, 20f, 5f, 5f, 8f, 6f);
        }

        // ── 변환 ──────────────────────────────────────────────

        public Vector3 ToWorld(float u, float v, float height = 0f)
        {
            return HomeBase + Right * u + Forward * v + Vector3.up * height;
        }

        public Vector3 ToWorld(Vector2 uv, float height = 0f) => ToWorld(uv.x, uv.y, height);

        /// <summary>아레나 평면 방향 → 월드 방향</summary>
        public Vector3 ToWorldDirection(Vector2 d) => Right * d.x + Forward * d.y;

        public Vector2 ToArena(Vector3 world)
        {
            Vector3 d = world - HomeBase;
            return new Vector2(Vector3.Dot(d, Right), Vector3.Dot(d, Forward));
        }

        public float ColumnU(int worldColumn)
        {
            return (Mathf.Clamp(worldColumn, 0, 2) - 1) * ColumnSpacing;
        }

        public int NearestWorldColumn(float u)
        {
            int c = Mathf.RoundToInt(u / ColumnSpacing) + 1;
            return Mathf.Clamp(c, 0, 2);
        }

        // ── 자리 (발판) ───────────────────────────────────────

        /// <summary>이 경기에서 쓰는 자리인가 — 다인전은 지금 링에 있는 자리</summary>
        public bool HasTeam(Team team) => IsPolygon ? (RingMask & (1 << (int)team)) != 0 : (int)team < TeamCount;

        /// <summary>전장 중심 (4인 개인전 대각선 레인이 만나는 곳)</summary>
        public Vector2 Center => new Vector2(0f, FieldLength * 0.5f);

        /// <summary>그 진영 발판 중앙</summary>
        public Vector2 BasePosition(Team team)
        {
            if (IsPolygon) return _base[(int)team];
            switch (team)
            {
                case Team.Home: return Vector2.zero;
                case Team.Away: return new Vector2(0f, FieldLength);
                case Team.Seat2: return new Vector2(-FieldLength * 0.5f, FieldLength * 0.5f);
                default: return new Vector2(FieldLength * 0.5f, FieldLength * 0.5f);
            }
        }

        /// <summary>그 진영이 바라보는 방향 (전장 중심 쪽)</summary>
        public Vector2 SeatForward(Team team)
        {
            if (IsPolygon) return _forward[(int)team];
            switch (team)
            {
                case Team.Home: return Vector2.up;
                case Team.Away: return Vector2.down;
                case Team.Seat2: return Vector2.right;
                default: return Vector2.left;
            }
        }

        public Vector2 SeatRight(Team team)
        {
            Vector2 f = SeatForward(team);
            return new Vector2(f.y, -f.x);
        }

        /// <summary>월드 열 번호가 커지는 방향 — Home · Away 는 u, 개인전의 2 · 4번 발판은 자기 오른쪽</summary>
        public Vector2 ColumnAxis(Team team)
        {
            // Away 는 열 번호가 반대(TeamUtil.ToWorldColumn)라 오른쪽의 반대 방향이 열이 커지는 쪽이다
            if (IsPolygon) return team == Team.Away ? -SeatRight(team) : SeatRight(team);
            return team == Team.Home || team == Team.Away ? Vector2.right : SeatRight(team);
        }

        // ── 레인 ──────────────────────────────────────────────

        /// <summary>시전자 시점 열 → 월드 레인 번호</summary>
        public int ResolveLane(Team caster, Column local)
        {
            if (IsDuel) return TeamUtil.ToWorldColumn(caster, local);
            if (PolygonDuelStage)
            {
                int wc = TeamUtil.ToWorldColumn(caster, local);
                return PolygonDuelLaneBase + (caster == _ring[0] ? wc : DuelStageMirror(wc));
            }
            return LaneBetween(caster, ColumnTarget(caster, local));
        }

        /// <summary>내 열이 가리키는 상대 (1:1 은 항상 상대, 개인전은 왼쪽 · 맞은편 · 오른쪽)</summary>
        public Team ColumnTarget(Team caster, Column local)
        {
            if (IsDuel) return TeamUtil.Opponent(caster);
            if (IsPolygon)
            {
                if (PolygonDuelStage) return caster == _ring[0] ? _ring[1] : _ring[0];
                int i = RingIndex(caster);
                return RingTeam((i < 0 ? 0 : i) + PolygonOffset(local));
            }
            return TeamUtil.TeamAtClock(TeamUtil.ClockIndex(caster) + (int)local + 1);
        }

        /// <summary>두 진영을 잇는 레인 (4인 개인전)</summary>
        public int LaneBetween(Team a, Team b)
        {
            if (IsPolygon) return PolygonDuelStage ? PolygonDuelLaneBase + 1 : PairLane(a, b);
            int ca = TeamUtil.ClockIndex(a), cb = TeamUtil.ClockIndex(b);
            for (int lane = 0; lane < s_ffaLanes.GetLength(0); lane++)
            {
                int x = s_ffaLanes[lane, 0], y = s_ffaLanes[lane, 1];
                if ((x == ca && y == cb) || (x == cb && y == ca)) return lane;
            }
            return 0;
        }

        /// <summary>레인을 따라 caster 가 향하는 상대. caster 가 그 레인 끝이 아니면 레인의 첫 끝</summary>
        public Team LaneTarget(Team caster, int lane)
        {
            if (IsDuel) return TeamUtil.Opponent(caster);
            if (IsPolygon)
            {
                if (lane >= PolygonDuelLaneBase) return caster == _ring[0] ? _ring[_ringCount > 1 ? 1 : 0] : _ring[0];
                lane = Mathf.Max(0, lane);
                return (int)caster == s_pairs[lane, 0] ? (Team)s_pairs[lane, 1] : (Team)s_pairs[lane, 0];
            }
            lane = Mathf.Clamp(lane, 0, LaneCount - 1);
            int c = TeamUtil.ClockIndex(caster);
            return TeamUtil.TeamAtClock(s_ffaLanes[lane, 0] == c ? s_ffaLanes[lane, 1] : s_ffaLanes[lane, 0]);
        }

        public bool TouchesLane(Team team, int lane)
        {
            if (IsDuel) return true;
            if (IsPolygon)
            {
                if (lane >= PolygonDuelLaneBase) return PolygonDuelStage && RingIndex(team) >= 0;
                lane = Mathf.Max(0, lane);
                return s_pairs[lane, 0] == (int)team || s_pairs[lane, 1] == (int)team;
            }
            lane = Mathf.Clamp(lane, 0, LaneCount - 1);
            int c = TeamUtil.ClockIndex(team);
            return s_ffaLanes[lane, 0] == c || s_ffaLanes[lane, 1] == c;
        }

        /// <summary>
        /// 그 진영 발판 중 그 레인과 이어진 칸 (월드 열) — 개인전은 왼쪽 발판 = 왼쪽 상대 레인, 가운데 = 맞은편, 오른쪽 = 오른쪽 상대.
        /// 그 레인으로 오는 공격은 이 칸에 서 있을 때만 맞는다. 1:1 은 레인 = 열
        /// </summary>
        public int LaneColumn(Team team, int lane)
        {
            if (IsDuel) return lane;
            if (IsPolygon)
            {
                // 2명 남은 1:1 레인은 열마다, 쌍 레인은 발판 가운데 칸끼리 (맞는 칸은 시전 순간 상대가 선 칸 — MatchSimulation.GetAimColumn)
                if (lane < PolygonDuelLaneBase) return 1;
                int c = lane - PolygonDuelLaneBase;
                return team == _ring[0] ? c : DuelStageMirror(c);
            }
            return TeamUtil.ToWorldColumn(team, TeamUtil.ColumnToward(team, LaneTarget(team, lane)));
        }

        /// <summary>레인이 viewer 시점 몇 열인가. 4인 개인전에서 viewer 와 이어지지 않은 레인이면 false</summary>
        public bool TryLocalColumn(Team viewer, int lane, out Column local)
        {
            local = Column.Center;
            if (IsDuel)
            {
                local = TeamUtil.ToLocalColumn(viewer, lane);
                return true;
            }
            if (!TouchesLane(viewer, lane)) return false;
            if (IsPolygon)
            {
                if (lane >= PolygonDuelLaneBase)
                {
                    local = TeamUtil.ToLocalColumn(viewer, LaneColumn(viewer, lane));
                    return true;
                }
                int i = RingIndex(viewer), j = RingIndex(LaneTarget(viewer, lane));
                if (i < 0 || j < 0 || PolygonDuelStage) return false;
                int off = ((j - i) % _ringCount + _ringCount) % _ringCount;
                local = off == 1 ? Column.Left : off == _ringCount - 1 ? Column.Right : (Column)(off == 2 ? 1 : off);
                return true;
            }
            int diff = (TeamUtil.ClockIndex(LaneTarget(viewer, lane)) - TeamUtil.ClockIndex(viewer) + 4) % 4;
            local = (Column)(diff - 1);
            return true;
        }

        /// <summary>시전자 시점 레인 좌표계 — 시전자 발판 쪽이 0, 상대 발판 쪽이 Length</summary>
        public LaneFrame GetLane(Team caster, int lane)
        {
            if (IsDuel)
            {
                Vector2 dir = caster == Team.Away ? Vector2.down : Vector2.up;
                float baseV = caster == Team.Away ? FieldLength : 0f;
                return new LaneFrame(new Vector2(ColumnU(lane), baseV), dir, FieldLength);
            }
            // 개인전 레인은 양쪽 진영에서 그 레인과 이어진 발판끼리 잇는다
            Team from = TouchesLane(caster, lane) ? caster : LaneTarget(caster, lane);
            Team to = LaneTarget(from, lane);
            Vector2 a = PlayerPosition(from, LaneColumn(from, lane));
            Vector2 d = PlayerPosition(to, LaneColumn(to, lane)) - a;
            float length = d.magnitude;
            if (length < 1e-3f) return new LaneFrame(a, SeatForward(from), 1f); // 링에 1명뿐인 순간 (경기 끝)
            return new LaneFrame(a, d / length, length);
        }

        /// <summary>3열 폭 띠 장판의 좌표계 — 1:1 은 전장 가운데 레인, 개인전은 그 레인</summary>
        public LaneFrame BandFrame(Team caster, int lane) => GetLane(caster, IsDuel ? 1 : PolygonDuelStage ? PolygonDuelLaneBase + 1 : lane);

        /// <summary>조준 지점(지면) → 레인 · 깊이. 1:1 은 열 · v, 개인전은 발판에서 그 지점 방향에 가장 가까운 레인 (spawnOnly: 소환 · 설치 레인만)</summary>
        public void ResolveAim(Team team, Vector2 point, out int lane, out Column local, out float depth01, bool spawnOnly = false)
        {
            if (IsDuel)
            {
                lane = NearestWorldColumn(point.x);
                local = TeamUtil.ToLocalColumn(team, lane);
                depth01 = GetLane(team, lane).Depth01(point);
                return;
            }
            lane = 0;
            local = Column.Center;
            if (PolygonDuelStage)
            {
                // 2명 남은 1:1 전장: 가로로 가장 가까운 레인
                float nearest = float.MaxValue;
                for (int c = 0; c < TeamUtil.ColumnCount; c++)
                {
                    int l = ResolveLane(team, (Column)c);
                    float d = Mathf.Abs(GetLane(team, l).Lateral(point));
                    if (d >= nearest) continue;
                    nearest = d;
                    lane = l;
                    local = (Column)c;
                }
                depth01 = GetLane(team, lane).Depth01(point);
                return;
            }
            Vector2 to = (point - BasePosition(team)).normalized;
            float best = float.MinValue;
            for (int c = 0; c < ColumnSlots; c++)
            {
                if (!ColumnValid(team, (Column)c)) continue;
                int l = ResolveLane(team, (Column)c);
                if (spawnOnly && !IsSpawnLane(team, l)) continue; // 다인전 소환 · 설치는 좌 · 우 이웃 레인 중에서
                float dot = Vector2.Dot(to, GetLane(team, l).Dir);
                if (dot > best)
                {
                    best = dot;
                    lane = l;
                    local = (Column)c;
                }
            }
            depth01 = GetLane(team, lane).Depth01(point);
        }

        // ── 주요 지점 ─────────────────────────────────────────

        public Vector2 PlayerPosition(Team team, int worldColumn)
        {
            return BasePosition(team) + ColumnAxis(team) * ((Mathf.Clamp(worldColumn, 0, 2) - 1) * ColumnSpacing);
        }

        public Vector2 StructurePosition(Team team, bool isNexus)
        {
            float depth = isNexus ? NexusDepth : TowerDepth;
            return BasePosition(team) + SeatForward(team) * depth;
        }

        /// <summary>타워 자리 — 1:1 은 발판 앞, 개인전은 column 쪽 상대로 가는 레인 위 (가운데 레인 = 발판 앞)</summary>
        public Vector2 TowerPosition(Team team, Column column)
        {
            if (IsDuel) return StructurePosition(team, false);
            return GetLane(team, ResolveLane(team, column)).Point(TowerDepth);
        }

        public Vector2 SpawnPosition(Team team, int lane) => GetLane(team, lane).Point(SpawnDepth);

        /// <summary>§11.1 돌파 지점 — 대상 진영 Commander 발판 앞 (그 레인 위)</summary>
        public Vector2 BreachPoint(Team targetTeam, int lane, float offset) => GetLane(targetTeam, lane).Point(offset);

        public Quaternion FacingRotation(Team team)
        {
            return Quaternion.LookRotation(ToWorldDirection(SeatForward(team)), Vector3.up);
        }

        public Vector2 ClampToField(Vector2 uv)
        {
            uv.x = Mathf.Clamp(uv.x, MinU, MaxU);
            uv.y = Mathf.Clamp(uv.y, 0f, FieldLength);
            return uv;
        }

        // ── 맵 밖 ─────────────────────────────────────────────

        /// <summary>
        /// lane 위 target 으로 날아올 스킬의 맵 밖 출발점. side = -1 시전자 쪽 · +1 반대쪽, distance = 전장 가장자리에서 바깥으로 (m).
        /// 판정은 도착 지점으로 하므로 ClampToField 를 쓰지 않는다.
        /// </summary>
        public Vector2 OffMapPoint(in LaneFrame lane, OffMapOrigin origin, Vector2 target, int side, float distance)
        {
            switch (origin)
            {
                case OffMapOrigin.Behind:
                    return lane.Point(-distance);
                case OffMapOrigin.Flank:
                    if (!DuelLike) return lane.Point(side < 0 ? -distance : lane.Length + distance); // 날아오는 길도 레인 위
                    float lateral = Vector2.Dot(target - Center, lane.Right);
                    return target + lane.Right * (side * (BandHalfWidth + distance - side * lateral));
                case OffMapOrigin.Sky:
                    return target - lane.Dir * distance;
                case OffMapOrigin.Around:
                    return OffMapRing(side, 8, distance);
                default:
                    return target;
            }
        }

        /// <summary>전장 둘레 count 등분 중 index 번째 맵 밖 지점</summary>
        public Vector2 OffMapRing(int index, int count, float distance)
        {
            float angle = (index + 0.5f) / Mathf.Max(1, count) * Mathf.PI * 2f;
            return Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (FieldLength * 0.5f + distance);
        }
    }
}
