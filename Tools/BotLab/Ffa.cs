// 4인 개인전 측정 · 검사 — 실제 MatchSimulation 으로 봇 4명을 대전시킨다.
//
//   dotnet run -c Release -- ffa [판 수, 기본 200] [최대 경기 시간(초), 기본 900]    자리 공정성 · 경기 길이 · 탈락 시각
//   dotnet run -c Release -- ffatest                                               레인 · 탈락 · 순위 규칙 검사 (실패하면 exit 1)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SpellboundVR.Arena;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;

internal static class Ffa
{
    private static readonly Team[] Seats = { Team.Home, Team.Seat2, Team.Away, Team.Seat4 };

    /// <summary>실제 씬과 같은 치수 (ArenaLayout: 전장 41m · 레인 5m · 개인전 구조물 깊이)</summary>
    public static ArenaGeometry Arena() => new ArenaGeometry(Vector3.zero, Vector3.forward, 41f, 5f, 4f, 9f, 6f, ArenaMode.Ffa4);

    private static BotConfig Config()
    {
        var cfg = BotConfig.CreateDefault();
        cfg.UseServerStrategy = false;
        cfg.ReportExperience = false;
        return cfg;
    }

    private static MatchSimulation NewSim(MatchRuleConfig rules, SpellCatalog catalog, BotConfig cfg, uint seed) =>
        new MatchSimulation(rules, catalog, Arena(), cfg, null, null, null, seed) { LogCasts = false };

    private static void JoinAi(MatchSimulation sim, Team team, SpellCatalog catalog, BotConfig cfg, uint seed)
    {
        sim.JoinBot(team, new AiStrategyBrain(team, sim.GetDeck(team), catalog, cfg, seed));
    }

    public static int Measure(int games, float cap)
    {
        var rules = MatchRuleConfig.CreateDefault();
        rules.ApplyFfaRules();
        var catalog = SpellCatalog.CreateDefault();
        var cfg = Config();
        var places = new int[4, 5];
        var deaths = new List<float>();
        var towers = new List<float>();
        int timeouts = 0, draws = 0, errors = 0;
        double total = 0;
        var sw = Stopwatch.StartNew();
        for (int g = 0; g < games; g++)
        {
            uint seed = (uint)(7919 * (200001 + g));
            var sim = NewSim(rules, catalog, cfg, seed);
            for (int i = 0; i < Seats.Length; i++) JoinAi(sim, Seats[i], catalog, cfg, seed + (uint)i * 11u);
            float dt = rules.LocalTickInterval;
            float limit = rules.CountdownSeconds + cap;
            float t = 0f;
            var towerSeen = new bool[4];
            var deadSeen = new bool[4];
            try
            {
                while (sim.Phase != MatchPhase.Ended && t < limit)
                {
                    sim.Tick(dt);
                    t += dt;
                    for (int i = 0; i < Seats.Length; i++)
                    {
                        if (!towerSeen[i] && sim.Phase == MatchPhase.Playing && !sim.Defense.TowerAlive(Seats[i]))
                        {
                            towerSeen[i] = true;
                            towers.Add(sim.MatchTime);
                        }
                        if (!deadSeen[i] && sim.GetPlayer(Seats[i]).Eliminated)
                        {
                            deadSeen[i] = true;
                            deaths.Add(sim.MatchTime);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                errors++;
                Console.WriteLine($"  판 {g} 오류: {e.Message}\n{e.StackTrace}");
                continue;
            }
            if (sim.Phase != MatchPhase.Ended)
            {
                timeouts++;
                total += cap;
                continue;
            }
            total += sim.Duration;
            if (sim.Outcome.Winner == MatchWinner.Draw) draws++;
            for (int i = 0; i < Seats.Length; i++) places[i, Math.Min(4, sim.GetPlayer(Seats[i]).Place)]++;
        }
        Console.WriteLine($"\n■ 4인 개인전 AI 4명 {games}판 · 최대 {cap:0}초");
        Console.WriteLine($"  평균 경기 {total / Math.Max(1, games - errors):0}초 · 시간 초과 {timeouts} · 무승부 {draws} · 오류 {errors} · 측정 {sw.Elapsed.TotalSeconds:0.0}초");
        for (int i = 0; i < Seats.Length; i++)
            Console.WriteLine($"  {TeamUtil.SeatNumber(Seats[i])}번 자리: 1위 {places[i, 1]} · 2위 {places[i, 2]} · 3위 {places[i, 3]} · 4위 {places[i, 4]}");
        deaths.Sort();
        towers.Sort();
        if (deaths.Count > 0) Console.WriteLine($"  탈락 시각: 처음 {deaths[0]:0}초 · 중간 {deaths[deaths.Count / 2]:0}초 · 마지막 {deaths[deaths.Count - 1]:0}초");
        if (towers.Count > 0) Console.WriteLine($"  타워 파괴 시각: 처음 {towers[0]:0}초 · 중간 {towers[towers.Count / 2]:0}초 ({towers.Count}개)");
        return errors == 0 ? 0 : 1;
    }

    /// <summary>한 판을 1분마다 찍는다 (경기가 왜 안 끝나는지 볼 때)</summary>
    public static int Trace(uint seed, float cap)
    {
        var rules = MatchRuleConfig.CreateDefault();
        rules.ApplyFfaRules();
        var catalog = SpellCatalog.CreateDefault();
        var cfg = Config();
        var sim = NewSim(rules, catalog, cfg, seed);
        for (int i = 0; i < Seats.Length; i++) JoinAi(sim, Seats[i], catalog, cfg, seed + (uint)i * 11u);
        float next = 0f;
        while (sim.Phase != MatchPhase.Ended && sim.MatchTime < cap)
        {
            sim.Tick(rules.LocalTickInterval);
            if (sim.Phase != MatchPhase.Playing || sim.MatchTime < next) continue;
            next += 60f;
            var line = $"{sim.MatchTime,4:0}s";
            foreach (var t in Seats)
            {
                var p = sim.GetPlayer(t);
                int n = 0, near = 0;
                foreach (var u in sim.Units)
                {
                    if (!u.Active || u.Dead) continue;
                    if (u.Team == t) n++;
                    else if ((u.Position - sim.Arena.BasePosition(t)).magnitude < 14f) near++;
                }
                line += $" | {TeamUtil.SeatNumber(t)}: hp{p.Hp,5:0} T{sim.Defense.Tower(t).Hp,5:0} N{sim.Defense.Nexus(t).Hp,5:0} u{n,2} near{near,2}";
            }
            Console.WriteLine(line);
        }
        Console.WriteLine($"끝 {sim.Phase} {sim.Outcome.Winner} t={sim.MatchTime:0}");
        return 0;
    }

    // ── 규칙 검사 ─────────────────────────────────────────

    private static int s_failed;

    private static void Check(bool ok, string what)
    {
        if (ok) return;
        s_failed++;
        Console.WriteLine("  실패: " + what);
    }

    private static void Near(float a, float b, string what) => Check(Mathf.Abs(a - b) < 0.01f, $"{what}: {a} != {b}");

    public static int Test()
    {
        var rules = MatchRuleConfig.CreateDefault();
        rules.ApplyFfaRules();
        var catalog = SpellCatalog.CreateDefault();
        var arena = Arena();

        // ① 그림대로: 1 위 · 2 오른쪽 · 3 아래 · 4 왼쪽 (Home 을 위로 놓고 내려다본 그림), 모든 쌍이 레인 하나로 이어진다
        var seen = new HashSet<int>();
        foreach (var a in Seats)
        {
            var targets = new HashSet<Team>();
            for (int c = 0; c < 3; c++)
            {
                var col = (Column)c;
                int lane = arena.ResolveLane(a, col);
                Team b = arena.ColumnTarget(a, col);
                Check(b != a, $"{a} {col} 이 자기 자신");
                targets.Add(b);
                seen.Add(lane);
                Check(arena.LaneTarget(a, lane) == b && arena.LaneTarget(b, lane) == a, $"레인 {lane} 양 끝");
                Check(arena.TryLocalColumn(a, lane, out Column back) && back == col, $"레인 {lane} → 열 되돌리기");
                var f = arena.GetLane(a, lane);
                var g = arena.GetLane(b, lane);
                Check((f.Origin - arena.BasePosition(a)).magnitude < 0.01f, "레인 시작 = 내 발판");
                Check((f.Point(f.Length) - arena.BasePosition(b)).magnitude < 0.01f, "레인 끝 = 상대 발판");
                Near(f.Length, g.Length, "양쪽 레인 길이");
                // 내 시점 왼쪽 열은 실제로 왼쪽에 있어야 한다
                float side = Vector2.Dot(f.Dir, arena.SeatRight(a));
                if (col == Column.Left) Check(side < -0.1f, $"{a} 왼쪽 레인이 왼쪽이 아님");
                if (col == Column.Right) Check(side > 0.1f, $"{a} 오른쪽 레인이 오른쪽이 아님");
                if (col == Column.Center) Check(Mathf.Abs(side) < 0.01f, $"{a} 가운데 레인이 맞은편이 아님");
            }
            Check(targets.Count == 3, $"{a} 의 상대가 3명이 아님");
        }
        Check(seen.Count == 6, $"레인 {seen.Count}개 (완전 그래프 6개)");

        // ② 조준: 레인 방향을 가리키면 그 레인 · 깊이가 나온다
        foreach (var a in Seats)
            for (int c = 0; c < 3; c++)
            {
                int lane = arena.ResolveLane(a, (Column)c);
                var f = arena.GetLane(a, lane);
                arena.ResolveAim(a, f.Point(f.Length * 0.7f), out int got, out Column local, out float depth);
                Check(got == lane && local == (Column)c, $"{a} {(Column)c} 조준");
                Near(depth, 0.7f, "조준 깊이");
            }

        // ③ 칸: 좌우 칸은 발판 앞을 보고 왼쪽 · 오른쪽
        foreach (var a in Seats)
        {
            Vector2 left = arena.PlayerPosition(a, TeamUtil.ToWorldColumn(a, Column.Left));
            Vector2 right = arena.PlayerPosition(a, TeamUtil.ToWorldColumn(a, Column.Right));
            Check(Vector2.Dot(right - left, arena.SeatRight(a)) > 0f, $"{a} 칸 좌우");
            Check(Vector2.Dot(arena.StructurePosition(a, false) - arena.BasePosition(a), arena.SeatForward(a)) > 0f, $"{a} 타워가 앞에");
        }

        // ④ 경기: 네 자리가 차야 시작, 유닛은 세 레인으로, 투사체는 고른 상대에게만, 탈락 · 순위 · 레인 닫힘
        var cfg = Config();
        var sim = NewSim(rules, catalog, cfg, 12345u);
        sim.JoinBot(Team.Home);
        sim.JoinBot(Team.Away);
        sim.Tick(0.1f);
        Check(sim.Phase == MatchPhase.WaitingForPlayers, "2명만 있으면 시작하면 안 됨");
        sim.JoinBot(Team.Seat2);
        sim.JoinBot(Team.Seat4);
        sim.Tick(0.1f);
        Check(sim.Phase == MatchPhase.Countdown, "4명이면 카운트다운");
        while (sim.Phase == MatchPhase.Countdown) sim.Tick(rules.LocalTickInterval);
        Check(sim.Phase == MatchPhase.Playing, "경기 시작");
        for (int i = 0; i < 12 / rules.LocalTickInterval && sim.Phase == MatchPhase.Playing; i++) sim.Tick(rules.LocalTickInterval);
        var lanesUsed = new HashSet<int>();
        foreach (var u in sim.Units)
            if (u.Active && !u.Dead) lanesUsed.Add(u.Lane);
        Check(lanesUsed.Count == 6, $"웨이브가 레인 6개에 나와야 함: {lanesUsed.Count}");

        sim.DebugSetHp(Team.Seat2, 0f);
        Eliminated(sim, Team.Seat2, 4, rules);
        Check(!sim.Defense.TowerAlive(Team.Seat2) && !sim.Defense.NexusAlive(Team.Seat2), "탈락하면 구조물도 사라짐");
        foreach (var u in sim.Units)
        {
            if (!u.Active || u.Dead) continue;
            Check(u.Team != Team.Seat2, "탈락한 사람의 유닛이 남아 있음");
            Check(sim.IsLaneOpen(u.Team, u.Lane), "탈락한 상대의 레인에 유닛이 남아 있음");
        }
        Check(!sim.IsLaneOpen(Team.Home, arena.LaneBetween(Team.Home, Team.Seat2)), "탈락한 상대의 레인은 닫힘");
        // 닫힌 레인으로 시전 → 거절
        var sink = new RejectSink();
        sim.SetEventSink(sink);
        var home = sim.GetPlayer(Team.Home);
        for (int s = 0; s < home.Slots.Length; s++)
        {
            var def = catalog.Get(home.Slots[s].SpellId);
            if (def == null || def.Targeting == SpellTargeting.Self || def.Targeting == SpellTargeting.Tower || def.Targeting == SpellTargeting.Global) continue;
            sim.DebugResetCooldowns(Team.Home);
            sim.SubmitCast(Team.Home, new SpellCastRequest { SpellId = def.SpellId, SlotIndex = s, TargetColumn = Column.Left, TargetDepth = 0.5f });
            sim.Tick(rules.LocalTickInterval);
            Check(sink.Last == CastRejectReason.LaneClosed, $"닫힌 레인 시전 거절: {sink.Last}");
            break;
        }
        sim.SetEventSink(null);

        sim.DebugSetHp(Team.Home, 0f);
        Eliminated(sim, Team.Home, 3, rules);
        sim.DebugSetHp(Team.Away, 0f);
        sim.Tick(rules.LocalTickInterval);
        Check(sim.Phase == MatchPhase.Ended, "한 명 남으면 끝");
        Check(sim.Outcome.Winner == MatchWinner.Seat4, $"승자 {sim.Outcome.Winner}");
        Check(sim.GetPlayer(Team.Away).Place == 2 && sim.GetPlayer(Team.Seat4).Place == 1, "2위 · 1위");
        Check(MatchResultResolver.ToLocalResult(sim.Outcome.Winner, Team.Seat4) == MatchResult.Win, "4번 승리");
        Check(MatchResultResolver.ToLocalResult(sim.Outcome.Winner, Team.Seat2) == MatchResult.Lose, "2번 패배");

        // ⑤ 같은 틱에 둘이 쓰러지면 같은 순위
        var sim2 = Started(rules, catalog, cfg, 777u);
        sim2.DebugSetHp(Team.Home, 0f);
        sim2.DebugSetHp(Team.Away, 0f);
        sim2.Tick(rules.LocalTickInterval);
        Check(sim2.GetPlayer(Team.Home).Place == 3 && sim2.GetPlayer(Team.Away).Place == 3, "같은 틱 탈락은 같은 순위");

        // ⑥ 사람: 나가면 봇이 잇고, 사람이 모두 탈락하면 남은 봇을 HP 순으로 끝낸다
        var sim3 = new MatchSimulation(rules, catalog, Arena(), cfg, null, null, null, 99u) { LogCasts = false };
        sim3.JoinHuman(Team.Home, sim3.GetDeck(Team.Away));
        sim3.JoinBot(Team.Away);
        sim3.JoinBot(Team.Seat2);
        sim3.JoinBot(Team.Seat4);
        sim3.Tick(0.1f);
        while (sim3.Phase == MatchPhase.Countdown) sim3.Tick(rules.LocalTickInterval);
        sim3.DebugSetHp(Team.Seat2, 500f);
        sim3.Forfeit(Team.Home);
        sim3.Tick(rules.LocalTickInterval);
        Check(sim3.Phase == MatchPhase.Ended, "사람이 없으면 봇끼리 두지 않음");
        Check(sim3.GetPlayer(Team.Home).Place == 4, "기권은 지금 순위");
        Check(sim3.GetPlayer(Team.Seat2).Place == 3, "HP 낮은 봇이 낮은 순위");

        var sim4 = new MatchSimulation(rules, catalog, Arena(), cfg, null, null, null, 98u) { LogCasts = false };
        sim4.JoinHuman(Team.Home, sim4.GetDeck(Team.Away));
        sim4.JoinHuman(Team.Away, sim4.GetDeck(Team.Home));
        sim4.JoinBot(Team.Seat2);
        sim4.JoinBot(Team.Seat4);
        sim4.Tick(0.1f);
        while (sim4.Phase == MatchPhase.Countdown) sim4.Tick(rules.LocalTickInterval);
        sim4.NotifyDisconnected(Team.Away);
        Check(sim4.GetPlayer(Team.Away).IsBot && sim4.GetBot(Team.Away) != null && sim4.IsPlayerAlive(Team.Away), "나간 사람 자리를 봇이 이음");
        for (int i = 0; i < 40; i++) sim4.Tick(rules.LocalTickInterval);
        Check(sim4.Phase == MatchPhase.Playing, "사람이 남아 있으면 경기 계속");

        // ⑦ 투사체: 고른 상대의 지금 칸으로 날아가고, 칸을 옮기면 피한다
        var sim5 = Started(rules, catalog, cfg, 55u);
        var d = SpellDefinition.CreateDefault(SpellIds.S07_Gatling);
        var launch = new SpellboundVR.Spells.Executor.ProjectileLaunch
        {
            SpellId = d.SpellId, CasterTeam = Team.Home, WorldColumn = arena.LaneBetween(Team.Home, Team.Seat2),
            TargetTeam = Team.Seat2, AimColumn = sim5.GetPlayer(Team.Seat2).WorldColumn,
            From = arena.PlayerPosition(Team.Seat2, 1) + Vector2.right * 3f, Height = 1.5f, Speed = 10f, Damage = 100f, Dodgeable = true, IsPlayerSkill = true, FocusBonus = 1f,
        };
        sim5.LaunchProjectile(launch);
        Check(sim5.Projectiles.CountIncoming(sim5, Team.Seat2, out _, out _) == 1, "2번에게 오는 투사체");
        Check(sim5.Projectiles.CountIncoming(sim5, Team.Seat4, out _, out _) == 0, "4번에게는 안 옴");
        float hp = sim5.GetPlayer(Team.Seat2).Hp;
        sim5.SubmitMove(Team.Seat2, new MoveRequest { Direction = 1 });
        for (int i = 0; i < 30; i++) sim5.Tick(rules.LocalTickInterval);
        Near(sim5.GetPlayer(Team.Seat2).Hp, hp, "칸을 옮기면 피해 없음");

        Console.WriteLine(s_failed == 0 ? "■ 4인 개인전 규칙 검사 통과" : $"■ 실패 {s_failed}건");
        return s_failed == 0 ? 0 : 1;
    }

    private static MatchSimulation Started(MatchRuleConfig rules, SpellCatalog catalog, BotConfig cfg, uint seed)
    {
        var sim = NewSim(rules, catalog, cfg, seed);
        foreach (var t in Seats) sim.JoinBot(t);
        sim.Tick(0.1f);
        while (sim.Phase == MatchPhase.Countdown) sim.Tick(rules.LocalTickInterval);
        return sim;
    }

    private static void Eliminated(MatchSimulation sim, Team team, int place, MatchRuleConfig rules)
    {
        sim.Tick(rules.LocalTickInterval);
        var p = sim.GetPlayer(team);
        Check(p.Eliminated && p.Place == place, $"{team} 탈락 순위 {p.Place} (기대 {place})");
    }

    private sealed class RejectSink : IMatchEventSink
    {
        public CastRejectReason Last;
        public void OnSpellFx(int spellId, VfxPart part, Vector3 p, int c) { }
        public void OnCastAccepted(Team team, int slot, int spellId) { }
        public void OnCastRejected(Team team, int slot, CastRejectReason r) => Last = r;
        public void OnMoveRejected(Team team, int c) { }
        public void OnHit(int id, Vector3 p, int amount, HitResultKind k) { }
    }
}
