// 게임 진입점 — 04_Battle 씬의 [Spellbound] 오브젝트에 붙는다.
//
// 실행 모드
//   LocalVsBot      : 같은 프로세스 서버 + 기본 봇 (에디터 개발 · Quest 단독 봇전). 기본값.
//   DedicatedServer : PC 서버 빌드 (Fusion Server 모드). 창 모드 + 디버그 패널 + 봇 수동 조종 키.
//   FusionClient    : Quest(또는 에디터)가 PC 서버 세션에 접속.
//   Auto            : 명령줄 -server / -client 또는 UNITY_SERVER 빌드면 해당 모드, 아니면 LocalVsBot.
//
// 데이터 에셋이 비어 있으면 런타임 기본값(S03 / S07 / S08, 기본 수치)으로 동작한다.
// 메뉴 Spellbound > 1. Setup Battle Scene 이 에셋 · 프리팹 · 씬 참조를 자동으로 채운다.

using Fusion;
using SpellboundVR.Arena;
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

        [Header("로컬 모드")]
        public Team localTeam = Team.Home;
        [Tooltip("false면 로컬 모드에서 AFK 기권을 끈다 (에디터 개발 편의)")]
        public bool localAfkEnabled = true;
        public bool showDebugPanels = true;

        [Header("내 덱 (SpellId, 0 = 빈 슬롯) — 마법서(C09) 연동 전 임시")]
        public int[] localDeck = { SpellIds.S03_Fireball, SpellIds.S07_Gatling, SpellIds.S08_Shield, 0, 0, 0, 0, 0 };

        private BootMode _resolvedMode;
        private ClientSession _session;
        private EightSlotDeckManager _deckManager;
        private LocalMatchHost _localHost;
        private FusionServerLauncher _serverLauncher;
        private ClientConnector _clientConnector;
        private MatchSimulation _serverSim;

        public BootMode ResolvedMode => _resolvedMode;

        public ClientSession Session => _session;

        private void Awake()
        {
            ResolveData();
            ResolveSceneReferences();
            _resolvedMode = ResolveMode();
            Debug.Log($"[GameBootstrap] 실행 모드: {_resolvedMode}");

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

        // ── 준비 ─────────────────────────────────────────────

        private void ResolveData()
        {
            if (rules == null) rules = MatchRuleConfig.CreateDefault();
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
#if UNITY_SERVER
            return BootMode.DedicatedServer;
#else
            if (bootMode != BootMode.Auto) return bootMode;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-server" || args[i] == "-spellboundServer") return BootMode.DedicatedServer;
                if (args[i] == "-client" || args[i] == "-spellboundClient") return BootMode.FusionClient;
                if (args[i] == "-session" && i + 1 < args.Length) sessionName = args[i + 1];
            }
            return BootMode.LocalVsBot;
#endif
        }

        private MatchSimulation CreateSimulation(bool localMode)
        {
            var effectiveRules = rules;
            if (localMode && !localAfkEnabled)
            {
                effectiveRules = Instantiate(rules); // 에셋 원본을 바꾸지 않도록 사본 사용
                effectiveRules.AfkForfeitSeconds = 1e9f;
                effectiveRules.AfkWarningSeconds = 1e9f;
            }
            uint seed = (uint)System.Environment.TickCount;
            var sim = new MatchSimulation(effectiveRules, spellCatalog, arenaLayout.Geometry, botConfig,
                                          meleeDefinition, rangedDefinition, bruteDefinition, seed);
            return sim;
        }

        // ── 클라이언트 (로컬 / Fusion) ─────────────────────────

        private void StartClient(bool networked)
        {
            var events = new NetworkGameEventPublisher(arenaLayout.Geometry);
            var deck = new DeckData(localDeck);
            _session = new ClientSession(rules, spellCatalog, runeLibrary, arenaLayout, events, deck);
            _deckManager = new EightSlotDeckManager(deck, events);

            ISpellInputSource input = CreateInput();

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
            var aim = gameObject.AddComponent<AimPointer>();
            aim.markerView = marker;
            var cast = gameObject.AddComponent<CastStateMachine>();
            cast.aimPointer = aim;
            cast.Initialize(_session, input, _deckManager);

            // 표시
            var lineGo = new GameObject("[MagicLine]", typeof(LineRenderer));
            lineGo.AddComponent<MagicLineRenderer>().Initialize(input, cast, input is KeyboardInput ? 0.004f : 0.006f);
            gameObject.AddComponent<RuneReadyDisplay>().Initialize(cast, input, head);
            var presentation = new GameObject("[Presentation]");
            presentation.AddComponent<VFXPlayer>().Initialize(_session, vfxCatalog);
            presentation.AddComponent<ArenaStateView>().Initialize(_session, vfxCatalog, head);
            presentation.AddComponent<DamageTextView>().Initialize(_session, head);
            var hud = gameObject.AddComponent<BattleHudView>();
            if (input is KeyboardInput)
            {
                // 데스크톱 화면: 전장을 가리지 않도록 화면 하단에 작게
                hud.localPosition = new Vector3(0f, -0.36f, 1.0f);
                hud.worldScale = 0.0006f;
            }
            hud.localPosition += head.parent != null ? head.localPosition : Vector3.zero; // 시작 시 머리 높이 기준
            hud.Initialize(_session, head.parent != null ? head.parent : playerRig);

            platform.SetTeam(localTeam, 1);

            if (networked)
            {
                _clientConnector = gameObject.AddComponent<ClientConnector>();
                _clientConnector.Connect(sessionName, _session, deck.ToArray());
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
                _localHost.Initialize(sim, events, localTeam, deck.ToArray(), fillOpponentWithBot: true);
                _session.BindClient(_localHost);

                if (showDebugPanels)
                {
                    gameObject.AddComponent<ServerDebugPanel>().Initialize(() => _localHost.Simulation, () => "Mode: LocalVsBot  Input: " + input.GetType().Name, true);
                    gameObject.AddComponent<ServerTestScenarios>().Initialize(() => _localHost.Simulation);
                    var overlay = gameObject.AddComponent<DebugOverlay>();
                    overlay.Initialize(() => AliveUnitLimiter.CountAlive(sim, Team.Home) + AliveUnitLimiter.CountAlive(sim, Team.Away),
                                       () => sim.MatchTime, null);
                }
            }
        }

        private ISpellInputSource CreateInput()
        {
            bool useHands;
            switch (inputMode)
            {
                case InputMode.HandTracking: useHands = true; break;
                case InputMode.Keyboard: useHands = false; break;
                default: useHands = UnityEngine.XR.XRSettings.isDeviceActive; break;
            }

            var inputGo = new GameObject("[Input]");
            inputGo.transform.SetParent(transform, false);
            if (useHands && leftHandAnchor != null && rightHandAnchor != null)
            {
                var left = HandJointProvider.EnsureForAnchor(leftHandAnchor, false);
                var right = HandJointProvider.EnsureForAnchor(rightHandAnchor, true);
                var hands = inputGo.AddComponent<HandTrackingInput>();
                hands.Configure(left, right, head);
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

            _serverSim = CreateSimulation(localMode: false);
            _serverSim.ConfigureBotManualKeys(true, false);

            // 서버 창 표시용 (Home 시점) — 판정과 무관
            var view = new NetworkGameEventPublisher(arenaLayout.Geometry);
            _session = new ClientSession(rules, spellCatalog, runeLibrary, arenaLayout, view, new DeckData(localDeck));
            view.SetLocalTeam(Team.Home);
            var presentation = new GameObject("[Server View]");
            presentation.AddComponent<VFXPlayer>().Initialize(_session, vfxCatalog);
            presentation.AddComponent<ArenaStateView>().Initialize(_session, vfxCatalog, head);
            presentation.AddComponent<DamageTextView>().Initialize(_session, head);

            if (playerRig != null)
            {
                var g = arenaLayout.Geometry;
                playerRig.SetPositionAndRotation(g.ToWorld(g.PlayerPosition(Team.Home, 1), 6f) - g.Forward * 4f,
                                                 Quaternion.LookRotation(g.Forward * 2f - Vector3.up, Vector3.up));
            }

            _serverLauncher = gameObject.AddComponent<FusionServerLauncher>();
            _serverLauncher.StartServer(sessionName, serverPort, matchStatePrefab, playerPrefab, _serverSim, view);

            gameObject.AddComponent<ServerDebugPanel>().Initialize(() => _serverSim,
                () => _serverLauncher.Status + "  players " + _serverLauncher.ConnectedPlayers, false);
            gameObject.AddComponent<ServerTestScenarios>().Initialize(() => _serverSim);
            gameObject.AddComponent<DebugOverlay>().Initialize(
                () => AliveUnitLimiter.CountAlive(_serverSim, Team.Home) + AliveUnitLimiter.CountAlive(_serverSim, Team.Away),
                () => _serverSim.MatchTime, null);
        }
    }
}
