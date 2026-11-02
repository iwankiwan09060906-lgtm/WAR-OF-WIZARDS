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

        public bool IsDuel => Mode == ArenaMode.Duel;
        /// <summary>이 경기의 진영 수 — 진영 번호 0 ~ TeamCount-1 만 쓴다</summary>
        public int TeamCount => IsDuel ? 2 : 4;
        public int LaneCount => IsDuel ? 3 : 6;

        public float LaneHalfWidth => ColumnSpacing * 0.5f;
        public float MinU => IsDuel ? -ColumnSpacing * 1.5f : -FieldLength * 0.5f;
        public float MaxU => IsDuel ? ColumnSpacing * 1.5f : FieldLength * 0.5f;
        /// <summary>띠 장판의 반폭 (S02 독 장판 · H05 분노) — 1:1 은 3열 전체, 개인전은 레인이 교차하므로 그 레인 안에서만</summary>
        public float BandHalfWidth => IsDuel ? ColumnSpacing * 1.5f : LaneHalfWidth;

        public ArenaGeometry(Vector3 homeBase, Vector3 forward, float fieldLength, float columnSpacing,
                             float nexusDepth, float towerDepth, float spawnDepth, ArenaMode mode = ArenaMode.Duel)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            Forward = forward.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            HomeBase = homeBase;
            GroundY = homeBase.y;
            FieldLength = Mathf.Max(4f, fieldLength);
            ColumnSpacing = Mathf.Max(0.5f, columnSpacing);
            NexusDepth = Mathf.Clamp(nexusDepth, 0.5f, FieldLength * 0.5f);
            TowerDepth = Mathf.Clamp(towerDepth, NexusDepth, FieldLength * 0.5f);
            SpawnDepth = Mathf.Clamp(spawnDepth, 0.5f, FieldLength * 0.5f);
            Mode = mode;
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

        public bool HasTeam(Team team) => (int)team < TeamCount;

        /// <summary>전장 중심 (4인 개인전 대각선 레인이 만나는 곳)</summary>
        public Vector2 Center => new Vector2(0f, FieldLength * 0.5f);

        /// <summary>그 진영 발판 중앙</summary>
        public Vector2 BasePosition(Team team)
        {
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
        public Vector2 ColumnAxis(Team team) => team == Team.Home || team == Team.Away ? Vector2.right : SeatRight(team);

        // ── 레인 ──────────────────────────────────────────────

        /// <summary>시전자 시점 열 → 월드 레인 번호</summary>
        public int ResolveLane(Team caster, Column local)
        {
            if (IsDuel) return TeamUtil.ToWorldColumn(caster, local);
            return LaneBetween(caster, ColumnTarget(caster, local));
        }

        /// <summary>내 열이 가리키는 상대 (1:1 은 항상 상대, 개인전은 왼쪽 · 맞은편 · 오른쪽)</summary>
        public Team ColumnTarget(Team caster, Column local)
        {
            if (IsDuel) return TeamUtil.Opponent(caster);
            return TeamUtil.TeamAtClock(TeamUtil.ClockIndex(caster) + (int)local + 1);
        }

        /// <summary>두 진영을 잇는 레인 (4인 개인전)</summary>
        public int LaneBetween(Team a, Team b)
        {
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
            lane = Mathf.Clamp(lane, 0, LaneCount - 1);
            int c = TeamUtil.ClockIndex(caster);
            return TeamUtil.TeamAtClock(s_ffaLanes[lane, 0] == c ? s_ffaLanes[lane, 1] : s_ffaLanes[lane, 0]);
        }

        public bool TouchesLane(Team team, int lane)
        {
            if (IsDuel) return true;
            lane = Mathf.Clamp(lane, 0, LaneCount - 1);
            int c = TeamUtil.ClockIndex(team);
            return s_ffaLanes[lane, 0] == c || s_ffaLanes[lane, 1] == c;
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
            Team from = TouchesLane(caster, lane) ? caster : LaneTarget(caster, lane);
            Vector2 a = BasePosition(from);
            Vector2 d = BasePosition(LaneTarget(from, lane)) - a;
            float length = d.magnitude;
            return new LaneFrame(a, d / length, length);
        }

        /// <summary>3열 폭 띠 장판의 좌표계 — 1:1 은 전장 가운데 레인, 개인전은 그 레인</summary>
        public LaneFrame BandFrame(Team caster, int lane) => GetLane(caster, IsDuel ? 1 : lane);

        /// <summary>조준 지점(지면) → 레인 · 깊이. 1:1 은 열 · v, 개인전은 발판에서 그 지점 방향에 가장 가까운 레인</summary>
        public void ResolveAim(Team team, Vector2 point, out int lane, out Column local, out float depth01)
        {
            if (IsDuel)
            {
                lane = NearestWorldColumn(point.x);
                local = TeamUtil.ToLocalColumn(team, lane);
                depth01 = GetLane(team, lane).Depth01(point);
                return;
            }
            Vector2 to = (point - BasePosition(team)).normalized;
            float best = float.MinValue;
            lane = 0;
            local = Column.Center;
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
            {
                int l = ResolveLane(team, (Column)c);
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
                    if (!IsDuel) return lane.Point(side < 0 ? -distance : lane.Length + distance); // 날아오는 길도 레인 위
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
