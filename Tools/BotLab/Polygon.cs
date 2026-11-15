// 다인전(정N각형) 규칙 검사 — 실제 ArenaGeometry · MatchSimulation 으로 확인한다.
//
//   dotnet run -c Release -- polytest      정N각형 배치 · 상대 고르기 · 변/대각선 · 시작 인원 · 탈락 재구성 · 1:1 전환 (실패하면 exit 1)
//   dotnet run -c Release -- poly [판 수, 기본 40] [최대 경기 시간(초), 기본 1200]   봇전 측정: 인원(3~6)별 경기 길이 · 자리별 승률 · 봇 시전 거절률
using System;
using SpellboundVR.Arena;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;

internal static class Polygon
{
    private const float Edge = 30f;
    private const float DuelLength = 20f;

    public static ArenaGeometry Arena() => new ArenaGeometry(Vector3.zero, Vector3.forward, Edge, 5f, 4f, 9f, 6f, ArenaMode.Polygon, DuelLength);

    private static int s_failed;

    private static void Check(bool ok, string what)
    {
        if (ok) return;
        s_failed++;
        Console.WriteLine("  실패: " + what);
    }

    private static bool Near(Vector2 a, Vector2 b) => (a - b).magnitude < 0.01f;

    private static int Mask(params Team[] teams)
    {
        int m = 0;
        foreach (var t in teams) m |= 1 << (int)t;
        return m;
    }

    public static int Test()
    {
        s_failed = 0;
        Geometry();
        DuelStage();
        Match();
        Console.WriteLine(s_failed == 0 ? "■ 다인전 규칙 검사 통과" : $"■ 실패 {s_failed}건");
        return s_failed == 0 ? 0 : 1;
    }

    /// <summary>3 ~ 6명: 정N각형, 왼쪽 · 오른쪽 이웃, 열 = 상대 고르기, 변만 소환 레인</summary>
    private static void Geometry()
    {
        var arena = Arena();
        for (int n = 3; n <= TeamUtil.TeamCount; n++)
        {
            Check(arena.SetRing((1 << n) - 1) || n == 3, $"{n}명 링");
            Check(arena.RingCount == n, $"{n}명 링 인원 {arena.RingCount}");
            for (int i = 0; i < n; i++)
            {
                Team me = arena.RingTeam(i), left = arena.RingTeam(i + 1), right = arena.RingTeam(i - 1);
                Vector2 p = arena.BasePosition(me);
                Check(Mathf.Abs((arena.BasePosition(left) - p).magnitude - Edge) < 0.01f, $"{n}각형 {me} 변 길이");
                Check(Vector2.Dot(arena.SeatForward(me), arena.Center - p) > 0f, $"{n}각형 {me} 가운데를 본다");
                Check(Vector2.Dot(arena.BasePosition(left) - p, arena.SeatRight(me)) < 0f, $"{n}각형 {me} 왼쪽 이웃이 왼쪽에");
                Check(Vector2.Dot(arena.BasePosition(right) - p, arena.SeatRight(me)) > 0f, $"{n}각형 {me} 오른쪽 이웃이 오른쪽에");
                Check(arena.ColumnTarget(me, Column.Left) == left && arena.ColumnTarget(me, Column.Right) == right, $"{n}각형 {me} 좌 · 우 열");

                int valid = 0, spawn = 0, seen = 0;
                for (int c = 0; c < arena.ColumnSlots; c++)
                {
                    var col = (Column)c;
                    if (!arena.ColumnValid(me, col)) continue;
                    valid++;
                    Team target = arena.ColumnTarget(me, col);
                    int lane = arena.ResolveLane(me, col);
                    Check(target != me && arena.LaneTarget(me, lane) == target, $"{n}각형 {me} 열 {c} → {target}");
                    seen |= 1 << (int)target;
                    Check(arena.TryLocalColumn(me, lane, out Column back) && back == col, $"{n}각형 {me} 레인 {lane} → 열 되돌리기");
                    Check(arena.LaneBetween(target, me) == lane, $"{n}각형 레인 {lane} 은 두 사람이 같이 쓴다");
                    var f = arena.GetLane(me, lane);
                    Check(Near(f.Origin, arena.BasePosition(me)) && Near(f.Point(f.Length), arena.BasePosition(target)), $"{n}각형 {me}→{target} 레인 = 발판 가운데끼리");
                    bool neighbor = target == left || target == right;
                    Check(arena.IsSpawnLane(me, lane) == neighbor, $"{n}각형 {me}→{target} 소환 레인은 이웃만");
                    if (neighbor) spawn++;
                }
                Check(valid == n - 1 && seen == (((1 << n) - 1) & ~(1 << (int)me)), $"{n}각형 {me} 열로 모든 상대를 한 번씩 ({valid}개)");
                Check(spawn == 2, $"{n}각형 {me} 소환 레인 2개 ({spawn})");

                // 조준: 건너편 상대 쪽을 가리키면 직접 공격은 그 상대, 소환은 가장 가까운 이웃 레인으로
                Team far = arena.RingTeam(i + n / 2);
                arena.ResolveAim(me, arena.BasePosition(far), out int aimLane, out _, out _);
                Check(arena.LaneTarget(me, aimLane) == far, $"{n}각형 {me} 가리킨 상대 {far} 조준");
                arena.ResolveAim(me, arena.BasePosition(far), out int placeLane, out _, out _, true);
                Check(arena.IsSpawnLane(me, placeLane), $"{n}각형 {me} 소환 조준은 이웃 레인으로");
            }
            int inUse = 0, edges = 0;
            for (int lane = 0; lane < arena.LaneCount; lane++)
            {
                if (arena.LaneInUse(lane)) inUse++;
                if (arena.LaneInUse(lane, false)) edges++;
            }
            Check(inUse == n * (n - 1) / 2 && edges == n, $"{n}각형 쓰는 레인 {inUse}개 · 변 {edges}개");
            // 전장 안에 다 들어간다
            for (int i = 0; i < n; i++)
                for (int c = 0; c < 3; c++)
                {
                    Vector2 q = arena.PlayerPosition(arena.RingTeam(i), c);
                    Check(Near(arena.ClampToField(q), q), $"{n}각형 칸이 전장 밖");
                }
        }
    }

    /// <summary>2명: 1:1 처럼 마주 보고 평행 레인 3개 — 어느 두 자리가 남아도 레인이 엇갈리지 않는다</summary>
    private static void DuelStage()
    {
        var pairs = new[] { (Team.Home, Team.Away), (Team.Home, Team.Seat2), (Team.Away, Team.Seat5), (Team.Seat4, Team.Seat6) };
        foreach (var (a, b) in pairs)
        {
            var arena = Arena();
            arena.SetRing(Mask(a, b, Team.Seat5 == a || Team.Seat5 == b ? Team.Seat6 : Team.Seat5)); // 3명에서
            arena.SetRing(Mask(a, b));                                                                   // 2명으로
            Check(arena.PolygonDuelStage && arena.DuelLike, $"{a}·{b} 1:1 전환");
            Check(Mathf.Abs((arena.BasePosition(a) - arena.BasePosition(b)).magnitude - DuelLength) < 0.01f, $"{a}·{b} 마주 보는 거리");
            int lanes = 0;
            foreach (var me in new[] { a, b })
            {
                Team other = me == a ? b : a;
                for (int c = 0; c < 3; c++)
                {
                    int lane = arena.ResolveLane(me, (Column)c);
                    lanes |= 1 << (lane - ArenaGeometry.PolygonDuelLaneBase);
                    Check(arena.LaneTarget(me, lane) == other && arena.IsSpawnLane(me, lane), $"{a}·{b} {me} 열 {c} 레인");
                    var f = arena.GetLane(me, lane);
                    Check(Vector2.Dot(f.Dir, arena.SeatForward(me)) > 0.999f, $"{a}·{b} {me} 열 {c} 레인이 똑바로 건너편으로 (엇갈림)");
                    Check(Near(f.Origin, arena.PlayerPosition(me, TeamUtil.ToWorldColumn(me, (Column)c))), $"{a}·{b} {me} 열 {c} 레인이 내 그 칸에서 출발");
                    Check(arena.TryLocalColumn(me, lane, out Column back) && back == (Column)c, $"{a}·{b} {me} 열 {c} 되돌리기");
                    Check(arena.TryLocalColumn(other, lane, out Column theirs) && theirs == (Column)(2 - c), $"{a}·{b} 내 왼쪽 레인은 상대의 오른쪽");
                }
                Check(!arena.ColumnValid(me, (Column)3), $"{a}·{b} 1:1 에서는 열 0 ~ 2 만");
            }
            Check(lanes == 0b111, $"{a}·{b} 레인 3개");
        }
    }

    private static MatchSimulation NewSim(uint seed, out MatchRuleConfig rules)
    {
        rules = MatchRuleConfig.CreateDefault();
        rules.ApplyPolygonRules();
        rules.AfkForfeitSeconds = 1e6f; // 검사 중 사람 자리가 잠수로 탈락하지 않게
        var cfg = BotConfig.CreateDefault();
        cfg.UseServerStrategy = false;
        cfg.ReportExperience = false;
        return new MatchSimulation(rules, SpellCatalog.CreateDefault(), Arena(), cfg, null, null, null, seed) { LogCasts = false, AllowTestCasts = true };
    }

    /// <summary>클라이언트처럼: 스냅샷만 보고 링을 맞춘 별도 전장이 서버 전장과 같은가</summary>
    private static void CheckClient(MatchSimulation sim, ArenaGeometry client, MatchSnapshot snap, string when)
    {
        sim.WriteSnapshot(snap);
        int mask = snap.RingMask();
        if (mask != 0) client.SetRing(mask);
        Check(client.RingMask == sim.Arena.RingMask, $"{when}: 클라이언트 링 {client.RingMask} ≠ 서버 {sim.Arena.RingMask}");
        for (int t = 0; t < TeamUtil.TeamCount; t++)
            if (sim.Arena.HasTeam((Team)t))
                Check(Near(client.BasePosition((Team)t), sim.Arena.BasePosition((Team)t)), $"{when}: 클라이언트 {(Team)t} 자리");
    }

    private static int ActiveUnits(MatchSimulation sim)
    {
        int n = 0;
        foreach (var u in sim.Units) if (u.Active && !u.Dead) n++;
        return n;
    }

    private static CastRejectReason Cast(MatchSimulation sim, RejectSink sink, Team team, int spellId, Column column)
    {
        sink.Last = CastRejectReason.None;
        sim.SubmitCast(team, new SpellCastRequest { SpellId = spellId, SlotIndex = MatchSimulation.TestCastSlot, TargetColumn = column, TargetDepth = 0.5f });
        sim.Tick(sim.Rules.LocalTickInterval);
        return sink.Last;
    }

    private static void CheckRing(MatchSimulation sim, int n, string when)
    {
        var arena = sim.Arena;
        Check(arena.RingCount == n, $"{when}: 링 {arena.RingCount}명 (기대 {n})");
        for (int t = 0; t < TeamUtil.TeamCount; t++)
        {
            var team = (Team)t;
            var nexus = sim.Defense.Nexus(team);
            bool inRing = arena.HasTeam(team);
            Check(nexus.Alive == inRing, $"{when}: {team} 넥서스 {(inRing ? "있어야" : "없어야")} 함");
            if (inRing) Check(Near(nexus.Position, arena.StructurePosition(team, true)), $"{when}: {team} 넥서스가 새 자리에");
            for (int c = 0; c < TeamUtil.ColumnCount; c++) Check(!sim.Defense.Tower(team, (Column)c).Alive, $"{when}: 다인전은 타워 없음");
        }
    }

    /// <summary>경기: 3명부터 시작 · 카운트다운 중 입장 · 변에만 웨이브 · 소환은 변만 · 대각선 직접 공격 · 탈락하면 재구성 · 2명이면 1:1</summary>
    private static void Match()
    {
        var sim = NewSim(4242u, out var rules);
        var sink = new RejectSink();
        sim.SetEventSink(sink);

        sim.JoinBot(Team.Home);
        sim.JoinBot(Team.Away);
        sim.Tick(0.1f);
        Check(sim.Phase == MatchPhase.WaitingForPlayers, "2명이면 시작하지 않음");
        sim.JoinBot(Team.Seat2);
        sim.Tick(0.1f);
        Check(sim.Phase == MatchPhase.Countdown, "3명이면 카운트다운");
        sim.JoinBot(Team.Seat4);
        sim.JoinHuman(Team.Seat5, sim.GetDeck(Team.Away));
        sim.Tick(0.1f);
        Check(sim.Phase == MatchPhase.Countdown, "카운트다운 중 입장해도 계속");
        while (sim.Phase == MatchPhase.Countdown) sim.Tick(rules.LocalTickInterval);
        Check(sim.Phase == MatchPhase.Playing, "경기 시작");
        CheckRing(sim, 5, "시작");
        var client = Arena();
        var snap = new MatchSnapshot();
        CheckClient(sim, client, snap, "시작");

        // 웨이브는 좌 · 우 이웃 레인에만
        for (int i = 0; i < 12f / rules.LocalTickInterval; i++) sim.Tick(rules.LocalTickInterval);
        int units = 0;
        foreach (var u in sim.Units)
        {
            if (!u.Active || u.Dead || u.IsStructure || u.Def.IsSummon) continue;
            units++;
            Check(sim.Arena.IsSpawnLane(u.Team, u.Lane), $"웨이브가 대각선에: {u.Team} 레인 {u.Lane}");
        }
        Check(units > 0, "웨이브가 나와야 함");

        // 사람(Seat5) 시전: 소환은 변만, 직접 공격은 누구에게나 (대각선 포함), 지금 인원에 없는 열은 거절
        var me = Team.Seat5;
        Check(Cast(sim, sink, me, SpellIds.S11_AxeWarrior, Column.Center) == CastRejectReason.InvalidTarget, "대각선에 소환 거절");
        Check(Cast(sim, sink, me, SpellIds.S11_AxeWarrior, Column.Left) == CastRejectReason.None, "왼쪽 이웃 레인에 소환");
        Check(Cast(sim, sink, me, SpellIds.S03_Fireball, (Column)3) == CastRejectReason.None, "대각선 너머 상대에게 직접 공격");
        Check(Cast(sim, sink, me, SpellIds.S03_Fireball, (Column)4) == CastRejectReason.InvalidTarget, "5명일 때 열 4 는 없음");
        Team across = sim.Arena.ColumnTarget(me, Column.Center);
        int chord = sim.Arena.ResolveLane(me, Column.Center);
        sim.GetPlayer(across).WorldColumn = 2;
        Check(sim.GetAimColumn(me, chord) == 2, "직접 공격은 시전 순간 상대가 선 칸을 노린다");
        sim.GetPlayer(across).WorldColumn = 1;

        // 탈락 → 4각형으로 재구성: 레인 위 유닛은 지우고 넥서스는 옮긴다
        Vector2 homeNexus = sim.Defense.Nexus(Team.Home).Position;
        // 내 오른쪽 이웃 레인에 터렛 — 그 이웃(Seat4)이 탈락해도 오른쪽 새 레인의 같은 깊이에 HP 그대로 다시 선다
        Check(Cast(sim, sink, me, SpellIds.S13_Turret, Column.Right) == CastRejectReason.None, "오른쪽 이웃 레인에 터렛");
        UnitState turret = null;
        foreach (var u in sim.Units) if (u.Active && !u.Dead && u.Team == me && u.IsStructure) turret = u;
        Check(turret != null, "터렛이 서야 함");
        float turretDepth = 0f;
        if (turret != null)
        {
            turret.Hp = turret.MaxHp * 0.6f;
            turretDepth = sim.Arena.GetLane(me, turret.Lane).Depth01(turret.Position);
        }
        float turretHp = turret != null ? turret.Hp : 0f;
        int survivorsBefore = 0;
        foreach (var u in sim.Units) if (u.Active && !u.Dead && u.Team != Team.Seat2) survivorsBefore++;
        sim.DebugSetHp(Team.Seat2, 0f);
        sim.Tick(rules.LocalTickInterval);
        Check(sim.GetPlayer(Team.Seat2).Eliminated && sim.GetPlayer(Team.Seat2).Place == 5, "5위로 탈락");
        CheckRing(sim, 4, "1명 탈락");
        CheckClient(sim, client, snap, "1명 탈락");
        Check(ActiveUnits(sim) == survivorsBefore, $"재구성해도 남은 사람의 유닛은 그대로 ({ActiveUnits(sim)} / {survivorsBefore})");
        foreach (var u in sim.Units)
        {
            if (!u.Active || u.Dead) continue;
            Check(sim.Arena.IsSpawnLane(u.Team, u.Lane), $"재구성 뒤 {u.Team} 유닛이 이웃 레인에");
            if (!u.IsStructure)
                Check((u.Position - sim.Arena.SpawnPosition(u.Team, u.Lane)).magnitude < 1f, $"재구성 뒤 {u.Team} 유닛은 출발 지점부터 다시");
        }
        if (turret != null)
        {
            Check(turret.Active && !turret.Dead && Mathf.Abs(turret.Hp - turretHp) < 0.01f, "터렛은 HP 그대로 남는다");
            Check(sim.Arena.TryLocalColumn(me, turret.Lane, out Column side) && side == Column.Right, "터렛은 같은 쪽(오른쪽) 새 레인으로");
            Check(Mathf.Abs(sim.Arena.GetLane(me, turret.Lane).Depth01(turret.Position) - turretDepth) < 0.02f, "터렛은 레인 위 같은 깊이에");
        }
        Check(!Near(homeNexus, sim.Defense.Nexus(Team.Home).Position), "넥서스가 4각형 자리로 옮겨짐");
        Check(Mathf.Abs((sim.Arena.BasePosition(Team.Home) - sim.Arena.BasePosition(Team.Away)).magnitude - Edge) < 0.01f, "4각형에서 Home · Away 는 이웃");

        sim.DebugSetHp(Team.Home, 0f);
        sim.Tick(rules.LocalTickInterval);
        CheckRing(sim, 3, "2명 탈락");
        Check(!sim.Arena.ColumnValid(me, Column.Center), "3명이면 가운데 열(대각선)이 없음");

        // 2명 → 1:1 전장: 레인 3개에 웨이브
        sim.DebugSetHp(Team.Seat4, 0f);
        sim.Tick(rules.LocalTickInterval);
        CheckRing(sim, 2, "3명 탈락");
        CheckClient(sim, client, snap, "3명 탈락");
        Check(sim.Arena.PolygonDuelStage, "2명 남으면 1:1 전장");
        if (turret != null)
            Check(turret.Active && !turret.Dead && sim.Arena.TryLocalColumn(me, turret.Lane, out Column side2) && side2 == Column.Right,
                  "터렛 옆 이웃이 탈락해도 터렛은 오른쪽 새 레인(1:1 오른쪽 레인)으로");
        int laneMask = 0;
        for (int i = 0; i < (rules.WaveInterval + 2f) / rules.LocalTickInterval && laneMask != 0b111; i++)
        {
            sim.Tick(rules.LocalTickInterval);
            foreach (var u in sim.Units)
                if (u.Active && !u.Dead && u.Team == me) laneMask |= 1 << (u.Lane - ArenaGeometry.PolygonDuelLaneBase);
        }
        Check(laneMask == 0b111, $"1:1 전장에서는 레인 3개에 웨이브 (레인 비트 {laneMask})");
        Check(Cast(sim, sink, me, SpellIds.S11_AxeWarrior, Column.Center) == CastRejectReason.None, "1:1 전장에서는 가운데 레인에도 소환");

        sim.DebugSetHp(Team.Away, 0f);
        sim.Tick(rules.LocalTickInterval);
        Check(sim.Phase == MatchPhase.Ended && sim.Outcome.Winner == MatchWinner.Seat5, $"마지막 1명 승리 ({sim.Outcome.Winner})");
        Check(sim.GetPlayer(Team.Away).Place == 2 && sim.GetPlayer(me).Place == 1, "2위 · 1위");
        CheckClient(sim, client, snap, "경기 끝 (마지막 1:1 전장 유지)");

        // 봇만으로 6명 한 판이 끝까지 돈다 (마나 폭풍으로 끝난다) — 봇이 쓸 수 없는 레인에 시전을 거의 하지 않아야 한다
        var full = NewSim(77u, out var rules2);
        var count = new RejectSink();
        full.SetEventSink(count);
        for (int t = 0; t < TeamUtil.TeamCount; t++) full.JoinBot((Team)t);
        full.Tick(0.1f);
        while (full.Phase == MatchPhase.Countdown) full.Tick(rules2.LocalTickInterval);
        CheckRing(full, 6, "6명 시작");
        int rebuilds = 0, last = 6;
        for (int i = 0; i < 1500f / rules2.LocalTickInterval && full.Phase == MatchPhase.Playing; i++)
        {
            full.Tick(rules2.LocalTickInterval);
            if (full.Arena.RingCount != last) { rebuilds++; last = full.Arena.RingCount; }
        }
        Check(full.Phase == MatchPhase.Ended, $"6명 봇전이 25분 안에 끝나야 함 (t={full.MatchTime:0})");
        Console.WriteLine($"  6명 봇전: {full.MatchTime:0}초, 재구성 {rebuilds}번, 승자 {full.Outcome.Winner}, 시전 {count.Accepted} · 거절 {count.Rejected} (레인 {count.LaneRejected})");
        Check(count.Accepted > 50, $"봇이 시전을 해야 함 ({count.Accepted})");
        Check(count.LaneRejected <= count.Accepted * 0.05f, $"봇의 레인 거절(대각선 소환 · 없는 열)이 많음: {count.LaneRejected} / {count.Accepted}");
    }

    /// <summary>봇만 N명(3 ~ 6)으로 여러 판 — 경기 길이 · 자리 번호별 1위 · 시전 거절률</summary>
    public static int Measure(int games, float maxSeconds)
    {
        var wins = new int[TeamUtil.TeamCount + 1, TeamUtil.TeamCount + 1]; // [인원, 자리 번호]
        var length = new float[TeamUtil.TeamCount + 1];
        var played = new int[TeamUtil.TeamCount + 1];
        int unfinished = 0;
        long accepted = 0, rejected = 0, laneRejected = 0;
        for (int g = 0; g < games; g++)
        {
            int n = 3 + g % 4;
            var sim = NewSim(1000u + (uint)g, out var rules);
            var sink = new RejectSink();
            sim.SetEventSink(sink);
            for (int i = 0; i < n && sim.TryGetFreeTeam(out Team free); i++) sim.JoinBot(free);
            sim.Tick(0.1f);
            while (sim.Phase == MatchPhase.Countdown) sim.Tick(rules.LocalTickInterval);
            while (sim.Phase == MatchPhase.Playing && sim.MatchTime < maxSeconds) sim.Tick(rules.LocalTickInterval);
            played[n]++;
            accepted += sink.Accepted;
            rejected += sink.Rejected;
            laneRejected += sink.LaneRejected;
            if (sim.Phase != MatchPhase.Ended) { unfinished++; continue; }
            length[n] += sim.MatchTime;
            for (int t = 0; t < TeamUtil.TeamCount; t++)
                if (sim.GetPlayer((Team)t).Place == 1) wins[n, TeamUtil.SeatNumber((Team)t)]++;
        }
        for (int n = 3; n <= TeamUtil.TeamCount; n++)
        {
            if (played[n] == 0) continue;
            var line = new System.Text.StringBuilder($"  {n}명 {played[n]}판: 평균 {length[n] / Mathf.Max(1, played[n]):0}초 · 1위 자리");
            for (int seat = 1; seat <= n; seat++) line.Append($" {seat}번 {wins[n, seat]}");
            Console.WriteLine(line);
        }
        Console.WriteLine($"  시간 안에 안 끝남 {unfinished}판 · 봇 시전 {accepted} · 거절 {rejected} (레인 {laneRejected}, {100.0 * laneRejected / Math.Max(1, accepted):0.0}%)");
        return 0;
    }

    private sealed class RejectSink : IMatchEventSink
    {
        public CastRejectReason Last;
        public int Accepted, Rejected, LaneRejected;
        public void OnSpellFx(int spellId, VfxPart part, Vector3 p, int c) { }
        public void OnCastAccepted(Team team, int slot, int spellId) => Accepted++;
        public void OnCastRejected(Team team, int slot, CastRejectReason r)
        {
            Last = r;
            Rejected++;
            if (r == CastRejectReason.InvalidTarget || r == CastRejectReason.LaneClosed) LaneRejected++;
        }
        public void OnMoveRejected(Team team, int c) { }
        public void OnHit(int id, Vector3 p, int amount, HitResultKind k) { }
    }
}
