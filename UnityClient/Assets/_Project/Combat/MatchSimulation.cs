// 서버 권한 전투 시뮬레이션 — 메인 루프 (§7 전투 흐름, §8 PC 서버 권한)
//
//   Input → Request → Server Validation → Rule Check → State Change → Event → Presentation (§36)
//
// 같은 클래스가 두 곳에서 돈다.
//   · LocalMatchHost (에디터 · Quest 오프라인 봇전): 같은 프로세스 안의 "서버"
//   · NetworkMatchState (Fusion Server 모드, PC 서버 빌드): 진짜 서버
// 사람과 봇은 SubmitCast / SubmitMove 라는 같은 경로로 요청한다(Rule 10).
// 요청은 큐에 쌓였다가 다음 틱 시작 시 순서대로 처리된다 (틱 결정성).

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Bot;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using SpellboundVR.Spells.Executor;
using SpellboundVR.Utils;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed partial class MatchSimulation
    {
        private enum RequestKind : byte { Cast, Move, Activity }

        private struct PendingRequest
        {
            public Team Team;
            public RequestKind Kind;
            public SpellCastRequest Cast;
            public MoveRequest Move;
        }

        private const int QueueCapacity = 64;

        public readonly PlayerState[] Players = new PlayerState[TeamUtil.TeamCount];
        public readonly StructureState[] Structures = new StructureState[MatchSnapshot.StructureCount];
        public readonly UnitState[] Units = new UnitState[MatchSnapshot.MaxUnits];

        private readonly MinionDefinition[] _minionDefs = new MinionDefinition[3];
        private readonly BotPlayer[] _bots = new BotPlayer[TeamUtil.TeamCount];
        private readonly IBotBrain[] _botBrainOverrides = new IBotBrain[TeamUtil.TeamCount];
        private readonly int[][] _decks = { new int[DeckData.SlotCount], new int[DeckData.SlotCount] };
        private readonly List<ISpellRoutine> _routines = new List<ISpellRoutine>(16);
        private readonly PendingRequest[] _queue = new PendingRequest[QueueCapacity];
        private readonly TargetRuleValidator _targetRules;
        private readonly StatusEffectHost _status = new StatusEffectHost();
        private readonly AccuracyRoll _accuracy = new AccuracyRoll();
        private readonly MatchScaling _scaling = new MatchScaling();
        private readonly WaveSpawner _waves = new WaveSpawner();
        private readonly PlayerHealthRegen _regen = new PlayerHealthRegen();
        private readonly ProjectileSystem _projectiles = new ProjectileSystem();
        private readonly SpellExecutorRegistry _executors;
        private readonly DeterministicRandom _rng;

        private IMatchEventSink _sink = NullMatchEventSink.Instance;
        private int _queueCount;
        private MatchPhase _phase = MatchPhase.WaitingForPlayers;
        private float _phaseTimer;
        private float _matchTime;
        private uint _tick;
        private int _matchSerial;
        private int _nextUnitId = TeamUtil.FirstUnitId;
        private MatchOutcome _outcome;
        private float _duration;
        private bool _botManualKeys;
        private bool _botRequireAlt;

        public MatchSimulation(MatchRuleConfig rules, SpellCatalog catalog, ArenaGeometry arena, BotConfig botConfig,
                               MinionDefinition melee, MinionDefinition ranged, MinionDefinition brute, uint seed)
        {
            Rules = rules;
            Catalog = catalog;
            Arena = arena;
            BotConfig = botConfig;
            _minionDefs[(int)MinionKind.Melee] = melee != null ? melee : MinionDefinition.CreateDefault(MinionKind.Melee);
            _minionDefs[(int)MinionKind.Ranged] = ranged != null ? ranged : MinionDefinition.CreateDefault(MinionKind.Ranged);
            _minionDefs[(int)MinionKind.Brute] = brute != null ? brute : MinionDefinition.CreateDefault(MinionKind.Brute);
            _rng = new DeterministicRandom(seed);

            for (int t = 0; t < TeamUtil.TeamCount; t++) Players[t] = new PlayerState((Team)t);
            Structures[0] = new StructureState(Team.Home, false);
            Structures[1] = new StructureState(Team.Home, true);
            Structures[2] = new StructureState(Team.Away, false);
            Structures[3] = new StructureState(Team.Away, true);
            for (int i = 0; i < Units.Length; i++) Units[i] = new UnitState();

            Defense = new DefenseLayerState(Structures);
            _targetRules = new TargetRuleValidator(Defense);
            Resolver = new ServerDamageResolver(this, _targetRules, Defense, _status, _accuracy, rules);
            _status.Bind(this);
            _executors = new SpellExecutorRegistry(catalog, this);

            ResetWorld();
        }

        // ── 공개 상태 ─────────────────────────────────────────

        public MatchRuleConfig Rules { get; }
        public SpellCatalog Catalog { get; }
        public ArenaGeometry Arena { get; }
        public BotConfig BotConfig { get; }
        public DefenseLayerState Defense { get; }
        public ServerDamageResolver Resolver { get; }
        public ISpellExecutorLookup Executors => _executors;
        public StatusEffectHost StatusHost => _status;
        public MatchPhase Phase => _phase;
        public float PhaseTimeLeft => _phaseTimer;
        public float MatchTime => _matchTime;
        public int MatchSerial => _matchSerial;
        public int ScalingStage => _scaling.Stage;
        public int WaveIndex => _waves.WaveIndex;
        public MatchOutcome Outcome => _outcome;
        public float Duration => _duration;
        public ProjectileSystem Projectiles => _projectiles;

        /// <summary>§37 디버깅: 시전 확정 · 거절을 콘솔에 남긴다 (서버 창 · 에디터)</summary>
        public bool LogCasts { get; set; } = true;

        public PlayerState GetPlayer(Team team) => Players[(int)team];

        public BotPlayer GetBot(Team team) => _bots[(int)team];

        public MinionDefinition GetMinionDefinition(MinionKind kind) => _minionDefs[(int)kind];

        public int[] GetDeck(Team team) => _decks[(int)team];

        public void SetEventSink(IMatchEventSink sink)
        {
            _sink = sink ?? NullMatchEventSink.Instance;
        }

        /// <summary>봇 수동 조종 키 허용 여부 (§20.3). 로컬 모드는 Alt 필요.</summary>
        public void ConfigureBotManualKeys(bool enabled, bool requireAlt)
        {
            _botManualKeys = enabled;
            _botRequireAlt = requireAlt;
        }

        // ── 참가자 ───────────────────────────────────────────

        public bool IsSlotFree(Team team) => !Players[(int)team].Present;

        public bool TryGetFreeTeam(out Team team)
        {
            if (!Players[0].Present) { team = Team.Home; return true; }
            if (!Players[1].Present) { team = Team.Away; return true; }
            team = Team.Home;
            return false;
        }

        public void JoinHuman(Team team, int[] deck)
        {
            var p = Players[(int)team];
            p.Present = true;
            p.IsBot = false;
            p.Connected = true;
            _bots[(int)team] = null;
            SetDeck(team, deck);
            Debug.Log($"[MatchSimulation] 사람 플레이어 입장: {team}, 덱 {new DeckData(_decks[(int)team])}");
        }

        /// <param name="brainOverride">null이면 기본 TimerRandomBrain (§20.4 FSM 봇은 여기로 교체)</param>
        public void JoinBot(Team team, IBotBrain brainOverride = null)
        {
            int t = (int)team;
            var p = Players[t];
            p.Present = true;
            p.IsBot = true;
            p.Connected = true;
            if (brainOverride != null) _botBrainOverrides[t] = brainOverride;
            bool wasManual = _bots[t] != null && _bots[t].ManualMode;

            int[] deck = RandomDeckBuilder.Build(Catalog, _executors, Rules, BotConfig, _rng);
            System.Array.Copy(deck, _decks[t], DeckData.SlotCount);
            _bots[t] = new BotPlayer(team, _decks[t], Catalog, BotConfig, _rng.NextUInt(),
                                     _botManualKeys, _botRequireAlt, _botBrainOverrides[t]);
            _bots[t].SetManualMode(wasManual);
            Debug.Log($"[MatchSimulation] 봇 입장: {team}, 덱 {new DeckData(_decks[t])}");
        }

        /// <summary>덱 제출 (서버 재검증 §13.1). 경기 중이면 다음 경기부터 적용.</summary>
        public void SetDeck(Team team, int[] deck)
        {
            if (!ServerDeckValidator.Validate(deck, Catalog, Rules, out string reason))
            {
                Debug.LogWarning($"[MatchSimulation] {team} 덱 거절: {reason} → 기본 덱 사용");
                deck = DeckData.CreateDefault().ToArray();
                if (!ServerDeckValidator.Validate(deck, Catalog, Rules, out _))
                    deck = BuildFallbackDeck();
            }
            System.Array.Copy(deck, _decks[(int)team], DeckData.SlotCount);

            // 카운트다운 중(시전 불가 구간)에 도착한 덱은 즉시 슬롯에 반영한다
            if (_phase == MatchPhase.Countdown)
                SlotCooldownService.Initialize(Players[(int)team], _decks[(int)team], Catalog, Rules);
        }

        private int[] BuildFallbackDeck()
        {
            var deck = new int[DeckData.SlotCount];
            int n = 0;
            var all = Catalog.All;
            for (int i = 0; i < all.Count && n < deck.Length; i++)
                if (all[i] != null && all[i].Tier == SpellTier.Normal) deck[n++] = all[i].SpellId;
            return deck;
        }

        /// <summary>§10 연결 끊김. 경기 중이면 다음 틱에 패배 처리, 대기 중이면 자리를 비운다.</summary>
        public void NotifyDisconnected(Team team)
        {
            var p = Players[(int)team];
            if (!p.Present) return;
            p.Connected = false;
            if (_phase != MatchPhase.Playing)
            {
                p.Present = false;
                _bots[(int)team] = null;
                if (_phase == MatchPhase.Countdown) _phase = MatchPhase.WaitingForPlayers;
            }
            Debug.Log($"[MatchSimulation] {team} 연결 끊김 (phase {_phase})");
        }

        public void RemovePlayer(Team team)
        {
            var p = Players[(int)team];
            p.Present = false;
            p.Connected = false;
            _bots[(int)team] = null;
            if (_phase == MatchPhase.Countdown) _phase = MatchPhase.WaitingForPlayers;
        }

        // ── 요청 (사람 · 봇 공용 경로) ─────────────────────────

        public void SubmitCast(Team team, in SpellCastRequest request)
        {
            if (_phase != MatchPhase.Playing)
            {
                _sink.OnCastRejected(team, request.SlotIndex, CastRejectReason.MatchNotRunning);
                return;
            }
            if (!Enqueue(new PendingRequest { Team = team, Kind = RequestKind.Cast, Cast = request }))
                _sink.OnCastRejected(team, request.SlotIndex, CastRejectReason.QueueFull);
        }

        public void SubmitMove(Team team, in MoveRequest request)
        {
            if (_phase != MatchPhase.Playing)
            {
                _sink.OnMoveRejected(team, Players[(int)team].WorldColumn);
                return;
            }
            if (!Enqueue(new PendingRequest { Team = team, Kind = RequestKind.Move, Move = request }))
                _sink.OnMoveRejected(team, Players[(int)team].WorldColumn);
        }

        /// <summary>§10 활동 — 룬 드로잉 시작 (시전 성공 · 이동은 서버가 자동 기록)</summary>
        public void ReportActivity(Team team)
        {
            if (_phase != MatchPhase.Playing) return;
            Enqueue(new PendingRequest { Team = team, Kind = RequestKind.Activity });
        }

        private bool Enqueue(in PendingRequest r)
        {
            if (_queueCount >= QueueCapacity) return false;
            _queue[_queueCount++] = r;
            return true;
        }

        // ── 메인 루프 ─────────────────────────────────────────

        /// <summary>프레임마다 호출 (봇 수동 조종 키 수집)</summary>
        public void PollFrameInput()
        {
            for (int t = 0; t < _bots.Length; t++) _bots[t]?.PollInput();
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            _tick++;

            switch (_phase)
            {
                case MatchPhase.WaitingForPlayers:
                    if (BothPlayersReady()) BeginCountdown();
                    break;

                case MatchPhase.Countdown:
                    if (!BothPlayersReady())
                    {
                        _phase = MatchPhase.WaitingForPlayers;
                        break;
                    }
                    _phaseTimer -= dt;
                    if (_phaseTimer <= 0f) BeginPlaying();
                    break;

                case MatchPhase.Playing:
                    TickPlaying(dt);
                    break;

                case MatchPhase.Ended:
                    _queueCount = 0;
                    if (Rules.AutoRestartDelay > 0f)
                    {
                        _phaseTimer -= dt;
                        if (_phaseTimer <= 0f)
                        {
                            DropDisconnectedPlayers();
                            if (BothPlayersReady()) BeginCountdown();
                            else _phase = MatchPhase.WaitingForPlayers;
                        }
                    }
                    break;
            }
        }

        private void TickPlaying(float dt)
        {
            _matchTime += dt;
            _scaling.Update(_matchTime, Rules);

            for (int t = 0; t < Players.Length; t++) SlotCooldownService.Update(Players[t], _matchTime);

            for (int t = 0; t < _bots.Length; t++) _bots[t]?.Tick(this);

            ProcessQueue();
            TickRoutines(dt);
            _projectiles.Tick(this, dt);
            _status.Tick(dt);
            _waves.Tick(this, dt);
            for (int i = 0; i < Units.Length; i++) MinionAI.Tick(this, Units[i], dt);
            TowerAI.Tick(this, Structures[0], dt);
            TowerAI.Tick(this, Structures[2], dt);
            _regen.Tick(this, dt);
            CleanupDeadUnits();

            var outcome = MatchResultResolver.Evaluate(Players[0], Players[1], _matchTime, Rules);
            if (outcome.Decided) EndMatch(outcome);
        }

        private void ProcessQueue()
        {
            for (int i = 0; i < _queueCount; i++)
            {
                ref PendingRequest r = ref _queue[i];
                switch (r.Kind)
                {
                    case RequestKind.Cast: ProcessCast(r.Team, r.Cast); break;
                    case RequestKind.Move: ProcessMove(r.Team, r.Move); break;
                    case RequestKind.Activity: Players[(int)r.Team].LastActivityTime = _matchTime; break;
                }
            }
            _queueCount = 0;
        }

        private void ProcessCast(Team team, in SpellCastRequest request)
        {
            var caster = Players[(int)team];
            var reason = ServerSpellValidator.Validate(_phase, caster, request, Catalog, _executors, out SpellDefinition def);
            if (reason != CastRejectReason.None || !_executors.TryGet(request.SpellId, out ISpellExecutor executor))
            {
                if (reason == CastRejectReason.None) reason = CastRejectReason.NotImplemented;
                if (LogCasts) Debug.Log($"[MatchSimulation] 시전 거절 {team} 슬롯{request.SlotIndex + 1} {SpellIds.Code(request.SpellId)} → {reason}");
                _sink.OnCastRejected(team, request.SlotIndex, reason);
                return;
            }
            if (LogCasts)
                Debug.Log($"[MatchSimulation] 시전 확정 {team}{(caster.IsBot ? "(Bot)" : "")} 슬롯{request.SlotIndex + 1} {def.Code} {def.DisplayName} " +
                          $"열 {request.TargetColumn} 깊이 {request.TargetDepth:0.00} t={_matchTime:0.0}");

            // 시전 확정 → 쿨타임 시작 (§13.4), 활동 기록 (§10)
            SlotCooldownService.Consume(caster, request.SlotIndex, _matchTime, Rules, def.Tier, _scaling.Stage);
            caster.LastActivityTime = _matchTime;
            _sink.OnCastAccepted(team, request.SlotIndex, request.SpellId);

            executor.Execute(request, team);
        }

        private void ProcessMove(Team team, in MoveRequest request)
        {
            var p = Players[(int)team];
            int worldDir = TeamUtil.ToWorldDirection(team, request.Direction);
            if (ServerMoveValidator.Validate(p, worldDir, _matchTime, Rules, out int newColumn))
            {
                p.WorldColumn = newColumn;
                p.LastMoveTime = _matchTime;
                p.LastActivityTime = _matchTime;
            }
            else
            {
                _sink.OnMoveRejected(team, p.WorldColumn);
            }
        }

        private void TickRoutines(float dt)
        {
            for (int i = _routines.Count - 1; i >= 0; i--)
            {
                bool alive;
                try
                {
                    alive = _routines[i].Tick(dt, this);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    alive = false;
                }
                if (!alive) _routines.RemoveAt(i);
            }
        }

        private void CleanupDeadUnits()
        {
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (u.Active && u.Dead)
                {
                    _status.ClearTarget(u.Id);
                    u.Deactivate();
                }
            }
        }

        // ── 단계 전환 ─────────────────────────────────────────

        private bool BothPlayersReady()
        {
            return Players[0].Present && Players[1].Present && Players[0].Connected && Players[1].Connected;
        }

        private void DropDisconnectedPlayers()
        {
            for (int t = 0; t < Players.Length; t++)
            {
                if (Players[t].Present && !Players[t].Connected) RemovePlayer((Team)t);
            }
        }

        private void BeginCountdown()
        {
            ResetWorld();
            _matchSerial++;
            _phase = MatchPhase.Countdown;
            _phaseTimer = Rules.CountdownSeconds;

            // 봇은 경기마다 새 랜덤 덱 (§20.2)
            for (int t = 0; t < TeamUtil.TeamCount; t++)
            {
                if (Players[t].Present && Players[t].IsBot) JoinBot((Team)t);
                SlotCooldownService.Initialize(Players[t], _decks[t], Catalog, Rules);
            }
            Debug.Log($"[MatchSimulation] 경기 #{_matchSerial} 카운트다운 시작");
        }

        private void BeginPlaying()
        {
            _phase = MatchPhase.Playing;
            _phaseTimer = 0f;
            _matchTime = 0f;
            for (int t = 0; t < Players.Length; t++) Players[t].LastActivityTime = 0f;
            Debug.Log($"[MatchSimulation] 경기 #{_matchSerial} 시작 — Home({(Players[0].IsBot ? "Bot" : "Human")}) vs Away({(Players[1].IsBot ? "Bot" : "Human")})");
        }

        private void EndMatch(in MatchOutcome outcome)
        {
            _outcome = outcome;
            _duration = _matchTime;
            _phase = MatchPhase.Ended;
            _phaseTimer = Rules.AutoRestartDelay;
            _routines.Clear();
            _projectiles.Clear();
            _queueCount = 0;
            Debug.Log($"[MatchSimulation] 경기 #{_matchSerial} 종료 — 승자 {outcome.Winner}, 사유 {outcome.Reason}, {_duration:0.0}초");
        }

        /// <summary>서버 오류 등으로 경기 무효 (§10, 전적 미기록)</summary>
        public void VoidMatch()
        {
            if (_phase != MatchPhase.Playing && _phase != MatchPhase.Countdown) return;
            EndMatch(new MatchOutcome { Decided = true, Winner = MatchWinner.Void, Reason = MatchEndReason.Void });
        }

        private void ResetWorld()
        {
            _matchTime = 0f;
            _queueCount = 0;
            _outcome = default;
            _outcome.Winner = MatchWinner.None;
            _duration = 0f;
            uint seed = _rng.NextUInt();
            _accuracy.Reset(seed);
            _scaling.Reset();
            _waves.Reset(Rules);
            _regen.Reset();
            _projectiles.Clear();
            _routines.Clear();
            _status.Clear();
            for (int i = 0; i < Units.Length; i++) Units[i].Deactivate();

            for (int i = 0; i < Structures.Length; i++)
            {
                var s = Structures[i];
                s.Reset(s.IsNexus ? Rules.NexusMaxHp : Rules.TowerMaxHp,
                        Arena.StructurePosition(s.Team, s.IsNexus), Rules.StructureRadius);
            }
            for (int t = 0; t < Players.Length; t++) Players[t].ResetForMatch(Rules.PlayerMaxHp, Rules.BaseAccuracy);
        }

        // ── 유닛 ─────────────────────────────────────────────

        public bool SpawnUnit(Team team, MinionDefinition def, int lane, Vector2 position)
        {
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (u.Active) continue;
                u.Activate(_nextUnitId++, team, def, lane, Arena.ClampToField(position), _matchTime);
                return true;
            }
            return false;
        }

        public UnitState FindUnit(int id)
        {
            if (id < TeamUtil.FirstUnitId) return null;
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (u.Active && !u.Dead && u.Id == id) return u;
            }
            return null;
        }

        public StructureState GetStructureById(int id)
        {
            for (int i = 0; i < Structures.Length; i++) if (Structures[i].Id == id) return Structures[i];
            return null;
        }

        /// <summary>유닛 AI: 현재 대상이 아직 유효한가</summary>
        public bool IsTargetAliveFor(UnitState attacker, int targetId)
        {
            if (targetId == 0) return false;
            if (TeamUtil.IsPlayerId(targetId))
            {
                Team t = TeamUtil.TeamOfPlayerId(targetId);
                return t != attacker.Team && Players[(int)t].Alive && Defense.IsBreached(t);
            }
            if (TeamUtil.IsStructureId(targetId))
            {
                var s = GetStructureById(targetId);
                if (s == null || !s.Alive || s.Team == attacker.Team) return false;
                return !s.IsNexus || !Defense.TowerAlive(s.Team);
            }
            var u = FindUnit(targetId);
            return u != null && u.Team != attacker.Team;
        }

        /// <summary>유닛 AI: 대상의 접근 지점과 반경 (플레이어는 돌파 지점)</summary>
        public bool TryGetTargetBody(UnitState attacker, int targetId, out Vector2 position, out float radius)
        {
            position = Vector2.zero;
            radius = 0f;
            if (TeamUtil.IsPlayerId(targetId))
            {
                Team t = TeamUtil.TeamOfPlayerId(targetId);
                if (!Players[(int)t].Alive) return false;
                position = BreachPointTargeting.GetBreachPoint(this, attacker, t);
                radius = BreachPointTargeting.BreachPointRadius;
                return true;
            }
            if (TeamUtil.IsStructureId(targetId))
            {
                var s = GetStructureById(targetId);
                if (s == null || !s.Alive) return false;
                position = s.Position;
                radius = s.Radius;
                return true;
            }
            var u = FindUnit(targetId);
            if (u == null) return false;
            position = u.Position;
            radius = u.Def.BodyRadius;
            return true;
        }

        // ── 테스트 시나리오 (§20.3 F1 ~, 개발 빌드 전용) ─────────────

        public void DebugDestroyStructure(Team team, bool isNexus)
        {
            if (_phase != MatchPhase.Playing) return;
            var s = Structures[MatchSnapshot.StructureIndex(team, isNexus)];
            if (!s.Alive) return;
            if (isNexus && Structures[MatchSnapshot.StructureIndex(team, false)].Alive)
                DebugDestroyStructure(team, false);
            s.Hp = 0f;
            OnStructureDestroyed(s);
        }

        public void DebugSetHp(Team team, float hp)
        {
            if (_phase != MatchPhase.Playing) return;
            var p = Players[(int)team];
            p.Hp = Mathf.Clamp(hp, 0f, p.MaxHp);
        }

        public void DebugForceScalingStage()
        {
            if (_phase != MatchPhase.Playing) return;
            _scaling.ForceNextStage();
        }

        public void DebugResetCooldowns(Team team)
        {
            var p = Players[(int)team];
            for (int i = 0; i < p.Slots.Length; i++)
            {
                ref SlotRuntime s = ref p.Slots[i];
                if (s.State == SlotState.Cooldown) s.CooldownEndTime = _matchTime;
            }
        }
    }
}
