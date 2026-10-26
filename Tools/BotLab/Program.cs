// War of Wizards 봇 승률 측정기 — 실제 MatchSimulation(서버 판정 코드 그대로)으로 봇끼리 대전시킨다.
//
//   dotnet run -c Release -- [판 수(대진마다), 기본 100] [최대 경기 시간(초), 기본 900]
//
// 대진: 랜덤 vs 랜덤(기준) · AI vs 랜덤 · AI(랜덤과 같은 시전 속도) vs 랜덤 · AI vs AI
// AI 쪽은 판마다 Home / Away 를 번갈아 맡는다 (진영 차이 상쇄). 덱은 시드로 정해지는 랜덤 덱(양쪽 같은 규칙).
using System;
using System.Diagnostics;
using SpellboundVR.Arena;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Spells;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length > 0 && args[0] == "diag") return Diag(args.Length > 1 ? int.Parse(args[1]) : 200, args.Length > 2 ? float.Parse(args[2]) : 6f);
        if (args.Length > 0 && args[0] == "tune") return Tune(args.Length > 1 ? int.Parse(args[1]) : 400, args.Length > 2 ? int.Parse(args[2]) : 3);
        int games = args.Length > 0 ? int.Parse(args[0]) : 100;
        float cap = args.Length > 1 ? float.Parse(args[1]) : 900f;

        var rules = MatchRuleConfig.CreateDefault();
        var catalog = SpellCatalog.CreateDefault();
        var arena = ArenaGeometry.CreateDefault();
        Console.WriteLine($"[BotLab] 판 {games}개씩 · 최대 {cap:0}초 · 틱 {1f / rules.LocalTickInterval:0}Hz · 스킬 {catalog.All.Count}종");

        Run("랜덤 vs 랜덤 (기준)", games, cap, rules, catalog, arena, Brain.Random, Brain.Random, 2.5f);
        Run("AI 전략 vs 랜덤", games, cap, rules, catalog, arena, Brain.Ai, Brain.Random, 2.5f);
        Run("AI 전략 (랜덤과 같은 시전 속도 6초) vs 랜덤", games, cap, rules, catalog, arena, Brain.Ai, Brain.Random, 6f);
        Run("AI 전략 vs AI 전략 (기준)", games, cap, rules, catalog, arena, Brain.Ai, Brain.Ai, 2.5f);
        return 0;
    }

    private enum Brain { Random, Ai }

    private static void Run(string title, int games, float cap, MatchRuleConfig rules, SpellCatalog catalog, ArenaGeometry arena,
                            Brain a, Brain b, float aiGap)
    {
        int aWins = 0, bWins = 0, draws = 0, timeouts = 0, aLead = 0, errors = 0;
        double totalTime = 0;
        var sw = Stopwatch.StartNew();
        for (int g = 0; g < games; g++)
        {
            uint seed = (uint)(7919 * (100001 + g));      // tune 이 쓴 훈련 시드(1~)와 겹치지 않게
            bool aHome = g % 2 == 0;
            Team aTeam = aHome ? Team.Home : Team.Away;
            Team bTeam = aHome ? Team.Away : Team.Home;
            var cfg = BotConfig.CreateDefault();
            cfg.AiCastGapSeconds = aiGap;
            cfg.UseServerStrategy = false;
            cfg.ReportExperience = false;
            var sim = new MatchSimulation(rules, catalog, arena, cfg, null, null, null, seed) { LogCasts = false };
            try
            {
                sim.JoinBot(aTeam, Make(a, aTeam, sim, catalog, cfg, seed + 11));
                sim.JoinBot(bTeam, Make(b, bTeam, sim, catalog, cfg, seed + 23));
                float dt = rules.LocalTickInterval;
                float wall = 0f;
                while (sim.Phase != MatchPhase.Ended && sim.MatchTime < cap && wall < cap + 30f)
                {
                    sim.Tick(dt);
                    wall += dt;
                }
            }
            catch (Exception e)
            {
                errors++;
                if (errors <= 2) Console.WriteLine($"  [오류] 판 {g}: {e.GetType().Name}: {e.Message}\n{e.StackTrace?.Split('\n')[0]}");
                continue;
            }
            totalTime += sim.MatchTime;
            if (sim.Phase != MatchPhase.Ended)
            {
                timeouts++;
                // 시간 초과: HP + 구조물로 우세 판정 (참고용)
                float aScore = Score(sim, aTeam), bScore = Score(sim, bTeam);
                if (aScore > bScore) aLead++;
                continue;
            }
            var w = sim.Outcome.Winner;
            if (w == MatchWinner.Draw || w == MatchWinner.Void || w == MatchWinner.None) draws++;
            else if ((w == MatchWinner.Home) == aHome) aWins++;
            else bWins++;
        }
        int decided = aWins + bWins;
        Console.WriteLine($"\n■ {title}");
        Console.WriteLine($"  A 승 {aWins} · B 승 {bWins} · 무 {draws} · 시간 초과 {timeouts} (A 우세 {aLead}) · 오류 {errors}");
        Console.WriteLine($"  A 승률 {(decided > 0 ? 100.0 * aWins / decided : 0):0.0}% (승부 난 판 기준) · 평균 경기 {totalTime / Math.Max(1, games - errors):0}초 · 측정 {sw.Elapsed.TotalSeconds:0.0}초");
    }

    /// <summary>진단: AI vs 랜덤에서 양쪽이 실제로 쓴 스킬 · 거절 이유 · 승패별 통계</summary>
    private sealed class CountingSink : IMatchEventSink
    {
        public readonly System.Collections.Generic.Dictionary<string, int>[] Casts =
            { new System.Collections.Generic.Dictionary<string, int>(), new System.Collections.Generic.Dictionary<string, int>() };
        public readonly System.Collections.Generic.Dictionary<string, int>[] Rejects =
            { new System.Collections.Generic.Dictionary<string, int>(), new System.Collections.Generic.Dictionary<string, int>() };
        public Team AiTeam;
        public void OnSpellFx(int spellId, VfxPart part, UnityEngine.Vector3 p, int c) { }
        public void OnCastAccepted(Team team, int slot, int spellId) => Add(Casts[team == AiTeam ? 0 : 1], SpellIds.Code(spellId));
        public void OnCastRejected(Team team, int slot, CastRejectReason r) => Add(Rejects[team == AiTeam ? 0 : 1], r.ToString());
        public void OnMoveRejected(Team team, int c) { }
        public void OnHit(int id, UnityEngine.Vector3 p, int amount, HitResultKind k) { }
        private static void Add(System.Collections.Generic.Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out int n) ? n + 1 : 1;
    }

    private static int Diag(int games, float aiGap)
    {
        var rules = MatchRuleConfig.CreateDefault();
        var catalog = SpellCatalog.CreateDefault();
        var arena = ArenaGeometry.CreateDefault();
        var sink = new CountingSink();
        int aiWins = 0, rndWins = 0;
        float aiTowerKills = 0, rndTowerKills = 0, time = 0;
        for (int g = 0; g < games; g++)
        {
            uint seed = (uint)(7919 * (g + 1));
            Team ai = g % 2 == 0 ? Team.Home : Team.Away, rnd = TeamUtil.Opponent(ai);
            var cfg = BotConfig.CreateDefault();
            cfg.AiCastGapSeconds = aiGap;
            var sim = new MatchSimulation(rules, catalog, arena, cfg, null, null, null, seed) { LogCasts = false };
            sink.AiTeam = ai;
            sim.SetEventSink(sink);
            sim.JoinBot(ai, Make(Brain.Ai, ai, sim, catalog, cfg, seed + 11));
            sim.JoinBot(rnd, Make(Brain.Random, rnd, sim, catalog, cfg, seed + 23));
            while (sim.Phase != MatchPhase.Ended && sim.MatchTime < 900f) sim.Tick(rules.LocalTickInterval);
            time += sim.MatchTime;
            if (!sim.Defense.TowerAlive(rnd)) aiTowerKills++;
            if (!sim.Defense.TowerAlive(ai)) rndTowerKills++;
            if (sim.Phase == MatchPhase.Ended && sim.Outcome.Winner != MatchWinner.Draw)
            {
                if ((sim.Outcome.Winner == MatchWinner.Home) == (ai == Team.Home)) aiWins++; else rndWins++;
            }
        }
        Console.WriteLine($"[진단] AI(시전 간격 {aiGap}초) vs 랜덤 {games}판: AI {aiWins}승 · 랜덤 {rndWins}승 · 평균 {time / games:0}초");
        Console.WriteLine($"  상대 타워를 부순 판: AI {aiTowerKills} · 랜덤 {rndTowerKills}");
        for (int s = 0; s < 2; s++)
        {
            int total = 0;
            foreach (var kv in sink.Casts[s]) total += kv.Value;
            Console.WriteLine($"  {(s == 0 ? "AI" : "랜덤")} 시전 {total}회 (판당 {(float)total / games:0.0}): " +
                              string.Join(" ", System.Linq.Enumerable.Select(System.Linq.Enumerable.OrderByDescending(sink.Casts[s], kv => kv.Value), kv => $"{kv.Key}:{kv.Value}")));
            Console.WriteLine($"     거절: " + string.Join(" ", System.Linq.Enumerable.Select(sink.Rejects[s], kv => $"{kv.Key}:{kv.Value}")));
        }
        return 0;
    }

    // ── 가중치 찾기 (실제 전투 코드로 봇끼리 대전시켜 승률이 오르는 쪽으로) ─────────────

    /// <summary>AI(weights) vs 랜덤, 같은 시전 속도. 승 1 · 무 · 시간 초과 0.5 · 패 0 의 평균.
    /// 전투 코드가 정적 버퍼(예: SplashBehavior.s_buffer)를 쓰므로 판은 한 번에 하나씩 (Unity 처럼 한 스레드).</summary>
    private static double WinRate(AiBrainWeights w, int firstSeed, int games, float aiGap, Brain opponent = Brain.Random, AiBrainWeights oppW = null)
    {
        var rules = MatchRuleConfig.CreateDefault();
        var catalog = SpellCatalog.CreateDefault();
        var arena = ArenaGeometry.CreateDefault();
        double total = 0;
        for (int g = 0; g < games; g++)
        {
            uint seed = (uint)(7919 * (firstSeed + g));
            Team ai = g % 2 == 0 ? Team.Home : Team.Away, other = TeamUtil.Opponent(ai);
            var cfg = BotConfig.CreateDefault();
            cfg.AiCastGapSeconds = aiGap;
            cfg.UseServerStrategy = false;
            cfg.ReportExperience = false;
            var sim = new MatchSimulation(rules, catalog, arena, cfg, null, null, null, seed) { LogCasts = false };
            sim.JoinBot(ai, new AiStrategyBrain(ai, sim.GetDeck(ai), catalog, cfg, seed + 11, null, w));
            sim.JoinBot(other, opponent == Brain.Ai
                ? new AiStrategyBrain(other, sim.GetDeck(other), catalog, cfg, seed + 23, null, oppW)
                : (IBotBrain)new TimerRandomBrain(sim.GetDeck(other), catalog, cfg, seed + 23));
            while (sim.Phase != MatchPhase.Ended && sim.MatchTime < 900f) sim.Tick(rules.LocalTickInterval);
            double r = 0.5;
            if (sim.Phase == MatchPhase.Ended && (sim.Outcome.Winner == MatchWinner.Home || sim.Outcome.Winner == MatchWinner.Away))
                r = (sim.Outcome.Winner == MatchWinner.Home) == (ai == Team.Home) ? 1 : 0;
            total += r;
        }
        return total / games;
    }

    private static readonly SpellBehaviorType[] Tunable =
    {
        SpellBehaviorType.MultiShotFrontFirst, SpellBehaviorType.Splash, SpellBehaviorType.ShieldRegen, SpellBehaviorType.BlindShot,
        SpellBehaviorType.PoisonField, SpellBehaviorType.FireballLob, SpellBehaviorType.ArrowVolley, SpellBehaviorType.TornadoLane,
        SpellBehaviorType.LightningStrikes, SpellBehaviorType.TowerBuff, SpellBehaviorType.Focus, SpellBehaviorType.SummonUnit,
        SpellBehaviorType.PlaceStructure, SpellBehaviorType.FreezeAll, SpellBehaviorType.LaserBeam, SpellBehaviorType.DrainLife,
        SpellBehaviorType.ReflectBarrier, SpellBehaviorType.RageField,
    };

    private static int Tune(int games, int rounds)
    {
        const float pace = 6f;           // 랜덤 봇과 같은 시전 속도 — 판단의 질만 비교
        var w = new AiBrainWeights();
        double best = WinRate(w, 1, games, pace);
        Console.WriteLine($"[tune] 시작 승률 {best:P1} (훈련 시드 1~{games}, 시전 간격 {pace}초)");
        var sw = Stopwatch.StartNew();
        for (int round = 1; round <= rounds; round++)
        {
            bool changed = false;
            foreach (var b in Tunable)
            {
                int i = (int)b;
                float cur = w.Scale[i];
                foreach (float f in new[] { 0f, 0.25f, 0.5f, 2f, 4f })
                {
                    var trial = w.Clone();
                    trial.Scale[i] = f == 0f ? 0f : (cur == 0f ? f : cur * f);
                    double r = WinRate(trial, 1, games, pace);
                    if (r > best + 0.0075)
                    {
                        best = r; w = trial; changed = true;
                        Console.WriteLine($"  [{round}] {b} ×{w.Scale[i]:0.###} → {best:P1}");
                    }
                }
            }
            foreach (var (name, values) in new (string, float[])[]
                     {
                         ("NormalNeed", new[] { 0f, 0.4f, 1.6f, 3f }), ("HighPowerNeed", new[] { 1f, 2f, 6f, 10f }),
                         ("DefenseBias", new[] { 0f, 0.5f, 2f }), ("SummonPush", new[] { 0.5f, 1f }),
                     })
            {
                foreach (float v in values)
                {
                    var trial = w.Clone();
                    Set(trial, name, v);
                    double r = WinRate(trial, 1, games, pace);
                    if (r > best + 0.0075)
                    {
                        best = r; w = trial; changed = true;
                        Console.WriteLine($"  [{round}] {name} = {v} → {best:P1}");
                    }
                }
            }
            Console.WriteLine($"[tune] {round}번째 끝: 승률 {best:P1} · {sw.Elapsed.TotalSeconds:0}초");
            if (!changed) break;
        }

        var lines = new System.Collections.Generic.List<string>();
        foreach (var b in Tunable)
            if (Math.Abs(w.Scale[(int)b] - 1f) > 1e-4f) lines.Add($"            w.Scale[(int)SpellBehaviorType.{b}] = {w.Scale[(int)b].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}f;");
        lines.Add($"            w.NormalNeed = {w.NormalNeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}f;");
        lines.Add($"            w.HighPowerNeed = {w.HighPowerNeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}f;");
        lines.Add($"            w.DefenseBias = {w.DefenseBias.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}f;");
        lines.Add($"            w.SummonPush = {w.SummonPush.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}f;");
        System.IO.File.WriteAllLines("tuned_weights.txt", lines);
        Console.WriteLine("[tune] 결과 (tuned_weights.txt):\n" + string.Join("\n", lines));

        // 훈련에 안 쓴 시드로 확인 (과적합 확인)
        double heldOld = WinRate(new AiBrainWeights(), 50001, 600, pace), heldNew = WinRate(w, 50001, 600, pace);
        Console.WriteLine($"[검증 · 새 시드 600판, 같은 시전 속도] 조정 전 {heldOld:P1} → 조정 후 {heldNew:P1}");
        return 0;
    }

    private static void Set(AiBrainWeights w, string name, float v)
    {
        if (name == "NormalNeed") w.NormalNeed = v;
        else if (name == "HighPowerNeed") w.HighPowerNeed = v;
        else if (name == "DefenseBias") w.DefenseBias = v;
        else if (name == "SummonPush") w.SummonPush = v;
    }

    private static float Score(MatchSimulation sim, Team t)
    {
        var p = sim.GetPlayer(t);
        return p.Hp + (sim.Defense.TowerAlive(t) ? 600f : 0f) + (sim.Defense.NexusAlive(t) ? 900f : 0f);
    }

    private static IBotBrain Make(Brain kind, Team team, MatchSimulation sim, SpellCatalog catalog, BotConfig cfg, uint seed)
    {
        // JoinBot 이 덱을 sim.GetDeck(team) 배열에 채운 뒤 두뇌가 그 배열을 본다
        int[] deck = sim.GetDeck(team);
        return kind == Brain.Ai
            ? new AiStrategyBrain(team, deck, catalog, cfg, seed)
            : new TimerRandomBrain(deck, catalog, cfg, seed);
    }
}
