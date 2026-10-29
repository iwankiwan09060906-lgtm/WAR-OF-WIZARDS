// §20.4 AI 전략 봇 두뇌 — TimerRandomBrain(정해진 간격 · 랜덤 슬롯 · 랜덤 조준) 대신 전장을 보고 고른다.
//
//   1) 피하기   : 내 칸으로 날아오는 투사체가 곧 닿으면 옆 칸으로 (가운데 쪽 우선)
//   2) 시전     : 쓸 수 있는 슬롯마다 지금 가치를 매겨 가장 높은 것 하나
//                 · 범위 피해(파이어볼 · 화살 세례 · 번개 · 독 장판 · 토네이도 · 총난사) → 적 유닛이 가장 많이 겹치는 레인 · 깊이
//                 · 프리즈(H01) → 적이 몰렸거나 내 타워 앞이 위험할 때만 (고위력은 경기당 3회라 아낀다)
//                 · 레이저(H02) · 흡혈(H03) → 상대 타워가 무너져 피해 100% 일 때 상대가 선 열로, 아니면 유닛이 많은 레인
//                 · 쉴드(S08) · 반사(H04) → HP 가 낮거나 내 타워가 없어 피해를 그대로 맞을 때
//                 · 소환(S11 · S12) · 소환탑(H06) → 밀리는 레인 보강 / 밀고 있는 레인 가속, 터렛(S13) → 내 쪽으로 오는 적이 많은 레인
//                 · 분노(H05) → 우리 유닛이 3기 이상 모인 레인의 앞줄
//   3) 하이브리드 (Unity 두뇌 = 빠른 반사 · VLM = 전략, IStrategyAdvisor — Unity 에서는 Backend/AiStrategyLink)
//      · 몇 초마다 지금 후보(botPick) · 유닛 점(전장 지도용)을 서버(/api/ai/strategy)로 보낸다
//      · 서버(VLM · 판 결과로 배운 것)는 작전(orders: 집중 레인 · 자세 · 먼저 쓸 / 아낄 스킬 · 유지 시간)과 한 수를 돌려준다
//      · 작전은 유지 시간 동안 0.25초마다 위 2)의 점수에 곱한다 → VLM 이 느려도 전략은 이 두뇌 속도로 반응한다
//      · 한 수는 요청한 지 AdviceMaxAge 안이고 그 슬롯이 준비됐으면 쓴다. 단 두뇌 점수가 EmergencyScore 이상이면(위급) 두뇌가 먼저
//      · 서버가 없으면 이 두뇌 혼자 둔다
//
// 사람처럼 룬을 그리고 조준하는 시간이 있으므로 시전 사이에 BotConfig.AiCastGapSeconds 만큼 쉰다.
// 승률 측정: 저장소의 Tools/BotLab (Unity 없이 실제 MatchSimulation 으로 봇끼리 수백 판)

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells;
using SpellboundVR.Utils;
using UnityEngine;

namespace SpellboundVR.Bot
{
    /// <summary>BotPlayer 가 Tick 직전에 시뮬레이션을 보여 주는 두뇌 (계약서 IBotBrain 은 그대로 두고 레인 정보를 더 본다)</summary>
    public interface IWorldAwareBrain
    {
        void Observe(MatchSimulation sim, Team team);
    }

    /// <summary>열(내 시점) 하나의 전황</summary>
    public struct LaneInfo
    {
        public int Enemies;
        public int Allies;
        public float EnemyHp;
        /// <summary>내 쪽에 가장 가까운 적 유닛의 깊이 (0 = 내 발판, 1 = 상대 발판), 적이 없으면 1</summary>
        public float EnemyFront;
        /// <summary>적 유닛이 가장 많이 겹치는 깊이와 그 수 (반경 ClusterRadius)</summary>
        public float ClusterDepth;
        public int ClusterCount;
        /// <summary>상대 쪽으로 가장 멀리 나간 우리 유닛의 깊이, 없으면 0</summary>
        public float AllyFront;
        /// <summary>내 타워 사거리 근처까지 온 적 유닛 수</summary>
        public int EnemiesNearMyBase;
    }

    /// <summary>서버가 준 한 수 (슬롯 · 열은 봇 자신의 시점)</summary>
    public struct StrategyAdvice
    {
        public int Slot;
        public int SpellId;
        public Column Column;
        public float Depth;
        public sbyte Move;
        /// <summary>요청한 경기 시간 (나이는 여기서부터 잰다 — 답이 늦을수록 덜 믿는다)</summary>
        public float RequestedAt;
        public float ReceivedAt;
    }

    /// <summary>서버(VLM · 학습)가 준 작전 — Until 까지 0.25초마다 두뇌 점수에 곱한다</summary>
    public struct StrategyOrders
    {
        /// <summary>집중할 열 (봇 시점), 없으면 -1</summary>
        public int Lane;
        /// <summary>-1 지키기 · 0 보통 · 1 밀기</summary>
        public int Stance;
        public int[] Prefer;
        public int[] Hold;
        public float Until;
        /// <summary>VLM 이 정한 작전인지 (아니면 규칙 · 학습으로 만든 작전)</summary>
        public bool FromModel;

        public bool Prefers(int spellId) => Contains(Prefer, spellId);
        public bool Holds(int spellId) => Contains(Hold, spellId);

        private static bool Contains(int[] a, int id)
        {
            if (a == null) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] == id) return true;
            return false;
        }
    }

    /// <summary>전장 지도용 유닛 점 (봇 시점)</summary>
    public struct BotUnitDot
    {
        public byte Column;
        public float Depth;
        public bool Enemy;
    }

    /// <summary>두뇌가 매긴 후보 (작전을 곱하기 전 점수)</summary>
    public struct BotCandidate
    {
        public int Slot;
        public int SpellId;
        public Column Column;
        public float Depth;
        public float Score;
        public string Why;
    }

    /// <summary>두뇌가 본 전장 — 서버 요청 · 학습 기록에 같이 보낸다 (두뇌가 매 틱 다시 채우므로 받은 쪽은 바로 복사할 것)</summary>
    public sealed class BotView
    {
        public const int MaxUnits = 96;
        public const int MaxCandidates = 3;
        public LaneInfo[] Lanes;
        public readonly BotUnitDot[] Units = new BotUnitDot[MaxUnits];
        public int UnitCount;
        public readonly BotCandidate[] Candidates = new BotCandidate[MaxCandidates];
        public int CandidateCount;
    }

    /// <summary>서버 AI 전략 · 학습 데이터 연결 (Unity: Backend/AiStrategyLink). 모든 호출은 막히지 않아야 한다.</summary>
    public interface IStrategyAdvisor
    {
        /// <summary>봇마다 하나 — 경기 · 진영 정보를 알려 준다 (경기가 바뀌면 다시 불린다)</summary>
        void Track(MatchSimulation sim, Team team);
        void RequestAdvice(Team team, in BotObservation obs, BotView view, int[] deck);
        /// <summary>한 수 (한 번 쓰면 사라진다)</summary>
        bool TryGetAdvice(Team team, out StrategyAdvice advice);
        /// <summary>지금(now) 유효한 작전</summary>
        bool TryGetOrders(Team team, float now, out StrategyOrders orders);
        void ReportDecision(Team team, in BotObservation obs, BotView view, int[] deck, in SpellCastRequest cast, bool hasCast, sbyte move,
                            bool followedAdvice, bool byOrders);
    }

    /// <summary>봇이 쓸 서버 연결 (없으면 null — 두뇌 혼자 판단). Unity 에서는 AiStrategyLink 가 실행 시 채운다.</summary>
    public static class StrategyAdvisors
    {
        public static IStrategyAdvisor Current;
    }

    /// <summary>
    /// 스킬 종류(SpellBehaviorType)마다 "지금 쓸 가치" 배율과 시전 문턱.
    /// 기본값은 Tools/BotLab tune(실제 전투 코드로 봇끼리 수백 판)으로 승률이 가장 높게 나온 값이다.
    /// </summary>
    public sealed class AiBrainWeights
    {
        public const int BehaviorCount = 24;
        public readonly float[] Scale = new float[BehaviorCount];
        /// <summary>일반 스킬은 가치가 이 값 이상일 때만 쓴다</summary>
        public float NormalNeed = 0.8f;
        /// <summary>고위력(경기당 3회)은 가치가 이 값 이상일 때만 쓴다</summary>
        public float HighPowerNeed = 4f;
        /// <summary>범위 피해를 내 쪽으로 가까이 온 적에게 얼마나 우선하나 (0 = 거리 상관없이 많이 겹친 곳)</summary>
        public float DefenseBias = 1f;
        /// <summary>소환을 밀리는 레인(0)에 넣을지, 이미 밀고 있는 레인(1)에 넣어 타워를 부술지</summary>
        public float SummonPush = 0f;

        public AiBrainWeights()
        {
            for (int i = 0; i < Scale.Length; i++) Scale[i] = 1f;
        }

        public AiBrainWeights Clone()
        {
            var w = new AiBrainWeights { NormalNeed = NormalNeed, HighPowerNeed = HighPowerNeed, DefenseBias = DefenseBias, SummonPush = SummonPush };
            System.Array.Copy(Scale, w.Scale, Scale.Length);
            return w;
        }

        /// <summary>작전 자세(-1 지키기 · 1 밀기)를 반영한 가중치</summary>
        public AiBrainWeights WithStance(int stance)
        {
            var w = Clone();
            if (stance < 0)
            {
                w.DefenseBias = Mathf.Max(1f, DefenseBias * 2f);
                w.SummonPush = 0f;
                w.Scale[(int)SpellBehaviorType.ShieldRegen] *= 1.3f;
                w.Scale[(int)SpellBehaviorType.TowerBuff] *= 1.3f;
                w.Scale[(int)SpellBehaviorType.PlaceStructure] *= 1.2f;
            }
            else if (stance > 0)
            {
                w.DefenseBias = DefenseBias * 0.5f;
                w.SummonPush = 1f;
                w.Scale[(int)SpellBehaviorType.RageField] *= 1.3f;
                w.Scale[(int)SpellBehaviorType.SummonUnit] *= 1.2f;
            }
            return w;
        }

        private static AiBrainWeights _default;
        public static AiBrainWeights Default => _default ?? (_default = CreateTuned());

        private static AiBrainWeights CreateTuned()
        {
            var w = new AiBrainWeights();
            // TUNED-BEGIN — Tools/BotLab tune 300판 × 3바퀴 (2026-10-26). 랜덤 봇과 같은 시전 속도에서 승률 41% → 90% (새 시드 600판)
            // 지금 밸런스에서는 레인 하나 청소용 피해(S07 · S04 · S06 · S01)보다 소환 · 독 장판이 타워를 부수는 데 훨씬 낫다.
            w.Scale[(int)SpellBehaviorType.MultiShotFrontFirst] = 0f;
            w.Scale[(int)SpellBehaviorType.BlindShot] = 0f;
            w.Scale[(int)SpellBehaviorType.PoisonField] = 2f;
            w.Scale[(int)SpellBehaviorType.ArrowVolley] = 0f;
            w.Scale[(int)SpellBehaviorType.LightningStrikes] = 0f;
            w.Scale[(int)SpellBehaviorType.SummonUnit] = 2f;
            w.NormalNeed = 0.8f;
            w.HighPowerNeed = 4f;
            w.DefenseBias = 1f;
            w.SummonPush = 0f;
            // TUNED-END
            return w;
        }
    }

    public sealed class AiStrategyBrain : IBotBrain, IWorldAwareBrain
    {
        private const float ThinkInterval = 0.25f;
        private const float DodgeEta = 1.4f;
        private const float ClusterRadius = 0.12f;
        /// <summary>서버의 한 수는 요청한 지 이 시간 안일 때만 쓴다 (답이 늦으면 전장이 바뀌었다)</summary>
        private const float AdviceMaxAge = 5f;
        private const float PreferBoost = 1.6f;
        private const float LaneBoost = 1.3f;

        private readonly Team _team;
        private readonly int[] _deck;
        private readonly SpellCatalog _catalog;
        private readonly BotConfig _config;
        private readonly DeterministicRandom _rng;
        private readonly IStrategyAdvisor _advisor;
        private readonly AiBrainWeights _w;
        /// <summary>자세별 가중치 (-1 지키기 · 0 그대로 · 1 밀기) — 작전이 있으면 이번 틱에 _cw 로 쓴다</summary>
        private readonly AiBrainWeights[] _stanceW;
        private AiBrainWeights _cw;
        private StrategyOrders _orders;
        private bool _hasOrders;
        private readonly LaneInfo[] _lanes = new LaneInfo[TeamUtil.ColumnCount];
        private readonly BotView _view = new BotView();

        private MatchSimulation _sim;
        private float _lastTime = -1f;
        private float _nextThink;
        private float _nextCastAllowed;
        private float _nextMoveAllowed;
        private float _nextAdviceRequest;
        private float _myTowerAliveDepth;
        private int _totalEnemies;
        private int _matchSerial = -1;

        public AiStrategyBrain(Team team, int[] deck, SpellCatalog catalog, BotConfig config, uint seed, IStrategyAdvisor advisor = null,
                               AiBrainWeights weights = null)
        {
            _w = weights ?? AiBrainWeights.Default;
            _cw = _w;
            _stanceW = new[] { _w.WithStance(-1), _w, _w.WithStance(1) };
            _view.Lanes = _lanes;
            _team = team;
            _deck = deck;
            _catalog = catalog;
            _config = config;
            _rng = new DeterministicRandom(seed);
            _advisor = advisor;
        }

        /// <summary>마지막 판단 (디버그 표시용)</summary>
        public string LastReason { get; private set; } = "";

        // ── 전장 보기 ─────────────────────────────────────────

        public void Observe(MatchSimulation sim, Team team)
        {
            _sim = sim;
            if (_advisor != null && sim.MatchSerial != _matchSerial)
            {
                _matchSerial = sim.MatchSerial;
                _advisor.Track(sim, _team);
            }
            var arena = sim.Arena;
            float field = arena.FieldLength;
            _myTowerAliveDepth = (arena.TowerDepth + sim.Rules.TowerRange) / field;
            _totalEnemies = 0;
            _view.UnitCount = 0;
            for (int c = 0; c < _lanes.Length; c++) _lanes[c] = new LaneInfo { EnemyFront = 1f };

            // 적 유닛 깊이를 열마다 모아 두었다가 가장 많이 겹치는 깊이를 찾는다
            var depths = _depthScratch;
            for (int c = 0; c < depths.Length; c++) _depthCount[c] = 0;

            var units = sim.Units;
            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (!u.Active || u.Dead) continue;
                int local = (int)TeamUtil.ToLocalColumn(team, arena.NearestWorldColumn(u.Position.x));
                float depth = Mathf.Clamp01(arena.VToDepth(team, u.Position.y));
                if (_view.UnitCount < BotView.MaxUnits)
                    _view.Units[_view.UnitCount++] = new BotUnitDot { Column = (byte)local, Depth = depth, Enemy = u.Team != team };
                ref LaneInfo lane = ref _lanes[local];
                if (u.Team == team)
                {
                    lane.Allies++;
                    if (depth > lane.AllyFront) lane.AllyFront = depth;
                }
                else
                {
                    lane.Enemies++;
                    lane.EnemyHp += u.Hp;
                    if (depth < lane.EnemyFront) lane.EnemyFront = depth;
                    if (depth <= _myTowerAliveDepth) lane.EnemiesNearMyBase++;
                    _totalEnemies++;
                    if (_depthCount[local] < MaxTrackedPerLane) depths[local][_depthCount[local]++] = depth;
                }
            }
            for (int c = 0; c < _lanes.Length; c++)
            {
                FindCluster(depths[c], _depthCount[c], out float at, out int count);
                _lanes[c].ClusterDepth = at;
                _lanes[c].ClusterCount = count;
            }
        }

        private const int MaxTrackedPerLane = 48;
        private readonly float[][] _depthScratch = { new float[MaxTrackedPerLane], new float[MaxTrackedPerLane], new float[MaxTrackedPerLane] };
        private readonly int[] _depthCount = new int[TeamUtil.ColumnCount];

        private static void FindCluster(float[] d, int n, out float at, out int count)
        {
            at = 0.5f;
            count = 0;
            for (int i = 0; i < n; i++)
            {
                int k = 0;
                float sum = 0f;
                for (int j = 0; j < n; j++)
                {
                    if (Mathf.Abs(d[j] - d[i]) <= ClusterRadius)
                    {
                        k++;
                        sum += d[j];
                    }
                }
                if (k > count)
                {
                    count = k;
                    at = sum / k;
                }
            }
        }

        // ── 매 틱 ─────────────────────────────────────────────

        public void Tick(in BotObservation obs, ref BotCommand cmd)
        {
            float t = obs.MatchTimeSeconds;
            if (t < _lastTime) ResetTimers();      // 새 경기 (경기 시간이 되감김)
            _lastTime = t;
            if (t < _nextThink) return;
            _nextThink = t + ThinkInterval;

            // 1) 피하기
            sbyte move = 0;
            if (obs.IncomingProjectileCount > 0 && obs.NearestIncomingProjectileColumn == obs.SelfColumn &&
                obs.NearestIncomingProjectileEta >= 0f && obs.NearestIncomingProjectileEta < DodgeEta && t >= _nextMoveAllowed)
            {
                int self = (int)obs.SelfColumn;
                int target = self == 1 ? (_rng.NextFloat() < 0.5f ? 0 : 2) : 1;
                move = (sbyte)(target > self ? 1 : -1);
                cmd.HasMoveRequest = true;
                cmd.MoveRequest = new MoveRequest { Direction = move };
                _nextMoveAllowed = t + 0.6f;
                LastReason = "투사체 피하기";
            }

            // 작전 (VLM · 학습) — 유효한 동안 이번 틱 점수에 곱한다
            bool useServer = _advisor != null && _config.UseServerStrategy;
            _hasOrders = useServer && _advisor.TryGetOrders(_team, t, out _orders);
            _cw = _hasOrders ? _stanceW[Mathf.Clamp(_orders.Stance, -1, 1) + 1] : _w;

            // 2) 후보 매기기 (빠른 반사 — 매 판단마다)
            bool own = PickBestCast(obs, out SpellCastRequest ownCast, out float ownScore, out bool byOrders);

            // 서버 전략은 몇 초에 한 번 요청 (응답은 비동기로 들어온다) — 지금 후보 · 유닛 점을 같이 보낸다
            if (useServer && t >= _nextAdviceRequest)
            {
                _nextAdviceRequest = t + Mathf.Max(1f, _config.ServerAdviceInterval);
                _advisor.RequestAdvice(_team, obs, _view, _deck);
            }

            // 3) 시전 (사람처럼 시전 사이에 쉰다): 위급하면 두뇌 → 서버의 한 수(신선할 때) → 두뇌(작전 반영)
            if (t < _nextCastAllowed)
            {
                if (move != 0) Report(obs, default, false, move, false, false);
                return;
            }
            bool followed = false;
            SpellCastRequest request = ownCast;
            bool found = own;
            bool emergency = own && ownScore >= Mathf.Max(1f, _config.EmergencyScore);
            if (!emergency && TryAdvice(obs, t, out SpellCastRequest advised))
            {
                request = advised;
                found = followed = true;
                byOrders = false;
            }
            else if (own) LastReason = (emergency ? "위급 · " : byOrders ? "작전 · " : "") + LastReason;

            if (found)
            {
                cmd.HasCastRequest = true;
                cmd.CastRequest = request;
                _nextCastAllowed = t + Mathf.Max(0.5f, _config.AiCastGapSeconds) * _rng.Range(0.9f, 1.2f);
            }
            if (found || move != 0) Report(obs, request, found, move, followed, found && byOrders);
        }

        private void Report(in BotObservation obs, in SpellCastRequest request, bool hasCast, sbyte move, bool followed, bool byOrders)
        {
            if (_advisor != null && _config.ReportExperience)
                _advisor.ReportDecision(_team, obs, _view, _deck, request, hasCast, move, followed, byOrders);
        }

        public void ResetTimers()
        {
            _nextThink = 0f;
            _nextCastAllowed = 0f;
            _nextMoveAllowed = 0f;
            _nextAdviceRequest = 0f;
        }

        // ── 서버 전략 ─────────────────────────────────────────

        private bool TryAdvice(in BotObservation obs, float t, out SpellCastRequest request)
        {
            request = default;
            if (_advisor == null || !_config.UseServerStrategy || !_advisor.TryGetAdvice(_team, out StrategyAdvice a)) return false;
            if (t - a.RequestedAt > AdviceMaxAge || a.Slot < 0 || a.Slot >= _deck.Length) return false;
            if ((obs.SelfSlotReadyMask & (1 << a.Slot)) == 0 || _deck[a.Slot] != a.SpellId) return false;
            var def = _catalog.Get(a.SpellId);
            if (def == null) return false;
            request = new SpellCastRequest
            {
                SpellId = a.SpellId,
                SlotIndex = a.Slot,
                TargetColumn = a.Column,
                TargetDepth = Mathf.Clamp(a.Depth, 0.05f, def.MaxDepth),
            };
            LastReason = $"서버 전략 {def.Code}";
            return true;
        }

        // ── 스스로 판단 ───────────────────────────────────────

        /// <summary>
        /// 쓸 수 있는 슬롯마다 점수를 매겨 가장 높은 것 (작전이 있으면 곱한 점수로). 작전을 곱하기 전 점수 상위 3개는
        /// _view.Candidates 에 남겨 서버(VLM)에 botPick 으로 보낸다.
        /// </summary>
        private bool PickBestCast(in BotObservation obs, out SpellCastRequest best, out float bestScore, out bool byOrders)
        {
            best = default;
            bestScore = 0f;
            byOrders = false;
            string bestWhy = "";
            _view.CandidateCount = 0;
            for (int slot = 0; slot < _deck.Length; slot++)
            {
                if ((obs.SelfSlotReadyMask & (1 << slot)) == 0) continue;
                int id = _deck[slot];
                var def = id > 0 ? _catalog.Get(id) : null;
                if (def == null) continue;
                float raw = Score(def, obs, out Column col, out float depth, out string why);
                float scale = _cw.Scale[(int)def.Behavior % AiBrainWeights.BehaviorCount];
                float score = raw * scale;
                if (score > 0f) AddCandidate(slot, id, col, Mathf.Clamp(depth, 0.05f, def.MaxDepth), score, why);

                // 작전: 아낄 스킬은 쓰지 않고, 먼저 쓸 스킬 · 집중 레인은 점수를 올린다 (조정으로 0 이 된 스킬도 VLM 이 고르면 다시 쓴다)
                bool boosted = false;
                if (_hasOrders)
                {
                    if (_orders.Holds(id)) continue;
                    if (_orders.Prefers(id))
                    {
                        score = raw * Mathf.Max(scale, 0.75f) * PreferBoost;
                        boosted = true;
                    }
                    if (_orders.Lane >= 0 && (int)col == _orders.Lane && def.Targeting != SpellTargeting.Self && def.Targeting != SpellTargeting.Global)
                    {
                        score *= LaneBoost;
                        boosted = true;
                    }
                }
                // 고위력은 경기당 3회 — 확실히 큰 순간에만
                float need = def.Tier == SpellTier.HighPower ? _cw.HighPowerNeed : _cw.NormalNeed;
                if (score < need) continue;
                if (def.Tier == SpellTier.HighPower) score *= 0.85f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestWhy = why;
                    byOrders = boosted;
                    best = new SpellCastRequest
                    {
                        SpellId = id,
                        SlotIndex = slot,
                        TargetColumn = col,
                        TargetDepth = Mathf.Clamp(depth, 0.05f, def.MaxDepth),
                    };
                }
            }
            if (bestScore <= 0f) return false;
            LastReason = bestWhy;
            return true;
        }

        private void AddCandidate(int slot, int id, Column col, float depth, float score, string why)
        {
            var c = _view.Candidates;
            int n = _view.CandidateCount;
            int at = n;
            while (at > 0 && c[at - 1].Score < score) at--;
            if (at >= BotView.MaxCandidates) return;
            for (int i = Mathf.Min(n, BotView.MaxCandidates - 1); i > at; i--) c[i] = c[i - 1];
            c[at] = new BotCandidate { Slot = slot, SpellId = id, Column = col, Depth = depth, Score = score, Why = why };
            if (n < BotView.MaxCandidates) _view.CandidateCount = n + 1;
        }

        private float Score(SpellDefinition def, in BotObservation obs, out Column col, out float depth, out string why)
        {
            col = obs.SelfColumn;
            depth = 0.5f;
            why = def.Code;
            float hp = obs.SelfHp / Mathf.Max(1f, _sim != null ? _sim.Rules.PlayerMaxHp : 1000f);
            float dmg = Potency(def) / 100f;
            bool oppExposed = !obs.OpponentTowerAlive;
            int busiest = BusiestLane();
            int weakest = WeakestLane();
            int threat = ThreatLane();

            switch (def.Behavior)
            {
                case SpellBehaviorType.Splash:
                case SpellBehaviorType.FireballLob:
                case SpellBehaviorType.ArrowVolley:
                case SpellBehaviorType.LightningStrikes:
                {
                    int lane = BestClusterLane();
                    var l = _lanes[lane];
                    col = (Column)lane;
                    depth = l.ClusterDepth;
                    float hits = l.ClusterCount + (def.Behavior == SpellBehaviorType.LightningStrikes ? l.Enemies * 0.3f : 0f);
                    why = $"{def.Code} {Ko(lane)} 레인 적 {l.ClusterCount}기 겹침";
                    return hits * dmg * Urgency(l);
                }
                case SpellBehaviorType.PoisonField:
                {
                    // 3열을 가로지르는 띠 — 같은 깊이에 걸친 적 전체
                    float bestHits = 0f;
                    for (int c = 0; c < _lanes.Length; c++)
                    {
                        if (_lanes[c].ClusterCount == 0) continue;
                        float d = _lanes[c].ClusterDepth;
                        float hits = 0f;
                        for (int k = 0; k < _lanes.Length; k++)
                            if (_lanes[k].Enemies > 0 && Mathf.Abs(_lanes[k].ClusterDepth - d) < 0.15f) hits += _lanes[k].ClusterCount;
                        if (hits > bestHits)
                        {
                            bestHits = hits;
                            depth = d;
                            col = (Column)c;
                        }
                    }
                    why = $"{def.Code} 독 장판 적 {bestHits}기";
                    return bestHits * dmg * 1.2f;
                }
                case SpellBehaviorType.TornadoLane:
                {
                    var l = _lanes[threat];
                    col = (Column)threat;
                    depth = l.EnemyFront;
                    why = $"{def.Code} {Ko(threat)} 레인 밀어내기";
                    return l.Enemies * dmg * 0.8f + l.EnemiesNearMyBase * 1.2f;
                }
                case SpellBehaviorType.MultiShotFrontFirst:
                {
                    var l = _lanes[busiest];
                    if (l.Enemies > 0)
                    {
                        col = (Column)busiest;
                        why = $"{def.Code} {Ko(busiest)} 레인 앞줄 연사";
                        return Mathf.Min(l.Enemies, Mathf.Max(1, def.ShotCount)) * dmg * Urgency(l);
                    }
                    col = obs.OpponentColumn;          // 앞에 유닛이 없으면 상대 플레이어
                    why = $"{def.Code} 상대 플레이어 연사";
                    return def.ShotCount * dmg * (oppExposed ? 1f : 0.25f);
                }
                case SpellBehaviorType.LaserBeam:
                {
                    int lane = busiest;
                    float player = 0f;
                    if (oppExposed)
                    {
                        lane = (int)obs.OpponentColumn;
                        player = 4f;
                    }
                    var l = _lanes[lane];
                    col = (Column)lane;
                    why = $"{def.Code} {Ko(lane)} 열 관통{(player > 0 ? " · 상대 플레이어" : "")}";
                    return l.Enemies * dmg * 0.6f + player * dmg;
                }
                case SpellBehaviorType.DrainLife:
                {
                    col = obs.OpponentColumn;
                    why = $"{def.Code} 흡혈";
                    float need = Mathf.Clamp01(1f - hp) * 6f;
                    return (oppExposed ? 3f : 0.5f) + need;
                }
                case SpellBehaviorType.BlindShot:
                    col = obs.OpponentColumn;
                    why = $"{def.Code} 상대 시야 가리기";
                    return oppExposed ? 1.2f : 0.85f;
                case SpellBehaviorType.ShieldRegen:
                {
                    bool danger = !obs.SelfTowerAlive || (obs.IncomingProjectileCount > 0);
                    why = $"{def.Code} 쉴드 (HP {Mathf.RoundToInt(hp * 100)}%)";
                    return (1f - hp) * 5f + (danger ? 1.5f : 0f);
                }
                case SpellBehaviorType.ReflectBarrier:
                    why = $"{def.Code} 반사 결계";
                    return !obs.SelfTowerAlive ? (1f - hp) * 9f : (1f - hp) * 3f;
                case SpellBehaviorType.TowerBuff:
                {
                    int near = 0;
                    for (int c = 0; c < _lanes.Length; c++) near += _lanes[c].EnemiesNearMyBase;
                    why = $"{def.Code} 타워 강화 (타워 앞 적 {near}기)";
                    return obs.SelfTowerAlive ? near * 0.9f : 0f;
                }
                case SpellBehaviorType.Focus:
                    why = $"{def.Code} 집중";
                    return _sim != null && _sim.GetPlayer(_team).HasFocus ? 0f : (_totalEnemies >= 3 ? 1.1f : 0.6f);
                case SpellBehaviorType.SummonUnit:
                {
                    int lane = SummonLane();
                    var l = _lanes[lane];
                    col = (Column)lane;
                    depth = 0.2f;
                    why = $"{def.Code} {Ko(lane)} 레인 {(l.Allies > l.Enemies ? "밀기" : "보강")}";
                    return 2f + Mathf.Max(0, l.Enemies - l.Allies) * 0.6f + l.EnemiesNearMyBase * 0.5f;
                }
                case SpellBehaviorType.PlaceStructure:
                {
                    var l = _lanes[threat];
                    col = (Column)threat;
                    depth = 0.2f;
                    why = $"{def.Code} {Ko(threat)} 레인 설치";
                    float early = def.Tier == SpellTier.HighPower && obs.MatchTimeSeconds < 120f ? 4.5f : 0f;
                    return 1.5f + l.Enemies * 0.5f + l.EnemiesNearMyBase * 0.8f + early;
                }
                case SpellBehaviorType.FreezeAll:
                {
                    int near = 0;
                    for (int c = 0; c < _lanes.Length; c++) near += _lanes[c].EnemiesNearMyBase;
                    why = $"{def.Code} 프리즈 (적 {_totalEnemies}기)";
                    return _totalEnemies * 0.55f + near * 0.9f;
                }
                case SpellBehaviorType.RageField:
                {
                    int lane = StrongestLane();
                    var l = _lanes[lane];
                    col = (Column)lane;
                    depth = Mathf.Max(0.15f, l.AllyFront);
                    why = $"{def.Code} {Ko(lane)} 레인 분노";
                    return l.Allies * 1.3f + (oppExposed ? 1f : 0f);
                }
                default:
                {
                    var l = _lanes[busiest];
                    col = (Column)busiest;
                    depth = l.ClusterDepth;
                    return def.Targeting == SpellTargeting.Self || def.Targeting == SpellTargeting.Global ? 0.9f : 0.8f + l.Enemies * dmg * 0.5f;
                }
            }
        }

        /// <summary>한 번 시전의 총 피해 (발 수 · 지속 틱 포함) — 스킬마다 다른 수치를 같은 눈금으로</summary>
        private static float Potency(SpellDefinition def)
        {
            float shots = Mathf.Max(1, def.ShotCount);
            float ticks = def.Behavior == SpellBehaviorType.LaserBeam || def.Behavior == SpellBehaviorType.PoisonField
                ? Mathf.Max(1f, def.EffectDuration / Mathf.Max(0.05f, def.TickInterval)) : 1f;
            return Mathf.Max(1f, def.BaseDamage) * shots * ticks;
        }

        /// <summary>내 쪽으로 가까이 온 적일수록 급하다 (DefenseBias 만큼)</summary>
        private float Urgency(in LaneInfo l) =>
            l.Enemies == 0 ? 0f : 1f + ((1f - l.EnemyFront) + l.EnemiesNearMyBase * 0.2f) * _cw.DefenseBias;

        /// <summary>소환할 레인: 밀리는 레인 보강 ↔ 밀고 있는 레인 가속 (SummonPush 로 섞는다)</summary>
        private int SummonLane()
        {
            int best = 1;
            float score = float.MinValue;
            for (int c = 0; c < _lanes.Length; c++)
            {
                var l = _lanes[c];
                float weak = (l.Enemies - l.Allies) * 2f + l.EnemiesNearMyBase * 2f - l.Allies * 0.1f;
                float push = (l.Allies - l.Enemies) * 2f + l.AllyFront * 3f;
                float s = (1f - _cw.SummonPush) * weak + _cw.SummonPush * push;
                if (s > score) { score = s; best = c; }
            }
            return best;
        }

        private int BestClusterLane()
        {
            int best = 1;
            float score = -1f;
            for (int c = 0; c < _lanes.Length; c++)
            {
                float s = _lanes[c].ClusterCount * (1f + (1f - _lanes[c].EnemyFront) * 0.5f * _cw.DefenseBias);
                if (s > score) { score = s; best = c; }
            }
            return best;
        }

        private int BusiestLane()
        {
            int best = 1;
            for (int c = 0; c < _lanes.Length; c++)
                if (_lanes[c].Enemies > _lanes[best].Enemies) best = c;
            return best;
        }

        /// <summary>적이 가장 가까이 온 레인 (같으면 적이 많은 쪽)</summary>
        private int ThreatLane()
        {
            int best = 1;
            float score = -1f;
            for (int c = 0; c < _lanes.Length; c++)
            {
                var l = _lanes[c];
                float s = l.Enemies == 0 ? 0f : l.Enemies + (1f - l.EnemyFront) * 3f + l.EnemiesNearMyBase * 2f;
                if (s > score) { score = s; best = c; }
            }
            return best;
        }

        /// <summary>우리 유닛이 가장 밀리는 레인 (적 - 아군 이 큰 쪽, 같으면 우리 유닛이 적은 쪽)</summary>
        private int WeakestLane()
        {
            int best = 1;
            float score = float.MinValue;
            for (int c = 0; c < _lanes.Length; c++)
            {
                var l = _lanes[c];
                float s = (l.Enemies - l.Allies) * 2f + l.EnemiesNearMyBase * 2f - l.Allies * 0.1f;
                if (s > score) { score = s; best = c; }
            }
            return best;
        }

        private int StrongestLane()
        {
            int best = 1;
            for (int c = 0; c < _lanes.Length; c++)
                if (_lanes[c].Allies - _lanes[c].Enemies > _lanes[best].Allies - _lanes[best].Enemies) best = c;
            return best;
        }

        private static string Ko(int col) => col == 0 ? "왼쪽" : col == 1 ? "가운데" : "오른쪽";
    }
}
