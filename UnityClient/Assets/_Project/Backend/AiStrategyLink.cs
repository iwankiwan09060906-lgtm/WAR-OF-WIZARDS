// AI 전략 봇(AiStrategyBrain) ↔ 백엔드 AI 서버 연결 — Bot/IStrategyAdvisor 구현.
//
//   · 전략   : 봇이 몇 초마다 지금 상황(BotObservation + 레인 유닛 + 유닛 점 + 두뇌 후보 botPick)을 POST /api/ai/strategy 로 보낸다
//              (brief — 짧은 답이 빨리 온다). 서버는 전장 지도를 그려 VLM(Claude · 로컬 · 미세조정한 모델)에 묻고,
//              판 결과로 배운 것과 함께 작전(orders) + 한 수를 돌려준다.
//              작전은 유지 시간 동안 두뇌가 0.25초마다 적용하고(하이브리드), 한 수는 요청한 지 5초 안 · 슬롯 준비됨이면 쓴다.
//              VLM 이 정한 작전이 살아 있는 동안 규칙으로 만든 작전(VLM 이 바쁠 때의 답)은 덮어쓰지 않는다. 서버가 없으면 봇 혼자 판단한다.
//   · 학습   : 봇의 판단(상황 + 행동)을 모아 POST /api/ai/experience 로 보내고, 경기가 끝나면 결과(WIN · LOSE · DRAW)를
//              붙인다 → 서버가 자동으로 다시 학습한다 (Docs/Backend/AiLearning.md).
//   · 인증   : X-Server-Key (서버 설정 WOW_SERVER_KEY). 명령줄 -wowServerKey 키 로 바꿀 수 있다. 로그인 토큰이 있으면 같이 보낸다.
//
// 씬에 붙일 필요 없음 — 게임이 시작될 때 스스로 만들어져 StrategyAdvisors.Current 에 들어간다.
// 끄기: BotConfig 의 UseServerStrategy · ReportExperience, 또는 명령줄 -noAiLink.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;
using UnityEngine.Networking;

namespace SpellboundVR.Backend
{
    public sealed class AiStrategyLink : MonoBehaviour, IStrategyAdvisor
    {
        private const string DefaultServerKey = "wow-server-secret-key-2026";
        private const int MaxStepsPerPost = 50;
        private const float FlushInterval = 3f;

        [Serializable] private class LaneDto { public int allies; public int enemies; public float enemyFront; public float clusterDepth; public float allyFront; public int nearBase; }
        [Serializable] private class UnitDto { public int c; public float d; public int e; }
        [Serializable] private class PickDto { public int slot; public string spellId; public int column; public float depth; public float score; public string why; }

        [Serializable]
        private class StateDto
        {
            public string[] deck;
            public bool[] ready;
            public int selfHp;
            public int opponentHp;
            public int selfColumn;
            public int opponentColumn;
            public float time;
            public bool selfTowerAlive;
            public bool selfNexusAlive;
            public bool opponentTowerAlive;
            public bool opponentNexusAlive;
            public int incomingProjectileCount;
            public int nearestIncomingProjectileColumn;
            public float nearestIncomingProjectileEta;
            public LaneDto[] lanes;
            public UnitDto[] units;
            public PickDto[] botPick;
            public int players = 2;
            public string mode = "bot";
        }

        [Serializable] private class StrategyRequestDto { public StateDto state; public string botId; public string gameId; public bool record; public bool brief = true; }
        [Serializable] private class OrdersDto { public int lane = -1; public string stance; public string[] prefer; public string[] hold; public float seconds; }
        [Serializable] private class CastDto { public int slot = -1; public string spellId; public int column = -1; public float depth = 0.5f; }
        [Serializable] private class MoveDto { public int direction; }
        [Serializable] private class StrategyResponseDto { public CastDto cast; public MoveDto move; public OrdersDto orders; public string source; public string summary; }

        [Serializable] private class ActionDto { public string spellId = ""; public int slot = -1; public int column = -1; public float depth; public int move; public bool followedAdvice; public bool byOrders; }
        [Serializable] private class StepDto { public StateDto state; public ActionDto action; public float reward; }
        [Serializable] private class ExperienceDto { public string gameId; public string botId; public string source = "unity-bot"; public StepDto[] steps; }
        [Serializable] private class OutcomeDto { public string gameId; public string botId; public string outcome; public int place; public int players = 2; }

        private class TeamState
        {
            public MatchSimulation Sim;
            public int MatchSerial = -1;
            public string GameId;
            public bool HasAdvice;
            public StrategyAdvice Advice;
            public bool HasOrders;
            public StrategyOrders Orders;
            public bool RequestInFlight;
            public bool OutcomeSent;
            public readonly List<StepDto> Steps = new List<StepDto>();
            public float NextFlush;
        }

        private static AiStrategyLink _instance;
        private readonly TeamState[] _teams = { new TeamState(), new TeamState() };
        private readonly string _session = Guid.NewGuid().ToString("N").Substring(0, 8);
        private string _serverKey = DefaultServerKey;
        private int _failures;
        private float _pauseUntil;

        public static AiStrategyLink Instance => _instance;

        /// <summary>봇이 만들어지기 전에 연결을 준비한다 (StrategyAdvisors.Current)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null || HasArg("-noAiLink")) return;
            var go = new GameObject("[AiStrategyLink]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AiStrategyLink>();
            StrategyAdvisors.Current = _instance;
        }

        private void Awake()
        {
            string key = ArgValue("-wowServerKey");
            if (!string.IsNullOrEmpty(key)) _serverKey = key;
        }

        // ── IStrategyAdvisor ─────────────────────────────────

        public void Track(MatchSimulation sim, Team team)
        {
            var ts = _teams[(int)team];
            if (ts.Sim == sim && ts.MatchSerial == sim.MatchSerial) return;
            FlushSteps(team);                     // 지난 경기 기록은 지난 gameId 로
            ts.Sim = sim;
            ts.MatchSerial = sim.MatchSerial;
            ts.GameId = $"bot-{_session}-{sim.MatchSerial}";
            ts.HasAdvice = false;
            ts.HasOrders = false;
            ts.OutcomeSent = false;
        }

        public void RequestAdvice(Team team, in BotObservation obs, BotView view, int[] deck)
        {
            var ts = _teams[(int)team];
            if (ts.RequestInFlight || !Online || ts.Sim == null) return;
            var body = new StrategyRequestDto { state = BuildState(ts.Sim, obs, view, deck, true), botId = BotId(team), gameId = ts.GameId, record = false };
            ts.RequestInFlight = true;
            int serial = ts.MatchSerial;
            float requestedAt = ts.Sim.MatchTime;
            StartCoroutine(Post("/api/ai/strategy", JsonUtility.ToJson(body), text =>
            {
                ts.RequestInFlight = false;
                if (ts.MatchSerial != serial || ts.Sim == null) return;
                StrategyResponseDto res;
                try { res = JsonUtility.FromJson<StrategyResponseDto>(text); }
                catch (Exception) { return; }
                if (res == null) return;
                if (res.orders != null) SetOrders(ts, res.orders, res.source, requestedAt);
                if (res.cast == null || res.cast.slot < 0) { ts.HasAdvice = false; return; }
                ts.Advice = new StrategyAdvice
                {
                    Slot = res.cast.slot,
                    SpellId = SpellIdOf(res.cast.spellId),
                    Column = (Column)Mathf.Clamp(res.cast.column < 0 ? 1 : res.cast.column, 0, 2),
                    Depth = res.cast.depth,
                    Move = (sbyte)Mathf.Clamp(res.move != null ? res.move.direction : 0, -1, 1),
                    RequestedAt = requestedAt,
                    ReceivedAt = ts.Sim.MatchTime,
                };
                ts.HasAdvice = true;
            }, () => ts.RequestInFlight = false));
        }

        public bool TryGetAdvice(Team team, out StrategyAdvice advice)
        {
            var ts = _teams[(int)team];
            advice = ts.Advice;
            if (!ts.HasAdvice) return false;
            ts.HasAdvice = false;                 // 한 번 쓴 수는 다시 쓰지 않는다
            return true;
        }

        public bool TryGetOrders(Team team, float now, out StrategyOrders orders)
        {
            var ts = _teams[(int)team];
            orders = ts.Orders;
            return ts.HasOrders && now < ts.Orders.Until;
        }

        /// <summary>VLM 이 정한 작전이 살아 있으면 규칙 작전(VLM 이 바쁘거나 실패했을 때의 답)으로 덮어쓰지 않는다</summary>
        private static void SetOrders(TeamState ts, OrdersDto o, string source, float requestedAt)
        {
            bool fromModel = !string.IsNullOrEmpty(source) && source != "rules";
            if (!fromModel && ts.HasOrders && ts.Orders.FromModel && ts.Sim.MatchTime < ts.Orders.Until) return;
            ts.Orders = new StrategyOrders
            {
                Lane = o.lane >= 0 && o.lane <= 2 ? o.lane : -1,
                Stance = o.stance == "defend" ? -1 : o.stance == "push" ? 1 : 0,
                Prefer = SpellIdsOf(o.prefer),
                Hold = SpellIdsOf(o.hold),
                Until = requestedAt + Mathf.Clamp(o.seconds > 0f ? o.seconds : 8f, 3f, 15f),
                FromModel = fromModel,
            };
            ts.HasOrders = true;
        }

        public void ReportDecision(Team team, in BotObservation obs, BotView view, int[] deck, in SpellCastRequest cast, bool hasCast,
                                   sbyte move, bool followedAdvice, bool byOrders)
        {
            var ts = _teams[(int)team];
            if (ts.Sim == null || !Online) return;
            ts.Steps.Add(new StepDto
            {
                state = BuildState(ts.Sim, obs, view, deck, false),
                action = new ActionDto
                {
                    spellId = hasCast ? SpellIds.Code(cast.SpellId) : "",
                    slot = hasCast ? cast.SlotIndex : -1,
                    column = hasCast ? (int)cast.TargetColumn : -1,
                    depth = hasCast ? cast.TargetDepth : 0f,
                    move = move,
                    followedAdvice = followedAdvice,
                    byOrders = byOrders,
                },
            });
            if (ts.Steps.Count >= MaxStepsPerPost) FlushSteps(team);
        }

        // ── 경기 결과 · 묶어 보내기 ──────────────────────────

        private void Update()
        {
            for (int t = 0; t < _teams.Length; t++)
            {
                var ts = _teams[t];
                if (ts.Sim == null) continue;
                if (ts.Steps.Count > 0 && Time.unscaledTime >= ts.NextFlush) FlushSteps((Team)t);
                if (!ts.OutcomeSent && ts.Sim.Phase == MatchPhase.Ended && ts.Sim.MatchSerial == ts.MatchSerial)
                {
                    ts.OutcomeSent = true;
                    FlushSteps((Team)t);
                    var w = ts.Sim.Outcome.Winner;
                    string outcome = w == MatchWinner.Draw || w == MatchWinner.Void || w == MatchWinner.None ? "DRAW"
                        : (w == MatchWinner.Home) == (t == (int)Team.Home) ? "WIN" : "LOSE";
                    if (Online)
                    {
                        var body = new OutcomeDto { gameId = ts.GameId, botId = BotId((Team)t), outcome = outcome, place = outcome == "WIN" ? 1 : 2 };
                        StartCoroutine(Post("/api/ai/experience", JsonUtility.ToJson(body), null, null));
                    }
                }
            }
        }

        private void FlushSteps(Team team)
        {
            var ts = _teams[(int)team];
            ts.NextFlush = Time.unscaledTime + FlushInterval;
            if (ts.Steps.Count == 0 || string.IsNullOrEmpty(ts.GameId)) return;
            var body = new ExperienceDto { gameId = ts.GameId, botId = BotId(team), steps = ts.Steps.ToArray() };
            ts.Steps.Clear();
            if (Online) StartCoroutine(Post("/api/ai/experience", JsonUtility.ToJson(body), null, null));
        }

        // ── HTTP ─────────────────────────────────────────────

        /// <summary>서버가 계속 안 받으면 30초 쉬었다가 다시 (오프라인 · 서버 꺼짐에서 로그가 쏟아지지 않게)</summary>
        private bool Online => Time.unscaledTime >= _pauseUntil && !string.IsNullOrEmpty(BackendClient.Instance.ServerUrl);

        private IEnumerator Post(string path, string json, Action<string> onText, Action onFail)
        {
            using (var req = new UnityWebRequest(BackendClient.Instance.ServerUrl + path, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-Server-Key", _serverKey);
                req.timeout = 10;
                UnityWebRequestAsyncOperation op;
                try { op = req.SendWebRequest(); }
                catch (Exception) { Fail(); onFail?.Invoke(); yield break; }
                yield return op;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    _failures = 0;
                    onText?.Invoke(req.downloadHandler.text);
                }
                else
                {
                    Fail();
                    onFail?.Invoke();
                }
            }
        }

        private void Fail()
        {
            if (++_failures >= 3)
            {
                _failures = 0;
                _pauseUntil = Time.unscaledTime + 30f;
                Debug.Log("[AiStrategyLink] AI 서버에 연결하지 못해 30초 동안 봇 혼자 판단합니다.");
            }
        }

        // ── 도우미 ───────────────────────────────────────────

        /// <summary>withPicks: 전략 요청에는 두뇌 후보(botPick)를, 학습 기록에는 유닛 점까지만 (데이터셋은 저장한 상황으로 지도를 다시 그린다)</summary>
        private static StateDto BuildState(MatchSimulation sim, in BotObservation obs, BotView view, int[] deck, bool withPicks)
        {
            var lanes = view.Lanes;
            var s = new StateDto
            {
                deck = new string[deck.Length],
                ready = new bool[deck.Length],
                selfHp = obs.SelfHp,
                opponentHp = obs.OpponentHp,
                selfColumn = (int)obs.SelfColumn,
                opponentColumn = (int)obs.OpponentColumn,
                time = obs.MatchTimeSeconds,
                selfTowerAlive = obs.SelfTowerAlive,
                selfNexusAlive = obs.SelfNexusAlive,
                opponentTowerAlive = obs.OpponentTowerAlive,
                opponentNexusAlive = obs.OpponentNexusAlive,
                incomingProjectileCount = obs.IncomingProjectileCount,
                nearestIncomingProjectileColumn = (int)obs.NearestIncomingProjectileColumn,
                nearestIncomingProjectileEta = obs.NearestIncomingProjectileEta,
                lanes = new LaneDto[lanes.Length],
            };
            for (int i = 0; i < deck.Length; i++)
            {
                s.deck[i] = deck[i] > 0 ? SpellIds.Code(deck[i]) : "";
                s.ready[i] = (obs.SelfSlotReadyMask & (1 << i)) != 0;
            }
            for (int c = 0; c < lanes.Length; c++)
                s.lanes[c] = new LaneDto
                {
                    allies = lanes[c].Allies, enemies = lanes[c].Enemies, enemyFront = lanes[c].EnemyFront, clusterDepth = lanes[c].ClusterDepth,
                    allyFront = lanes[c].AllyFront, nearBase = lanes[c].EnemiesNearMyBase,
                };
            s.units = new UnitDto[view.UnitCount];
            for (int i = 0; i < view.UnitCount; i++)
                s.units[i] = new UnitDto { c = view.Units[i].Column, d = Mathf.Round(view.Units[i].Depth * 1000f) / 1000f, e = view.Units[i].Enemy ? 1 : 0 };
            s.botPick = new PickDto[withPicks ? view.CandidateCount : 0];
            for (int i = 0; i < s.botPick.Length; i++)
            {
                var c = view.Candidates[i];
                s.botPick[i] = new PickDto { slot = c.Slot, spellId = SpellIds.Code(c.SpellId), column = (int)c.Column, depth = c.Depth, score = c.Score, why = c.Why };
            }
            return s;
        }

        private static int[] SpellIdsOf(string[] codes)
        {
            if (codes == null) return Array.Empty<int>();
            var ids = new int[codes.Length];
            for (int i = 0; i < codes.Length; i++) ids[i] = SpellIdOf(codes[i]);
            return ids;
        }

        private static int SpellIdOf(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 3 || !int.TryParse(code.Substring(1), out int n)) return 0;
            return code[0] == 'H' ? 100 + n : n;
        }

        private string BotId(Team team) => $"{_session}-{team}";

        private static bool HasArg(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
