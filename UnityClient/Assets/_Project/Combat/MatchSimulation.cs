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
        /// <summary>이 시간(초) 동안 보고가 없는 시전 준비 표시는 지운다 (클라이언트는 1초마다 다시 보낸다)</summary>
        private const float CastPreviewTimeout = 2.5f;

        public readonly PlayerState[] Players = new PlayerState[TeamUtil.TeamCount];
        public readonly StructureState[] Structures = new StructureState[MatchSnapshot.StructureCount];
        public readonly UnitState[] Units = new UnitState[MatchSnapshot.MaxUnits];

        private readonly MinionDefinition[] _minionDefs = new MinionDefinition[MinionDefinition.KindCount];
        private readonly BotPlayer[] _bots = new BotPlayer[TeamUtil.TeamCount];
        private readonly IBotBrain[] _botBrainOverrides = new IBotBrain[TeamUtil.TeamCount];
        private readonly int[][] _decks = new int[TeamUtil.TeamCount][];
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
        /// <summary>실행 중인 시전의 S10 집중 배율 (ISpellWorld.CastFocus)</summary>
        private float _castFocus = 1f;
        /// <summary>경기를 시작할 때 사람이 있었는지 (4인 개인전: 사람이 모두 탈락하면 봇끼리 끝까지 두지 않는다)</summary>
        private bool _hadHuman;
        private float _stormTimer;
        private int _stormTicks;
        /// <summary>4인 개인전 탈락 처리용 (할당 없이)</summary>
        private readonly bool[] _fallen = new bool[TeamUtil.TeamCount];
        /// <summary>다인전: 탈락으로 링을 다시 잡는 중</summary>
        private bool _rebuilding;
        /// <summary>다인전 재구성 전 유닛마다: 주인 시점 좌 · 우 열, 레인 위 깊이(0 ~ 1) · 옆 거리 (재구성 뒤 같은 쪽 새 레인에 다시 놓는다)</summary>
        private readonly Column[] _keepColumn = new Column[MatchSnapshot.MaxUnits];
        private readonly float[] _keepDepth = new float[MatchSnapshot.MaxUnits];
        private readonly float[] _keepLateral = new float[MatchSnapshot.MaxUnits];
        private readonly bool[] _keepValid = new bool[MatchSnapshot.MaxUnits];

        /// <summary>덱 밖 룬 테스트 시전 슬롯 번호 (AllowTestCasts일 때만 허용)</summary>
        public const int TestCastSlot = -1;

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
            for (int k = 0; k < _minionDefs.Length; k++)
                if (_minionDefs[k] == null) _minionDefs[k] = MinionDefinition.CreateDefault((MinionKind)k);
            _rng = new DeterministicRandom(seed);

            for (int t = 0; t < TeamUtil.TeamCount; t++) Players[t] = new PlayerState((Team)t);
            for (int t = 0; t < TeamUtil.TeamCount; t++) _decks[t] = new int[DeckData.SlotCount];
            for (int t = 0; t < TeamUtil.TeamCount; t++)
            {
                var nexus = new StructureState((Team)t, true);
                Structures[nexus.Index] = nexus;
                for (int c = 0; c < TeamUtil.ColumnCount; c++)
                {
                    var tower = new StructureState((Team)t, false, (Column)c);
                    Structures[tower.Index] = tower;
                }
            }
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

        /// <summary>
        /// 로컬 개발 전용: 덱 밖 룬도 SlotIndex = -1 로 시전 허용 (쿨타임 · 횟수 없음). PC 서버에서는 끈다.
        /// </summary>
        public bool AllowTestCasts { get; set; }

        /// <summary>§37 디버깅: 시전 확정 · 거절을 콘솔에 남긴다 (서버 창 · 에디터)</summary>
        public bool LogCasts { get; set; } = true;

        public PlayerState GetPlayer(Team team) => Players[(int)team];

        public BotPlayer GetBot(Team team) => _bots[(int)team];

        public MinionDefinition GetMinionDefinition(MinionKind kind) => _minionDefs[(int)kind];

        /// <summary>소환수 · 설치물 정의 에셋 지정 (없으면 코드 기본값)</summary>
        public void SetUnitDefinition(MinionDefinition def)
        {
            if (def != null && (int)def.Kind < _minionDefs.Length) _minionDefs[(int)def.Kind] = def;
        }

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

        /// <summary>자리별 표시 이름 (사람만, 봇은 빈 문자열)</summary>
        public readonly string[] Names = { "", "", "", "", "", "" }; // 진영 수 (TeamUtil.TeamCount)

        public void SetPlayerName(Team team, string name) => Names[(int)team] = name ?? "";

        public bool TryGetFreeTeam(out Team team)
        {
            for (int t = 0; t < Arena.TeamCount; t++)
            {
                if (Players[t].Present) continue;
                team = (Team)t;
                return true;
            }
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
            RefreshRing();
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
            Names[t] = "";
            if (brainOverride != null) _botBrainOverrides[t] = brainOverride;
            bool wasManual = _bots[t] != null && _bots[t].ManualMode;

            int[] deck = RandomDeckBuilder.Build(Catalog, _executors, Rules, BotConfig, _rng);
            System.Array.Copy(deck, _decks[t], DeckData.SlotCount);
            _bots[t] = new BotPlayer(team, _decks[t], Catalog, BotConfig, _rng.NextUInt(),
                                     _botManualKeys, _botRequireAlt, _botBrainOverrides[t]);
            _bots[t].SetManualMode(wasManual);
            RefreshRing();
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
            if (_phase == MatchPhase.Playing && !Arena.IsDuel)
            {
                // 개인전: 경기는 계속 — 살아 있으면 그 자리를 봇이 잇고, 이미 탈락했으면 관전을 그만둔 것
                if (!p.IsHuman) return;
                if (p.Eliminated) p.Connected = false;
                else TakeOverWithBot(team);
                return;
            }
            p.Connected = false;
            if (_phase != MatchPhase.Playing)
            {
                p.Present = false;
                _bots[(int)team] = null;
                if (_phase == MatchPhase.Countdown) _phase = MatchPhase.WaitingForPlayers;
                RefreshRing();
            }
            Debug.Log($"[MatchSimulation] {team} 연결 끊김 (phase {_phase})");
        }

        /// <summary>기권 — 1:1 은 연결 끊김과 같은 패배, 개인전은 지금 순위로 그 사람만 탈락 (자리를 봇이 잇지 않고, 관전 없이 떠난다)</summary>
        public void Forfeit(Team team)
        {
            if (Arena.IsDuel)
            {
                NotifyDisconnected(team);
                return;
            }
            if (_phase != MatchPhase.Playing) return;
            var p = Players[(int)team];
            if (!p.Eliminated) p.Hp = 0f;
            p.Connected = false;
        }

        /// <summary>경기 중 사람이 나간 자리를 봇이 잇는다 (HP · 슬롯 · 덱 · 구조물 그대로)</summary>
        private void TakeOverWithBot(Team team)
        {
            int t = (int)team;
            var p = Players[t];
            p.IsBot = true;
            p.Connected = true;
            _bots[t] = new BotPlayer(team, _decks[t], Catalog, BotConfig, _rng.NextUInt(), _botManualKeys, _botRequireAlt, _botBrainOverrides[t]);
            Debug.Log($"[MatchSimulation] {team} 사람이 나가서 봇이 자리를 잇습니다");
        }

        public void RemovePlayer(Team team)
        {
            var p = Players[(int)team];
            p.Present = false;
            p.Connected = false;
            _bots[(int)team] = null;
            if (_phase == MatchPhase.Countdown) _phase = MatchPhase.WaitingForPlayers;
            RefreshRing();
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

        /// <summary>
        /// 상대에게 보일 시전 준비를 갱신한다. 표시 전용이라 요청 큐를 거치지 않고, 시전 가능 여부는 여전히 ProcessCast 가 정한다.
        /// </summary>
        public void SetCastPreview(Team team, in CastPreview preview)
        {
            var p = Players[(int)team];
            if (_phase != MatchPhase.Playing || !p.Alive || preview.Phase == CastPreviewPhase.None)
            {
                p.ClearPreview();
                return;
            }
            p.Preview = preview;
            if (!Catalog.TryGet(preview.SpellId, out _)) p.Preview.SpellId = 0;
            p.PreviewLane = preview.Aiming && Arena.ColumnValid(team, preview.TargetColumn) ? (byte)Arena.ResolveLane(team, preview.TargetColumn) : PlayerSnap.NoCastLane;
            p.PreviewUntil = _matchTime + CastPreviewTimeout;
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
                    if (AllSeatsReady()) BeginCountdown();
                    break;

                case MatchPhase.Countdown:
                    if (!AllSeatsReady())
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
                            if (AllSeatsReady()) BeginCountdown();
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

            for (int t = 0; t < Players.Length; t++)
            {
                var p = Players[t];
                SlotCooldownService.Update(p, _matchTime);
                if (p.Preview.Phase != CastPreviewPhase.None && (!p.Alive || _matchTime > p.PreviewUntil)) p.ClearPreview();
            }

            for (int t = 0; t < _bots.Length; t++) _bots[t]?.Tick(this);

            ProcessQueue();
            TickRoutines(dt);
            _projectiles.Tick(this, dt);
            _status.Tick(dt);
            _waves.Tick(this, dt);
            for (int i = 0; i < Units.Length; i++) MinionAI.Tick(this, Units[i], dt);
            for (int t = 0; t < Arena.TeamCount; t++)
                for (int c = 0; c < TeamUtil.ColumnCount; c++) TowerAI.Tick(this, Defense.Tower((Team)t, (Column)c), dt);
            _regen.Tick(this, dt);
            CleanupDeadUnits();

            if (!Arena.IsDuel)
            {
                TickStorm(dt);
                TickFfaResult();
                return;
            }
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
            bool testCast = request.SlotIndex == TestCastSlot && AllowTestCasts;
            SpellDefinition def;
            var reason = testCast
                ? ValidateTestCast(caster, request, out def)
                : ServerSpellValidator.Validate(_phase, caster, request, Catalog, _executors, out def);
            if (reason == CastRejectReason.None && NeedsOpenLane(def)) reason = ValidateLane(team, def, request.TargetColumn);
            if (reason == CastRejectReason.None && !CanPlaceUnits(team, def, request)) reason = CastRejectReason.UnitCapReached;
            if (reason != CastRejectReason.None || !_executors.TryGet(request.SpellId, out ISpellExecutor executor))
            {
                if (reason == CastRejectReason.None) reason = CastRejectReason.NotImplemented;
                if (LogCasts) Debug.Log($"[MatchSimulation] 시전 거절 {team} 슬롯{request.SlotIndex + 1} {SpellIds.Code(request.SpellId)} → {reason}");
                _sink.OnCastRejected(team, request.SlotIndex, reason);
                return;
            }
            // S10 집중: 다음 스킬 1회에 배율 적용 후 소모 (집중 자신은 제외)
            float focus = 1f;
            if (def.Behavior != SpellBehaviorType.Focus && caster.HasFocus)
            {
                focus = caster.PendingFocus;
                caster.PendingFocus = 1f;
            }

            if (LogCasts)
                Debug.Log($"[MatchSimulation] 시전 확정 {team}{(caster.IsBot ? "(Bot)" : "")} {(testCast ? "테스트" : "슬롯" + (request.SlotIndex + 1))} {def.Code} {def.DisplayName} " +
                          $"열 {request.TargetColumn} 깊이 {request.TargetDepth:0.00}{(focus > 1f ? " 집중x" + focus.ToString("0.0") : "")} t={_matchTime:0.0}");

            // 시전 확정 → 쿨타임 시작 (§13.4), 활동 기록 (§10)
            if (!testCast)
            {
                SlotCooldownService.Consume(caster, request.SlotIndex, _matchTime, Rules, def.Tier, _scaling.Stage, def.CooldownOverride);
                _sink.OnCastAccepted(team, request.SlotIndex, request.SpellId);
            }
            caster.LastActivityTime = _matchTime;

            _castFocus = focus;
            try
            {
                executor.Execute(request, team);
            }
            finally
            {
                _castFocus = 1f;
            }
        }

        private CastRejectReason ValidateTestCast(PlayerState caster, in SpellCastRequest request, out SpellDefinition def)
        {
            def = null;
            if (!caster.Alive) return CastRejectReason.CasterDead;
            if (!Catalog.TryGet(request.SpellId, out def) || !_executors.Has(request.SpellId)) return CastRejectReason.NotImplemented;
            if ((int)request.TargetColumn > 4 || float.IsNaN(request.TargetDepth)) return CastRejectReason.InvalidTarget;
            return CastRejectReason.None;
        }

        /// <summary>
        /// 레인을 고르는 스킬: 그 열이 지금 상대를 가리키고(다인전은 인원마다 0 ~ 4), 상대가 살아 있어야 하며,
        /// 다인전의 소환 · 설치는 이웃과 잇는 변(좌 · 우 레인)에만 놓인다 (대각선은 직접 공격 전용)
        /// </summary>
        private CastRejectReason ValidateLane(Team team, SpellDefinition def, Column column)
        {
            if (!Arena.ColumnValid(team, column)) return CastRejectReason.InvalidTarget;
            int lane = Arena.ResolveLane(team, column);
            if (!IsLaneOpen(team, lane)) return CastRejectReason.LaneClosed;
            bool placement = def.Executor == SpellExecutorType.Summon || def.Executor == SpellExecutorType.Structure;
            if (placement && !Arena.IsSpawnLane(team, lane)) return CastRejectReason.InvalidTarget;
            return CastRejectReason.None;
        }

        /// <summary>개인전: 레인을 고르는 스킬은 상대가 살아 있는 레인에만 (자기 · 전체 스킬 제외)</summary>
        private static bool NeedsOpenLane(SpellDefinition def)
        {
            return def != null && def.Targeting != SpellTargeting.Self && def.Targeting != SpellTargeting.Tower && def.Targeting != SpellTargeting.Global;
        }

        /// <summary>§14.5 소환 · 설치: 진영 생존 상한이면 거절 (쿨타임 미소모). 설치물 교체는 자리가 생기므로 허용.</summary>
        private bool CanPlaceUnits(Team team, SpellDefinition def, in SpellCastRequest request)
        {
            if (def == null) return true;
            if (def.Executor == SpellExecutorType.Summon) return AliveUnitLimiter.CanSpawn(this, team);
            if (def.Executor == SpellExecutorType.Structure)
                return (CountTeamUnits(team, def.SummonKind, out _) >= StructureExecutor.MaxCount(def.SummonKind) || AliveUnitLimiter.CanSpawn(this, team)) &&
                       StructureExecutor.TryPlace(this, def, request, team, out _); // 겹치지 않는 빈자리가 있어야 한다
            return true;
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

        // ── 마나 폭풍 (4인 개인전 후반) ───────────────────────

        /// <summary>폭풍까지 남은 초, 시작했으면 0, 1:1 은 -1</summary>
        public float StormIn => Arena.IsDuel ? -1f : Mathf.Max(0f, Rules.StormStartSeconds - _matchTime);

        private void TickStorm(float dt)
        {
            if (_matchTime < Rules.StormStartSeconds) return;
            _stormTimer -= dt;
            if (_stormTimer > 0f) return;
            _stormTimer += Mathf.Max(0.5f, Rules.StormInterval);
            float playerDamage = Rules.StormPlayerDamage + Rules.StormPlayerRamp * _stormTicks;
            _stormTicks++;
            for (int t = 0; t < Arena.TeamCount; t++)
            {
                var team = (Team)t;
                if (!IsPlayerAlive(team)) continue;
                if (Defense.TowerAlive(team))
                {
                    for (int c = 0; c < TeamUtil.ColumnCount; c++)
                    {
                        var tower = Defense.Tower(team, (Column)c);
                        if (tower.Alive) Resolver.ResolveStorm(tower.Id, Rules.StormStructureDamage);
                    }
                }
                else if (Defense.NexusAlive(team))
                {
                    Resolver.ResolveStorm(Defense.Nexus(team).Id, Rules.StormStructureDamage);
                }
                Resolver.ResolveStorm(TeamUtil.PlayerId(team), playerDamage);
            }
        }

        // ── 4인 개인전 탈락 · 순위 ─────────────────────────────

        /// <summary>쓰러진 사람을 탈락시키고 (같은 틱이면 같은 순위), 한 명 남으면 끝낸다</summary>
        private void TickFfaResult()
        {
            int alive = 0, fallen = 0;
            for (int t = 0; t < Arena.TeamCount; t++)
            {
                var p = Players[t];
                _fallen[t] = false;
                if (!p.Present || p.Eliminated) continue;
                if (!p.Alive || AfkMonitor.IsForfeit(p, _matchTime, Rules))
                {
                    _fallen[t] = true;
                    fallen++;
                }
                else alive++;
            }

            if (fallen > 0)
            {
                for (int t = 0; t < Arena.TeamCount; t++)
                    if (_fallen[t]) Eliminate((Team)t, alive + 1);
                if (!Arena.IsPolygon) ReassignLanes();
                else if (alive >= 2)
                {
                    RememberLanes();
                    _rebuilding = true;
                    if (RefreshRing()) RebuildPolygon();
                    _rebuilding = false;
                }
            }

            if (alive == 0)
            {
                EndMatch(new MatchOutcome { Decided = true, Winner = MatchWinner.Draw, Reason = MatchEndReason.Draw });
                return;
            }
            if (alive == 1)
            {
                EndFfaWith(MatchEndReason.PlayerDeath);
                return;
            }
            // 싸우는 사람도 관전하는 사람도 없으면 봇끼리 끝까지 두지 않는다 — 남은 HP 순으로 순위를 매긴다
            if (_hadHuman && !AnyHumanWatching()) EndFfaWith(MatchEndReason.PlayerDeath);
        }

        private void Eliminate(Team team, int place)
        {
            int t = (int)team;
            var p = Players[t];
            p.Eliminated = true;
            p.Place = place;
            p.Hp = 0f;
            _bots[t] = null;
            _status.ClearTarget(p.Id);
            for (int c = 0; c < TeamUtil.ColumnCount; c++) Defense.Tower(team, (Column)c).Alive = false;
            Defense.Nexus(team).Alive = false;
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (!u.Active || u.Team != team) continue;
                _status.ClearTarget(u.Id);
                u.Deactivate();
            }
            Debug.Log($"[MatchSimulation] {team} 탈락 → {place}위 t={_matchTime:0.0}");
        }

        /// <summary>
        /// 탈락한 상대에게 가던 유닛은 살아 있는 다음 상대의 레인으로 갈아탄다 — 레인 사이는 낭떠러지라
        /// 가로질러 가지 않고 예전 레인을 따라 내 발판까지 돌아간 뒤 새 레인으로 나간다 (MinionAI 우회).
        /// 설치물은 자리를 지키고, 소환탑이 그 뒤로 내보낸 해골은 다음 틱에 같은 방식으로 갈아탄다.
        /// </summary>
        private void ReassignLanes()
        {
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (!u.Active || u.Dead || u.IsStructure || IsLaneOpen(u.Team, u.Lane)) continue;
                Team from = Arena.LaneTarget(u.Team, u.Lane);
                for (int step = 1; step < 4; step++)
                {
                    Team next = TeamUtil.TeamAtClock(TeamUtil.ClockIndex(from) + step);
                    if (next == u.Team || !IsPlayerAlive(next)) continue;
                    if (u.ReturnLane < 0) u.ReturnLane = u.Lane; // 이미 우회 중이면 처음 레인으로 돌아가는 길을 그대로 쓴다
                    u.Lane = Arena.LaneBetween(u.Team, next);
                    u.TargetId = 0;
                    break;
                }
            }
        }

        // ── 다인전 링 ─────────────────────────────────────────

        /// <summary>다인전: 링 = 자리에 있는 사람 중 탈락하지 않은 사람 (진영 번호 순). 바뀌면 true</summary>
        private bool RefreshRing()
        {
            if (!Arena.IsPolygon) return false;
            if (_phase == MatchPhase.Playing && !_rebuilding) return false; // 경기 중 링은 탈락으로만 바뀐다
            int mask = 0;
            for (int t = 0; t < TeamUtil.TeamCount; t++)
                if (Players[t].Present && !Players[t].Eliminated) mask |= 1 << t;
            return Arena.SetRing(mask);
        }

        /// <summary>다인전 재구성 직전 (옛 링): 유닛마다 주인 시점 좌 · 우 열과 레인 위 자리를 적어 둔다</summary>
        private void RememberLanes()
        {
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                _keepValid[i] = u.Active && !u.Dead && Arena.TryLocalColumn(u.Team, u.Lane, out _keepColumn[i]);
                if (!_keepValid[i]) continue;
                var frame = Arena.GetLane(u.Team, u.Lane);
                _keepDepth[i] = frame.Depth01(u.Position);
                _keepLateral[i] = frame.Lateral(u.Position);
            }
        }

        /// <summary>
        /// 다인전: 탈락으로 링이 바뀌면 전장을 N-1각형(2명이면 1:1)으로 다시 짠다. 레인은 주인 시점 같은 쪽(좌 · 우)의 새 레인으로 —
        /// 설치물(터렛 · 소환탑)은 HP 그대로 레인 위 같은 깊이 · 옆 자리에 다시 서고, 미니언 · 소환수는 HP 그대로 출발 지점부터 다시 나간다.
        /// 날아가던 투사체 · 진행 중인 스킬은 지우고, 넥서스는 HP 그대로 새 자리로 옮긴다
        /// </summary>
        private void RebuildPolygon()
        {
            int order = 0;
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (!u.Active) continue;
                int lane = _keepValid[i] && Arena.HasTeam(u.Team) ? Arena.ResolveLane(u.Team, _keepColumn[i]) : -1;
                if (lane < 0 || !Arena.IsSpawnLane(u.Team, lane))
                {
                    _status.ClearTarget(u.Id);
                    u.Deactivate();
                    continue;
                }
                var frame = Arena.GetLane(u.Team, lane);
                u.Lane = lane;
                u.ReturnLane = -1;
                u.TargetId = 0;
                u.RetargetTimer = 0f;
                u.Position = u.IsStructure
                    ? Arena.ClampToField(frame.Point(_keepDepth[i] * frame.Length, _keepLateral[i]))
                    : Arena.SpawnPosition(u.Team, lane) + Arena.ColumnAxis(u.Team) * (order++ % 2 == 0 ? -0.4f : 0.4f);
            }
            _projectiles.Clear();
            _routines.Clear();
            for (int i = 0; i < Structures.Length; i++)
            {
                var s = Structures[i];
                if (s.IsNexus && Arena.HasTeam(s.Team)) s.Position = Arena.StructurePosition(s.Team, true); // 무너진 넥서스의 잔해도 새 자리로
            }
            for (int t = 0; t < Players.Length; t++) Players[t].ClearPreview();
            Debug.Log($"[MatchSimulation] 다인전 전장 재구성 → {Arena.RingCount}{(Arena.PolygonDuelStage ? "명 (1:1 전장)" : "각형")} t={_matchTime:0.0}");
        }

        /// <summary>아직 싸우거나, 탈락했어도 접속해서 관전 중인 사람이 있는가</summary>
        private bool AnyHumanWatching()
        {
            for (int t = 0; t < Arena.TeamCount; t++)
                if (Players[t].IsHuman && (!Players[t].Eliminated || Players[t].Connected)) return true;
            return false;
        }

        /// <summary>남은 사람을 HP 순으로 순위 매기고 1위를 승자로 끝낸다</summary>
        private void EndFfaWith(MatchEndReason reason)
        {
            for (int place = 1; place <= Arena.TeamCount; place++)
            {
                int best = -1;
                for (int t = 0; t < Arena.TeamCount; t++)
                {
                    var p = Players[t];
                    if (!p.Present || p.Eliminated || p.Place != 0) continue;
                    if (best < 0 || p.Hp > Players[best].Hp) best = t;
                }
                if (best < 0) break;
                Players[best].Place = place;
            }
            var winner = MatchWinner.Draw;
            for (int t = 0; t < Arena.TeamCount; t++)
                if (Players[t].Place == 1) winner = MatchResultResolver.WinnerOf((Team)t);
            EndMatch(new MatchOutcome { Decided = true, Winner = winner, Reason = reason });
        }

        // ── 자리 ─────────────────────────────────────────────

        /// <summary>이 경기의 자리가 모두 찼는가 (1:1 = 2자리, 개인전 = 4자리)</summary>
        private bool AllSeatsReady()
        {
            if (Arena.IsPolygon)
            {
                // 다인전: 들어온 사람이 모두 접속해 있고 최소 인원이 모이면 — 카운트다운 중에 들어온 사람도 링에 낀다
                int ready = 0;
                for (int t = 0; t < TeamUtil.TeamCount; t++)
                {
                    if (!Players[t].Present) continue;
                    if (!Players[t].Connected) return false;
                    ready++;
                }
                return ready >= Mathf.Max(2, Rules.PolygonMinPlayers);
            }
            for (int t = 0; t < Arena.TeamCount; t++)
                if (!Players[t].Present || !Players[t].Connected) return false;
            return true;
        }

        /// <summary>자리에 들어온 사람 수 · 전체 자리 수 (봇 채우기 · 대기 화면)</summary>
        public int HumanCount
        {
            get
            {
                int n = 0;
                for (int t = 0; t < Arena.TeamCount; t++) if (Players[t].IsHuman) n++;
                return n;
            }
        }

        /// <summary>자리에 들어온 수 (사람 + AI)</summary>
        public int PresentCount
        {
            get
            {
                int n = 0;
                for (int t = 0; t < Arena.TeamCount; t++) if (Players[t].Present) n++;
                return n;
            }
        }

        /// <summary>대기 중: 사람이 들어온 뒤 빈자리를 AI 가 채우기까지 남은 초 (NetworkMatchState 가 넣는다, 없으면 -1)</summary>
        public float BotFillIn { get; set; } = -1f;

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
            _hadHuman = HumanCount > 0;
            if (Arena.IsPolygon)
            {
                _rebuilding = true;
                RefreshRing();
                _rebuilding = false;
                ResetStructures(); // 카운트다운 중에 들어온 사람까지 넣은 정N각형 자리로
            }
            Debug.Log($"[MatchSimulation] 경기 #{_matchSerial} 시작 — {Arena.Mode} 사람 {HumanCount}명 · 봇 {Arena.RingCount - HumanCount}명");
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
            for (int t = 0; t < Players.Length; t++) Players[t].ClearPreview();
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
            _stormTimer = 0f;
            _stormTicks = 0;
            for (int i = 0; i < Units.Length; i++) Units[i].Deactivate();
            for (int t = 0; t < Players.Length; t++) Players[t].ResetForMatch(Rules.PlayerMaxHp, Rules.BaseAccuracy);
            RefreshRing();
            ResetStructures();
        }

        /// <summary>구조물을 지금 자리에 새로 세운다 — 1:1 은 가운데 타워 + 넥서스, 4인은 레인마다 타워 + 넥서스, 다인전은 넥서스만</summary>
        private void ResetStructures()
        {
            for (int i = 0; i < Structures.Length; i++)
            {
                var s = Structures[i];
                bool used = s.IsNexus || (!Arena.IsPolygon && (s.Column == Column.Center || !Arena.IsDuel)); // 왼쪽 · 오른쪽 타워는 4인 개인전 전용
                s.Reset(!used ? 0f : s.IsNexus ? Rules.NexusMaxHp : Rules.TowerMaxHp,
                        s.IsNexus ? Arena.StructurePosition(s.Team, true) : Arena.TowerPosition(s.Team, s.Column), Rules.StructureRadius);
                if (!used || !Arena.HasTeam(s.Team)) s.Alive = false; // 이 경기에서 쓰지 않는 자리
            }
        }

        // ── 유닛 ─────────────────────────────────────────────

        public bool SpawnUnit(Team team, MinionDefinition def, int lane, Vector2 position)
        {
            return SpawnUnit(team, def, lane, position, 1f, out _);
        }

        public bool SpawnUnit(Team team, MinionDefinition def, int lane, Vector2 position, float hpMultiplier, out int unitId)
        {
            unitId = 0;
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (u.Active) continue;
                unitId = _nextUnitId++;
                u.Activate(unitId, team, def, lane, Arena.ClampToField(position), _matchTime, hpMultiplier);
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
                return !s.IsNexus || !Defense.TowerAlive(s.Team, attacker.Team);
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
            // 타워: 서 있는 것 하나, 넥서스: 남은 타워를 먼저 모두
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
            {
                var tower = Defense.Tower(team, (Column)c);
                if (!tower.Alive) continue;
                tower.Hp = 0f;
                OnStructureDestroyed(tower);
                if (!isNexus) return;
            }
            if (!isNexus) return;
            var nexus = Defense.Nexus(team);
            if (!nexus.Alive) return;
            nexus.Hp = 0f;
            OnStructureDestroyed(nexus);
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
