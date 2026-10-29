// BotLab 봇을 진짜 AI 서버(Backend/wow_server.py → VLM)에 연결한다 — Unity 의 AiStrategyLink 와 같은 요청 · 같은 응답.
//
// BotLab 은 경기를 시뮬레이션 시간으로 돌리므로 서버 답을 기다리는 동안 경기 시간이 멈춘다.
// 대신 실제로 걸린 시간(L)만큼 경기 시간이 흐른 뒤에야 그 답(작전 · 한 수)을 쓴다 → 실제 게임과 같은 지연.
// 답을 기다리는 동안(경기 시간 기준)에는 새 요청을 보내지 않는다 (Unity 의 RequestInFlight 와 같다).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Network;
using SpellboundVR.Spells;

internal sealed class RemoteAdvisor : IStrategyAdvisor
{
    private sealed class TeamState
    {
        public MatchSimulation Sim;
        public string GameId = "";
        public float BusyUntil;
        public bool HasPendingAdvice;
        public StrategyAdvice PendingAdvice;
        public float AdviceAt;
        public bool HasPendingOrders;
        public StrategyOrders PendingOrders;
        public float OrdersAt;
        public bool HasOrders;
        public StrategyOrders Orders;
        public readonly List<JsonObject> Steps = new List<JsonObject>();
        // 집계
        public int Casts, ByAdvice, ByOrders;
    }

    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    private readonly string _base;
    private readonly string _key;
    private readonly string _session = Guid.NewGuid().ToString("N").Substring(0, 6);
    private readonly TeamState[] _teams = { new TeamState(), new TeamState() };
    private int _game;

    public int Calls, Failures;
    public double LatencySum, LatencyMax;
    public readonly Dictionary<string, int> Sources = new Dictionary<string, int>();
    public readonly List<string> Errors = new List<string>();

    public RemoteAdvisor(string baseUrl, string serverKey)
    {
        _base = baseUrl.TrimEnd('/');
        _key = serverKey;
    }

    public string BotId(Team team) => $"lab{_session}-{team}";
    public (int casts, int byAdvice, int byOrders) Stats(Team team) => (_teams[(int)team].Casts, _teams[(int)team].ByAdvice, _teams[(int)team].ByOrders);

    // ── IStrategyAdvisor ─────────────────────────────────

    public void Track(MatchSimulation sim, Team team)
    {
        var ts = _teams[(int)team];
        if (ts.Sim == sim) return;
        _teams[(int)team] = ts = new TeamState { Sim = sim, GameId = $"lab-{_session}-{_game}" };
    }

    public void RequestAdvice(Team team, in BotObservation obs, BotView view, int[] deck)
    {
        var ts = _teams[(int)team];
        float now = obs.MatchTimeSeconds;
        if (ts.Sim == null || now < ts.BusyUntil) return;
        var body = new JsonObject
        {
            ["state"] = BuildState(obs, view, deck, true), ["botId"] = BotId(team), ["gameId"] = ts.GameId,
            ["record"] = false, ["brief"] = true,
        };
        var sw = Stopwatch.StartNew();
        JsonNode res = Post("/api/ai/strategy", body);
        double latency = sw.Elapsed.TotalSeconds;
        Calls++;
        LatencySum += latency;
        LatencyMax = Math.Max(LatencyMax, latency);
        float ready = now + (float)latency;          // 이만큼 경기 시간이 흐른 뒤에 답이 도착한다
        ts.BusyUntil = ready;
        if (res == null) return;
        string source = (string)res["source"] ?? "?";
        Sources[source] = Sources.TryGetValue(source, out int n) ? n + 1 : 1;
        var o = res["orders"];
        if (o != null)
        {
            ts.PendingOrders = new StrategyOrders
            {
                Lane = (int?)o["lane"] ?? -1,
                Stance = (string)o["stance"] == "defend" ? -1 : (string)o["stance"] == "push" ? 1 : 0,
                Prefer = Ids(o["prefer"] as JsonArray),
                Hold = Ids(o["hold"] as JsonArray),
                Until = now + Math.Clamp((float?)o["seconds"] ?? 8f, 3f, 15f),
                FromModel = source != "rules",
            };
            ts.HasPendingOrders = true;
            ts.OrdersAt = ready;
        }
        var c = res["cast"];
        int slot = (int?)c?["slot"] ?? -1;
        if (slot >= 0)
        {
            ts.PendingAdvice = new StrategyAdvice
            {
                Slot = slot,
                SpellId = Id((string)c["spellId"]),
                Column = (Column)Math.Clamp((int?)c["column"] is int col && col >= 0 ? col : 1, 0, 2),
                Depth = (float?)c["depth"] ?? 0.5f,
                Move = (sbyte)Math.Clamp((int?)res["move"]?["direction"] ?? 0, -1, 1),
                RequestedAt = now,
                ReceivedAt = ready,
            };
            ts.HasPendingAdvice = true;
            ts.AdviceAt = ready;
        }
        else ts.HasPendingAdvice = false;
    }

    public bool TryGetAdvice(Team team, out StrategyAdvice advice)
    {
        var ts = _teams[(int)team];
        advice = ts.PendingAdvice;
        if (!ts.HasPendingAdvice || ts.Sim == null || ts.Sim.MatchTime < ts.AdviceAt) return false;
        ts.HasPendingAdvice = false;
        return true;
    }

    public bool TryGetOrders(Team team, float now, out StrategyOrders orders)
    {
        var ts = _teams[(int)team];
        if (ts.HasPendingOrders && now >= ts.OrdersAt)
        {
            ts.HasPendingOrders = false;
            // VLM 작전이 살아 있으면 규칙 작전으로 덮어쓰지 않는다 (AiStrategyLink 와 같은 규칙)
            if (ts.PendingOrders.FromModel || !(ts.HasOrders && ts.Orders.FromModel && now < ts.Orders.Until))
            {
                ts.Orders = ts.PendingOrders;
                ts.HasOrders = true;
            }
        }
        orders = ts.Orders;
        return ts.HasOrders && now < ts.Orders.Until;
    }

    public void ReportDecision(Team team, in BotObservation obs, BotView view, int[] deck, in SpellCastRequest cast, bool hasCast, sbyte move,
                               bool followedAdvice, bool byOrders)
    {
        var ts = _teams[(int)team];
        if (hasCast)
        {
            ts.Casts++;
            if (followedAdvice) ts.ByAdvice++;
            if (byOrders) ts.ByOrders++;
        }
        ts.Steps.Add(new JsonObject
        {
            ["state"] = BuildState(obs, view, deck, false),
            ["action"] = new JsonObject
            {
                ["spellId"] = hasCast ? SpellIds.Code(cast.SpellId) : "", ["slot"] = hasCast ? cast.SlotIndex : -1,
                ["column"] = hasCast ? (int)cast.TargetColumn : -1, ["depth"] = hasCast ? cast.TargetDepth : 0f, ["move"] = (int)move,
                ["followedAdvice"] = followedAdvice, ["byOrders"] = byOrders,
            },
            ["reward"] = 0,
        });
    }

    /// <summary>경기가 끝나면: 두 봇의 판단을 서버 학습 데이터로 보내고 결과 이름표를 붙인다</summary>
    public void FinishGame(MatchSimulation sim)
    {
        var w = sim.Phase == MatchPhase.Ended ? sim.Outcome.Winner : MatchWinner.Draw;
        for (int t = 0; t < _teams.Length; t++)
        {
            var ts = _teams[t];
            if (ts.Sim != sim) continue;
            for (int i = 0; i < ts.Steps.Count; i += 100)
            {
                var steps = new JsonArray();
                for (int k = i; k < Math.Min(ts.Steps.Count, i + 100); k++) steps.Add(ts.Steps[k]);
                Post("/api/ai/experience", new JsonObject { ["gameId"] = ts.GameId, ["botId"] = BotId((Team)t), ["source"] = "botlab", ["steps"] = steps });
            }
            string outcome = w == MatchWinner.Home || w == MatchWinner.Away ? ((w == MatchWinner.Home) == (t == (int)Team.Home) ? "WIN" : "LOSE") : "DRAW";
            Post("/api/ai/experience", new JsonObject
            {
                ["gameId"] = ts.GameId, ["botId"] = BotId((Team)t), ["outcome"] = outcome, ["place"] = outcome == "WIN" ? 1 : 2, ["players"] = 2,
            });
            ts.Steps.Clear();
        }
        _game++;
    }

    // ── 도우미 ───────────────────────────────────────────

    private JsonNode Post(string path, JsonObject body)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _base + path)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Server-Key", _key);
            using var resp = _http.Send(req);
            string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                Failures++;
                if (Errors.Count < 5) Errors.Add($"{path} {(int)resp.StatusCode}: {text.Substring(0, Math.Min(200, text.Length))}");
                return null;
            }
            return JsonNode.Parse(text);
        }
        catch (Exception e)
        {
            Failures++;
            if (Errors.Count < 5) Errors.Add($"{path}: {e.GetType().Name} {e.Message}");
            return null;
        }
    }

    private static JsonObject BuildState(in BotObservation obs, BotView view, int[] deck, bool withPicks)
    {
        var deckArr = new JsonArray();
        var ready = new JsonArray();
        for (int i = 0; i < deck.Length; i++)
        {
            deckArr.Add(deck[i] > 0 ? SpellIds.Code(deck[i]) : "");
            ready.Add((obs.SelfSlotReadyMask & (1 << i)) != 0);
        }
        var lanes = new JsonArray();
        foreach (var l in view.Lanes)
            lanes.Add(new JsonObject
            {
                ["allies"] = l.Allies, ["enemies"] = l.Enemies, ["enemyFront"] = R(l.EnemyFront), ["clusterDepth"] = R(l.ClusterDepth),
                ["allyFront"] = R(l.AllyFront), ["nearBase"] = l.EnemiesNearMyBase,
            });
        var units = new JsonArray();
        for (int i = 0; i < view.UnitCount; i++)
            units.Add(new JsonObject { ["c"] = (int)view.Units[i].Column, ["d"] = R(view.Units[i].Depth), ["e"] = view.Units[i].Enemy ? 1 : 0 });
        var picks = new JsonArray();
        for (int i = 0; withPicks && i < view.CandidateCount; i++)
        {
            var c = view.Candidates[i];
            picks.Add(new JsonObject
            {
                ["slot"] = c.Slot, ["spellId"] = SpellIds.Code(c.SpellId), ["column"] = (int)c.Column, ["depth"] = R(c.Depth),
                ["score"] = R(c.Score), ["why"] = c.Why,
            });
        }
        return new JsonObject
        {
            ["deck"] = deckArr, ["ready"] = ready, ["selfHp"] = obs.SelfHp, ["opponentHp"] = obs.OpponentHp,
            ["selfColumn"] = (int)obs.SelfColumn, ["opponentColumn"] = (int)obs.OpponentColumn, ["time"] = R(obs.MatchTimeSeconds),
            ["selfTowerAlive"] = obs.SelfTowerAlive, ["selfNexusAlive"] = obs.SelfNexusAlive,
            ["opponentTowerAlive"] = obs.OpponentTowerAlive, ["opponentNexusAlive"] = obs.OpponentNexusAlive,
            ["incomingProjectileCount"] = obs.IncomingProjectileCount,
            ["nearestIncomingProjectileColumn"] = (int)obs.NearestIncomingProjectileColumn,
            ["nearestIncomingProjectileEta"] = R(obs.NearestIncomingProjectileEta),
            ["lanes"] = lanes, ["units"] = units, ["botPick"] = picks, ["players"] = 2, ["mode"] = "botlab",
        };
    }

    private static float R(float v) => (float)Math.Round(v, 3);

    private static int Id(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length < 3 || !int.TryParse(code.Substring(1), out int n)) return 0;
        return code[0] == 'H' ? 100 + n : n;
    }

    private static int[] Ids(JsonArray a)
    {
        if (a == null) return Array.Empty<int>();
        var ids = new int[a.Count];
        for (int i = 0; i < a.Count; i++) ids[i] = Id((string)a[i]);
        return ids;
    }
}
