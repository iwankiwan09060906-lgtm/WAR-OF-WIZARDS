// 게임 진입점 — 04_Battle 씬의 [Spellbound] 오브젝트에 붙는다.
//
// 실행 모드
//   LocalVsBot      : 같은 프로세스 서버 + 기본 봇 (에디터 개발 · Quest 단독 봇전). 기본값.
//   DedicatedServer : PC 서버 빌드 (Fusion Server 모드). 창 모드 + 디버그 패널 + 봇 수동 조종 키.
//   FusionClient    : Quest(또는 에디터)가 PC 서버 세션에 접속.
//   Auto            : 명령줄 -server / -client 또는 UNITY_SERVER 빌드면 해당 모드, 아니면 LocalVsBot.
//
// 메타 UI(metaUi, 기본 켜짐): 전투 전에 C01 로그인 → C02 로비 → … → C11 카운트다운 화면이 먼저 뜨고,
//   C11에서 BeginBattle(덱)으로 경기를 시작한다 (UI/Common/MetaFlow). 끄면 예전처럼 바로 봇전이 시작된다.
// 데이터 에셋이 비어 있으면 런타임 기본값(스킬 19종, 기본 수치)으로 동작한다.
// 메뉴 Spellbound > 1. Setup Battle Scene 이 에셋 · 프리팹 · 씬 참조를 자동으로 채운다.

using Fusion;
using SpellboundVR.Arena;
using SpellboundVR.Backend;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Deck;
using SpellboundVR.Gesture;
using SpellboundVR.Input;
using SpellboundVR.Network;
using SpellboundVR.Presentation;
using SpellboundVR.Server;
using SpellboundVR.Spells;
using SpellboundVR.UI;
using SpellboundVR.Utils;
using SpellboundVR.XR;
using UnityEngine;

namespace SpellboundVR.Core
{
    public enum BootMode
    {
        Auto,
        LocalVsBot,
        DedicatedServer,
        FusionClient,
    }

    public enum InputMode
    {
        Auto,
        HandTracking,
        Keyboard,
        /// <summary>웹캠 + MediaPipe 손 추적기 (PC, GestureTools/MediaPipe)</summary>
        MediaPipe,
    }

    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("실행 모드")]
        public BootMode bootMode = BootMode.Auto;
        [Tooltip("Fusion 세션 이름 (서버 · 클라이언트가 같아야 함)")]
        public string sessionName = "spellbound-dev";
        [Tooltip("서버 UDP 포트 (0 = 자동)")]
        public ushort serverPort;

        [Header("데이터 (비우면 런타임 기본값)")]
        public MatchRuleConfig rules;
        public SpellCatalog spellCatalog;
        public RuneTemplateLibrary runeLibrary;
        public BotConfig botConfig;
        public MinionDefinition meleeDefinition;
        public MinionDefinition rangedDefinition;
        public MinionDefinition bruteDefinition;
        [Tooltip("소환수 · 설치물 정의 (Minion_AxeOrc · Dragon · Skeleton · Turret · SummonHut). 비우면 코드 기본값")]
        public MinionDefinition[] unitDefinitions = new MinionDefinition[0];
        public VFXCatalog vfxCatalog;

        [Header("씬 참조 (비우면 자동 탐색)")]
        public ArenaLayout arenaLayout;
        public Transform playerRig;
        public Transform head;
        public Transform leftHandAnchor;
        public Transform rightHandAnchor;

        [Header("네트워크 프리팹 (Prefabs/Network)")]
        public NetworkObject matchStatePrefab;
        public NetworkObject playerPrefab;

        [Header("입력")]
        public InputMode inputMode = InputMode.Auto;
        [Tooltip("키보드(데스크톱) 모드에서 시점을 아래로 기울이는 각도 — 헤드셋에서는 사용 안 함")]
        public float desktopViewPitch = 28f;
        [Tooltip("웹캠 룬 그리기: 획을 끝내고 이 시간(초) 동안 다음 획이 없으면 그리기를 끝낸다")]
        public float webcamRuneFinishSeconds = 1.2f;

        [Header("경기 종류")]
        [Tooltip("서버 빌드가 여는 경기 — 메타 UI 경기는 C02 에서 고른 인원을 따른다. 명령줄 -duel 이면 1:1")]
        public ArenaMode serverArenaMode = ArenaMode.Ffa4;

        [Header("로컬 모드")]
        public Team localTeam = Team.Home;
        [Tooltip("false면 로컬 모드에서 AFK 기권을 끈다 (에디터 개발 편의)")]
        public bool localAfkEnabled = true;
        public bool showDebugPanels = true;

        [Header("메타 UI (C01~C13)")]
        [Tooltip("true: 로그인 · 로비 · 마법서 · 매칭 등 게임 밖 화면을 먼저 띄우고, 대전을 시작하면 전투로 들어간다. false: 바로 봇전 (전투만 테스트)")]
        public bool metaUi = true;

        [Header("룬 인식 테스트")]
        [Tooltip("true: 덱(스킬창)에 없는 룬 19종도 인식해 시전 (로컬 봇전에서만 실제 발동 — 쿨타임 · 횟수 없음). 덱 안의 룬은 원래대로 시전")]
        public bool recognizeAllRunes = true;

        [Header("내 덱 (SpellId, 0 = 빈 슬롯) — 마법서(C09) 연동 전 임시")]
        public int[] localDeck = { SpellIds.S03_Fireball, SpellIds.S07_Gatling, SpellIds.S08_Shield, SpellIds.S01_Smoke,
                                   SpellIds.S11_AxeWarrior, SpellIds.S13_Turret, SpellIds.H01_Freeze, SpellIds.H04_ReflectBarrier };

        private BootMode _resolvedMode;
        private ClientSession _session;
        private EightSlotDeckManager _deckManager;
        private LocalMatchHost _localHost;
        private FusionServerLauncher _serverLauncher;
        private ClientConnector _clientConnector;
        private ArenaStateView _arenaView;
        private MatchSimulation _serverSim;
        private ISpellInputSource _input;
        private CastStateMachine _cast;
        private bool _allowAllRunes;
        private bool _battleOnline;
        private ArenaMode _arenaMode = ArenaMode.Duel;
        /// <summary>이번 경기에 쓰는 규칙 (개인전이면 값을 바꾼 사본)</summary>
        private MatchRuleConfig _matchRules;

        public BootMode ResolvedMode => _resolvedMode;

        public ClientSession Session => _session;

        /// <summary>지금 경기가 PC 서버(Fusion)에 접속한 경기인지 (false = 이 기기 안의 로컬 봇전)</summary>
        public bool IsOnlineBattle => _battleOnline;

        /// <summary>자리 표시 이름 (메타 UI 가 넣는다)</summary>
        public string LocalPlayerName { get; set; } = "";

        private void Awake()
        {
            ResolveData();
            ResolveSceneReferences();
            _resolvedMode = ResolveMode();
            _allowAllRunes = recognizeAllRunes;
            Debug.Log($"[GameBootstrap] 실행 모드: {_resolvedMode}");

            if (_resolvedMode != BootMode.DedicatedServer && metaUi)
            {
                // 전투는 C11 카운트다운에서 BeginBattle로 시작한다
                gameObject.AddComponent<SpellboundVR.UI.MetaFlow>().Initialize(this, WantsHandTracking(), head, rightHandAnchor);
                return;
            }

            switch (_resolvedMode)
            {
                case BootMode.DedicatedServer:
                    StartDedicatedServer();
                    break;
                case BootMode.FusionClient:
                    StartClient(networked: true);
                    break;
                default:
                    StartClient(networked: false);
                    break;
            }
        }

        private void OnDestroy()
        {
            _deckManager?.Dispose();
        }

        /// <summary>메타 UI(C11)에서 대전 시작 — deck: SpellId 8개 (0 = 빈 슬롯). 한 씬에서 한 번만.</summary>
        public void BeginBattle(int[] deck) => BeginBattle(deck, _resolvedMode == BootMode.FusionClient, recognizeAllRunes);

        /// <summary>
        /// online: PC 서버(Fusion) 경기 (FusionClient 빌드의 랭킹전 · 친선전) / false: 이 기기 안에서 봇과 (봇 연습 · 스킬 테스트).
        /// allowAllRunes: 덱 밖 룬 19종도 시전 (스킬 테스트 전용 — 로컬 경기에서만 동작).
        /// </summary>
        public void BeginBattle(int[] deck, bool online, bool allowAllRunes, ArenaMode mode = ArenaMode.Duel)
        {
            if (_session != null) return;
            if (deck != null && deck.Length == localDeck.Length) localDeck = deck;
            _allowAllRunes = allowAllRunes && !online;
            SetArenaMode(mode);
            StartClient(networked: online);
        }

        /// <summary>전장 · 규칙을 경기 종류에 맞춘다 — 시뮬레이션을 만들기 전에 부른다</summary>
        private void SetArenaMode(ArenaMode mode)
        {
            _arenaMode = mode;
            arenaLayout.SetMode(mode);
            _matchRules = rules;
            if (mode == ArenaMode.Duel) return;
            _matchRules = Instantiate(rules); // 에셋 원본을 바꾸지 않도록 사본 사용
            _matchRules.ApplyFfaRules();
        }

        /// <summary>경기 종류별 Fusion 세션 — 서버 하나가 한 종류만 연다 (RunServer.bat · RunServer1v1.bat)</summary>
        private string SessionFor(ArenaMode mode) => mode == ArenaMode.Duel ? sessionName : sessionName + "-ffa";

        /// <summary>경기 중 메뉴를 여는 동안 룬 그리기 · 조준 · 이동 입력을 멈춘다 (경기 자체는 계속된다)</summary>
        public void SetBattleInputEnabled(bool enabled)
        {
            if (_cast != null)
            {
                if (!enabled) _cast.Cancel(CastCancelReason.None);
                _cast.enabled = enabled;
            }
            if (_input is MonoBehaviour mb && mb != null) mb.enabled = enabled;
        }

        /// <summary>기권 — 로컬 경기는 다음 틱에 패배(연결 끊김과 같은 규칙), PC 서버 경기는 접속을 끊고 이 기기에서 패배로 마무리한다</summary>
        public bool Forfeit()
        {
            if (_localHost != null && _localHost.Simulation != null)
            {
                if (_localHost.Simulation.Phase != MatchPhase.Playing) return false;
                _localHost.Simulation.Forfeit(localTeam);
                return true;
            }
            if (_clientConnector != null && _session != null)
            {
                var events = _session.Events;
                float duration = events.Latest != null ? events.Latest.Header.MatchTime : 0f;
                _clientConnector.Leave();
                events.RaiseLocalMatchEnd(MatchResult.Lose, MatchEndReason.Disconnect, duration);
                return true;
            }
            return false;
        }

        private SpectatorView _spectator;

        /// <summary>지금 보는 사람 한 줄 ("2번 (AI)"), 관전 중이 아니면 빈 문자열</summary>
        public string SpectatorTargetLabel
        {
            get
            {
                if (_spectator == null || !_spectator.Active) return "";
                var latest = _session.Events.Latest;
                bool bot = latest != null && latest.Players[(int)_spectator.Target].IsBot != 0;
                return TeamUtil.SeatNumber(_spectator.Target) + "번" + (bot ? " (AI)" : "");
            }
        }

        /// <summary>개인전 탈락 후 관전 — 남은 플레이어 시점을 돌아가며 본다. 볼 사람이 없거나 1:1 이면 false</summary>
        public bool BeginSpectating()
        {
            if (_session == null || _arenaMode != ArenaMode.Ffa4 || _session.Arena.IsDuel) return false;
            var hud = GetComponent<BattleHudView>();
            if (_spectator == null)
            {
                var platform = GetComponent<CommanderPlatform>();
                string keys = WantsHandTracking() ? "A 다음  B 이전  ·  메뉴에서 전환 · 결과 보기" : "Tab · → 다음  ← 이전  ·  Esc 메뉴";
                _spectator = gameObject.AddComponent<SpectatorView>();
                _spectator.Initialize(_session, playerRig, platform != null ? platform.viewPitch : 0f);
                _spectator.OnTargetChanged += team =>
                {
                    if (_arenaView != null) _arenaView.SetViewpoint(team);
                    if (hud != null) hud.SetSpectating(team, "관전 중 — " + SpectatorTargetLabel + "   ·   " + keys);
                };
            }
            if (!_spectator.Begin()) return false;
            SetBattleInputEnabled(false);
            var mover = GetComponent<LocalMovePredictor>();
            if (mover != null) mover.enabled = false;
            return true;
        }

        /// <summary>다음(dir=+1) · 이전(dir=-1) 사람을 본다 (경기 메뉴 버튼)</summary>
        public void StepSpectator(int dir)
        {
            if (_spectator != null) _spectator.Step(dir);
        }

        public void SetSpectatorInputEnabled(bool enabled)
        {
            if (_spectator != null) _spectator.InputEnabled = enabled;
        }

        /// <summary>경기가 끝나 관전을 마친다 — 결과 화면이 뜰 때까지 마지막 시점을 그대로 둔다</summary>
        public void EndSpectating()
        {
            if (_spectator == null || !_spectator.Active) return;
            _spectator.End();
            var hud = GetComponent<BattleHudView>();
            if (hud != null) hud.SetSpectating(null, "경기가 끝났습니다");
        }

        /// <summary>경기가 끝나기 전에 관전을 그만두고 떠난다 — 서버가 관전자가 없다는 걸 알아야 봇끼리 경기를 끝낸다</summary>
        public void LeaveSpectating()
        {
            EndSpectating();
            if (_localHost != null && _localHost.Simulation != null) _localHost.Simulation.NotifyDisconnected(localTeam);
            else if (_clientConnector != null) _clientConnector.Leave();
        }

        /// <summary>헤드셋 핸드트래킹을 쓸지 (전투 입력 · 메타 UI 조작 방식이 같이 바뀐다)</summary>
        public bool WantsHandTracking()
        {
            switch (inputMode)
            {
                case InputMode.HandTracking: return leftHandAnchor != null && rightHandAnchor != null;
                case InputMode.Keyboard: return false;
                default: return UnityEngine.XR.XRSettings.isDeviceActive && leftHandAnchor != null && rightHandAnchor != null;
            }
        }

        // ── 준비 ─────────────────────────────────────────────

        private void ResolveData()
        {
            if (rules == null) rules = MatchRuleConfig.CreateDefault();
            _matchRules = rules;
            if (spellCatalog == null) spellCatalog = SpellCatalog.CreateDefault();
            if (runeLibrary == null) runeLibrary = RuneTemplateLibrary.CreateDefault();
            if (botConfig == null) botConfig = BotConfig.CreateDefault();
            if (vfxCatalog == null) vfxCatalog = VFXCatalog.CreateDefault();
            if (meleeDefinition == null) meleeDefinition = MinionDefinition.CreateDefault(MinionKind.Melee);
            if (rangedDefinition == null) rangedDefinition = MinionDefinition.CreateDefault(MinionKind.Ranged);
            if (bruteDefinition == null) bruteDefinition = MinionDefinition.CreateDefault(MinionKind.Brute);
        }

        private void ResolveSceneReferences()
        {
            if (arenaLayout == null) arenaLayout = FindFirstObjectByType<ArenaLayout>();
            if (arenaLayout == null)
            {
                var go = new GameObject("[ArenaLayout]");
                arenaLayout = go.AddComponent<ArenaLayout>();
                Debug.LogWarning("[GameBootstrap] ArenaLayout이 없어 원점 기준 기본 레이아웃을 생성했습니다.");
            }

            if (playerRig == null)
            {
                var rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig != null)
                {
                    playerRig = rig.transform;
                    if (head == null) head = rig.centerEyeAnchor;
                    if (leftHandAnchor == null) leftHandAnchor = rig.leftHandAnchor;
                    if (rightHandAnchor == null) rightHandAnchor = rig.rightHandAnchor;
                }
            }
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null)
            {
                var camGo = new GameObject("Fallback Camera");
                camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.transform.position = new Vector3(0f, 3f, 0f);
                head = camGo.transform;
            }
            if (playerRig == null) playerRig = head.parent != null ? head.root : head;
        }

        private BootMode ResolveMode()
        {
            // 경기 종류 · 세션은 빌드에 실행 모드가 박혀 있어도 읽는다 (RunServer1v1.bat 의 -duel)
            var args = System.Environment.GetCommandLineArgs();
            BootMode requested = BootMode.LocalVsBot;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-server" || args[i] == "-spellboundServer") requested = BootMode.DedicatedServer;
                if (args[i] == "-client" || args[i] == "-spellboundClient") requested = BootMode.FusionClient;
                if (args[i] == "-session" && i + 1 < args.Length) sessionName = args[i + 1];
                if (args[i] == "-duel") serverArenaMode = ArenaMode.Duel;
                if (args[i] == "-ffa") serverArenaMode = ArenaMode.Ffa4;
            }
#if UNITY_SERVER
            return BootMode.DedicatedServer;
#else
            return bootMode != BootMode.Auto ? bootMode : requested;
#endif
        }

        private MatchSimulation CreateSimulation(bool localMode)
        {
            var effectiveRules = _matchRules;
            if (localMode && (!localAfkEnabled || metaUi))
            {
                effectiveRules = Instantiate(_matchRules); // 에셋 원본을 바꾸지 않도록 사본 사용
                if (!localAfkEnabled)
                {
                    effectiveRules.AfkForfeitSeconds = 1e9f;
                    effectiveRules.AfkWarningSeconds = 1e9f;
                }
                if (metaUi) effectiveRules.AutoRestartDelay = 0f; // 경기가 끝나면 결과 화면(C13) — 자동 재시작 없음
            }
            uint seed = (uint)System.Environment.TickCount;
            var sim = new MatchSimulation(effectiveRules, spellCatalog, arenaLayout.Geometry, botConfig,
                                          meleeDefinition, rangedDefinition, bruteDefinition, seed);
            if (unitDefinitions != null)
                for (int i = 0; i < unitDefinitions.Length; i++) sim.SetUnitDefinition(unitDefinitions[i]);
            sim.AllowTestCasts = localMode && _allowAllRunes; // 덱 밖 룬 테스트 시전 (스킬 테스트 · 메타 UI 없는 개발 모드, PC 서버는 항상 거절)
            return sim;
        }

        // ── 클라이언트 (로컬 / Fusion) ─────────────────────────

        private void StartClient(bool networked)
        {
            var events = new NetworkGameEventPublisher(arenaLayout.Geometry);
            var deck = new DeckData(localDeck);
            _session = new ClientSession(_matchRules, spellCatalog, runeLibrary, arenaLayout, events, deck);
            _deckManager = new EightSlotDeckManager(deck, events);

            ISpellInputSource input = CreateInput();
            _input = input;
            _battleOnline = networked;

            // 리그 · 이동
            var platform = gameObject.AddComponent<CommanderPlatform>();
            platform.Initialize(arenaLayout, playerRig);
            var vignette = gameObject.AddComponent<SnapVignette>();
            vignette.Initialize(head);
            platform.OnSnapped += vignette.Play;
            var mover = gameObject.AddComponent<LocalMovePredictor>();
            mover.Initialize(_session, input, platform);

            // 시전 흐름
            var markerGo = new GameObject("[AimMarker]");
            var marker = markerGo.AddComponent<AimMarkerView>();
            marker.Initialize(vfxCatalog);
            var aim = gameObject.AddComponent<AimPointer>();
            aim.markerView = marker;
            var cast = gameObject.AddComponent<CastStateMachine>();
            cast.aimPointer = aim;
            cast.recognizeAllRunes = _allowAllRunes && !networked; // PC 서버는 덱 밖 테스트 시전을 항상 거절하므로 네트워크 모드에선 덱 룬만 인식
            cast.Initialize(_session, input, _deckManager);
            _cast = cast;
            gameObject.AddComponent<CastPreviewReporter>().Initialize(_session, input, cast, _deckManager, head);

            // 표시
            var lineGo = new GameObject("[MagicLine]", typeof(LineRenderer));
            lineGo.AddComponent<MagicLineRenderer>().Initialize(input, cast, input is KeyboardInput ? 0.004f : 0.006f);
            gameObject.AddComponent<RuneReadyDisplay>().Initialize(cast, input, head, runeLibrary);
            var presentation = new GameObject("[Presentation]");
            if (_arenaMode == ArenaMode.Ffa4) presentation.AddComponent<FfaArenaView>().Initialize(_session); // 구조물 · 팀 아트를 쓰기 전에
            presentation.AddComponent<VFXPlayer>().Initialize(_session, vfxCatalog);
            _arenaView = presentation.AddComponent<ArenaStateView>();
            _arenaView.Initialize(_session, vfxCatalog, head);
            presentation.AddComponent<CastPreviewView>().Initialize(_session, _arenaView, head, vfxCatalog);
            presentation.AddComponent<DamageTextView>().Initialize(_session, head);
            presentation.AddComponent<BlindOverlayView>().Initialize(_session, head);
            var hud = gameObject.AddComponent<BattleHudView>();
            if (!(input is HandTrackingInput))
            {
                // 데스크톱 화면(헤드셋 없음): 고개를 숙일 수 없으니 시점을 아래로 기울이고 HUD는 화면 하단에 작게
                platform.viewPitch = desktopViewPitch;
                hud.localPosition = new Vector3(0f, -0.43f, 1.0f);
                hud.tiltDegrees = 10f;
                hud.worldScale = 0.0005f;
            }
            hud.localPosition += head.parent != null ? head.localPosition : Vector3.zero; // 시작 시 머리 높이 기준
            hud.Initialize(_session, head.parent != null ? head.parent : playerRig);
            gameObject.AddComponent<BattleCoach>().Initialize(_session, hud);
            if (input is MediaPipeHandInput tracker)
            {
                var settings = SpellboundVR.UI.MetaState.Settings;
                if (settings.webcamDrawRunes)
                {
                    cast.autoFinishSeconds = webcamRuneFinishSeconds;
                    cast.aimAfterRune = true;
                }
                tracker.Bind(hud, _deckManager, deck.ToArray(), cast, settings.webcamDrawRunes, settings.holdFrames, runeLibrary);
            }

            platform.SetTeam(localTeam, 1);

            if (networked)
            {
                _clientConnector = gameObject.AddComponent<ClientConnector>();
                _clientConnector.Connect(SessionFor(_arenaMode), _session, deck.ToArray(), LocalPlayerName);
                if (showDebugPanels)
                {
                    var overlay = gameObject.AddComponent<DebugOverlay>();
                    overlay.Initialize(() => events.Latest != null ? events.Latest.UnitCount : 0,
                                       () => events.Latest != null ? events.Latest.Header.MatchTime : 0f,
                                       () => _clientConnector.PingMs);
                }
            }
            else
            {
                var sim = CreateSimulation(localMode: true);
                sim.ConfigureBotManualKeys(true, botConfig.RequireAltInLocalMode);
                _localHost = gameObject.AddComponent<LocalMatchHost>();
                _localHost.Initialize(sim, events, localTeam, deck.ToArray(), fillOpponentWithBot: true, LocalPlayerName);
                _session.BindClient(_localHost);

                if (showDebugPanels)
                {
                    gameObject.AddComponent<ServerDebugPanel>().Initialize(() => _localHost.Simulation, () => "Mode: LocalVsBot  Input: " + input.GetType().Name, true);
                    gameObject.AddComponent<ServerTestScenarios>().Initialize(() => _localHost.Simulation);
                    var overlay = gameObject.AddComponent<DebugOverlay>();
                    overlay.Initialize(() => CountAllUnits(sim), () => sim.MatchTime, null);
                }
            }
        }

        private static int CountAllUnits(MatchSimulation sim)
        {
            int n = 0;
            for (int t = 0; t < sim.Arena.TeamCount; t++) n += AliveUnitLimiter.CountAlive(sim, (Team)t);
            return n;
        }

        private ISpellInputSource CreateInput()
        {
            bool useHands = WantsHandTracking();

            var inputGo = new GameObject("[Input]");
            inputGo.transform.SetParent(transform, false);
            // 헤드셋 없는 PC 의 기본 입력: 웹캠 + MediaPipe 손 추적기 (환경설정 "웹캠 손 입력"을 끄면 마우스 · 키보드)
            bool webcam = inputMode == InputMode.MediaPipe || (inputMode != InputMode.Keyboard && SpellboundVR.UI.MetaState.Settings.webcamInput);
            if (!useHands && webcam && !Application.isBatchMode && WebcamTracker.Available)
            {
                var tracker = inputGo.AddComponent<MediaPipeHandInput>();
                tracker.viewCamera = head != null ? head.GetComponent<Camera>() : null;
                Debug.Log("[GameBootstrap] 입력: MediaPipeHandInput (웹캠 손 추적)");
                return tracker;
            }
            if (useHands && leftHandAnchor != null && rightHandAnchor != null)
            {
                var left = HandJointProvider.EnsureForAnchor(leftHandAnchor, false);
                var right = HandJointProvider.EnsureForAnchor(rightHandAnchor, true);
                var hands = inputGo.AddComponent<HandTrackingInput>();
                // 왼손잡이 모드: 입력의 "오른손"(스킬 손) 자리에 왼손을 넣는다
                bool swap = SpellboundVR.UI.MetaState.Settings.leftHanded;
                hands.Configure(swap ? right : left, swap ? left : right, head);
                Debug.Log("[GameBootstrap] 입력: HandTrackingInput (Quest 핸드트래킹)");
                return hands;
            }

            var keyboard = inputGo.AddComponent<KeyboardInput>();
            keyboard.viewCamera = head != null ? head.GetComponent<Camera>() : null;
            Debug.Log("[GameBootstrap] 입력: KeyboardInput (에디터 대체 입력 — 1~8 시전, 마우스 드래그 룬, F/G/X, A/D)");
            return keyboard;
        }

        // ── PC 전용 서버 ─────────────────────────────────────

        private void StartDedicatedServer()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;

            SetArenaMode(serverArenaMode);
            _serverSim = CreateSimulation(localMode: false);
            _serverSim.ConfigureBotManualKeys(true, false);

            // 서버 창 표시용 (Home 시점) — 판정과 무관
            var view = new NetworkGameEventPublisher(arenaLayout.Geometry);
            _session = new ClientSession(_matchRules, spellCatalog, runeLibrary, arenaLayout, view, new DeckData(localDeck));
            view.SetLocalTeam(Team.Home);
            var presentation = new GameObject("[Server View]");
            if (_arenaMode == ArenaMode.Ffa4) presentation.AddComponent<FfaArenaView>().Initialize(_session);
            presentation.AddComponent<VFXPlayer>().Initialize(_session, vfxCatalog);
            var serverArena = presentation.AddComponent<ArenaStateView>();
            serverArena.Initialize(_session, vfxCatalog, head);
            presentation.AddComponent<CastPreviewView>().Initialize(_session, serverArena, head, vfxCatalog);
            presentation.AddComponent<DamageTextView>().Initialize(_session, head);

            // 서버 창 카메라: 대기 중엔 Home 뒤 시점, 경기가 시작되면 위에서 비스듬히 내려다보는 사이드뷰 (V로 전환)
            gameObject.AddComponent<ServerSpectatorCamera>().Initialize(arenaLayout.Geometry, playerRig, head, view);

            _serverLauncher = gameObject.AddComponent<FusionServerLauncher>();
            _serverLauncher.StartServer(SessionFor(_arenaMode), serverPort, matchStatePrefab, playerPrefab, _serverSim, view);

            gameObject.AddComponent<ServerDebugPanel>().Initialize(() => _serverSim,
                () => _serverLauncher.Status + "  players " + _serverLauncher.ConnectedPlayers, false);
            gameObject.AddComponent<ServerTestScenarios>().Initialize(() => _serverSim);
            gameObject.AddComponent<DebugOverlay>().Initialize(
                () => CountAllUnits(_serverSim), () => _serverSim.MatchTime, null);
        }
    }
}
