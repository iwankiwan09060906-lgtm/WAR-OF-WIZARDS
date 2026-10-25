// 메타 UI 흐름 — 게임 밖 화면(C01~C11 · C13)과 전투(C12)를 잇는다. GameBootstrap.metaUi 가 켜져 있으면 전투 대신 이것이 먼저 뜬다.
//
//   C01 로그인 ─▶ C02 로비 ─┬─▶ C10 매칭 대기 (랭킹전) ─┐
//                           ├─▶ C06 친선전 룸 ──────────┼─▶ C11 카운트다운 ─▶ C12 전투 HUD ─▶ C13 결과 ─▶ (씬 다시 불러오기) ─▶ C02
//                           └─▶ 봇 연습전 ──────────────┘
//   그 밖에 C03 마이페이지 · C04 환경설정 · C05 친구 · C07 우편함 · C08 랭킹 · C09 마법서
//
//   · C11에 들어가면 GameBootstrap.BeginBattle(덱)로 실제 경기를 시작하고, 서버(로컬 호스트)의 카운트다운이 끝나면 메뉴를 숨긴다.
//   · 경기가 끝나면 2.5초 뒤(HUD의 VICTORY/DEFEAT를 보여준 뒤) C13. 전적 저장은 MatchResultReporter가 한다.
//   · 다시 로비 · 다시 대전은 씬을 다시 불러와 전투 상태를 깨끗이 지운다 (로그인 · 덱 · 설정은 MetaState에 남는다).
//   · 경기 종류별 접속: 봇 연습 · 스킬 테스트 = 항상 이 기기 안에서 (PC 서버 불필요),
//     랭킹전 · 친선전 = FusionClient 빌드면 PC 게임 서버, 아니면 이 기기의 봇. PC 서버에 25초 안에 붙지 못하면 로비로 돌아간다.
//   · 경기 중 메뉴: 왼손 메뉴 제스처(손바닥을 보며 엄지 · 검지 집기) · 컨트롤러 ☰ 버튼 · Esc → 계속 / 기권 (BattleMenuScreen)

using SpellboundVR.Backend;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SpellboundVR.UI
{
    public sealed class MetaFlow : MonoBehaviour
    {
        public static MetaFlow Instance { get; private set; }

        [Tooltip("경기가 끝난 뒤 결과 화면(C13)을 띄우기까지 (초) — 그동안 HUD의 VICTORY/DEFEAT가 보인다")]
        public float resultDelay = 2.5f;

        private GameBootstrap _boot;
        private MetaUiRoot _ui;
        private NetworkGameEventPublisher _events;
        private MatchResultReporter _reporter;
        private bool _battleStarted;
        private float _resultAt = -1f;
        private float _battleStartedAt;
        private bool _gotPhase;
        private bool _online;
        private bool _menuOpen;
        private bool _forfeited;

        [Tooltip("PC 게임 서버 경기에서 이 시간(초) 안에 서버 응답이 없으면 로비로 돌아간다")]
        public float onlineConnectTimeout = 25f;
        private readonly int[] _lastHp = { -1, -1 };

        public MetaUiRoot Ui => _ui;
        public bool BattleStarted => _battleStarted;
        /// <summary>지금 경기 단계 (C11 카운트다운 표시용)</summary>
        public MatchPhase Phase { get; private set; } = MatchPhase.WaitingForPlayers;
        public float PhaseTimeLeft { get; private set; }
        /// <summary>PC 게임 서버 경기인지 (C11 안내 문구용)</summary>
        public bool OnlineBattle => _online;
        /// <summary>PC 게임 서버 접속 상황 한 줄 (C11 대기 화면용). 없으면 빈 문자열</summary>
        public string OnlineStatus
        {
            get
            {
                if (!_online) return "";
                var c = FindAnyObjectByType<ClientConnector>();
                if (c == null) return "PC 게임 서버에 접속하는 중…";
                if (c.IsConnected) return "서버에 접속했습니다. 상대를 기다리는 중… (없으면 봇)";
                string fail = string.IsNullOrEmpty(c.LastFailure) ? "" : c.LastFailure + "\n";
                return fail + $"PC 게임 서버에 접속하는 중… (시도 {c.Attempts}/{c.MaxAttempts})";
            }
        }
        public bool MenuOpen => _menuOpen;

        public void Initialize(GameBootstrap boot, bool vr, Transform head, Transform rightHandAnchor)
        {
            Instance = this;
            _boot = boot;
            _ui = gameObject.AddComponent<MetaUiRoot>();
            _ui.Initialize(vr, head, rightHandAnchor);
            _ui.Register(MetaScreenId.Login, new LoginScreen(false));
            _ui.Register(MetaScreenId.Signup, new LoginScreen(true));
            _ui.Register(MetaScreenId.Lobby, new LobbyScreen());
            _ui.Register(MetaScreenId.MyPage, new MyPageScreen());
            _ui.Register(MetaScreenId.Settings, new SettingsScreen());
            _ui.Register(MetaScreenId.Friends, new FriendsScreen());
            _ui.Register(MetaScreenId.Room, new FriendlyRoomScreen());
            _ui.Register(MetaScreenId.Mailbox, new MailboxScreen());
            _ui.Register(MetaScreenId.Ranking, new RankingScreen());
            _ui.Register(MetaScreenId.Spellbook, new DeckBuilderScreen());
            _ui.Register(MetaScreenId.Queue, new MatchQueueScreen());
            _ui.Register(MetaScreenId.Countdown, new CountdownScreen());
            _ui.Register(MetaScreenId.Result, new ResultScreen());
            _ui.Register(MetaScreenId.Tutorial, new TutorialScreen());
            _ui.Register(MetaScreenId.BattleMenu, new BattleMenuScreen());
            MetaState.ApplySettings();
            BackendClient.OnUnauthorized += HandleUnauthorized;
            Route();
        }

        private void OnDestroy()
        {
            BackendClient.OnUnauthorized -= HandleUnauthorized;
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        /// <summary>처음 열 화면: 경기 뒤 돌아온 화면 → 로그인 상태면 로비 → 저장된 토큰 자동 로그인 → 로그인 화면</summary>
        private void Route()
        {
            var pending = MetaState.PendingScreen;
            MetaState.PendingScreen = null;
            if (MetaState.LoggedIn)
            {
                if (MetaState.Profile == null && !MetaState.Offline) MetaState.LoadPlayer(null);
                _ui.Go(pending ?? MetaScreenId.Lobby);
                if (!string.IsNullOrEmpty(MetaState.PendingToast))
                {
                    MetaUiRoot.Toast(MetaState.PendingToast, Tone.Enemy);
                    MetaState.PendingToast = null;
                }
                return;
            }
            if (BackendClient.Instance.HasToken)
            {
                _ui.Go(MetaScreenId.Login);
                AuthService.RestoreSession(
                    _ => MetaState.LoadPlayer(() => { if (_ui.CurrentId == MetaScreenId.Login) _ui.Go(MetaScreenId.Lobby); }),
                    _ => { });
                return;
            }
            _ui.Go(MetaScreenId.Login);
        }

        private void HandleUnauthorized()
        {
            if (MetaState.Offline || _battleStarted) return;
            MetaState.SignOut();
            MetaUiRoot.Toast("로그인이 만료되었습니다. 다시 로그인해 주세요.", Tone.Enemy);
            _ui.Go(MetaScreenId.Login);
        }

        /// <summary>서버 없이 로컬 덱으로 시작 (C01 "오프라인으로 시작")</summary>
        public void StartOffline()
        {
            MetaState.StartOffline(_boot.localDeck);
            _ui.Go(MetaScreenId.Lobby);
        }

        // ── 전투 ─────────────────────────────────────────────

        /// <summary>C11 카운트다운이 열릴 때 — 실제 경기를 시작한다</summary>
        public void BeginBattle()
        {
            if (_battleStarted) return;
            _battleStarted = true;
            _battleStartedAt = Time.unscaledTime;
            _gotPhase = false;
            _forfeited = false;
            MetaState.LastMatch = null;
            var ctx = MetaState.Match ?? new MatchContext { Mode = MatchResultService.ModeBot, OpponentName = "Bot", OpponentBot = true };
            bool skillTest = string.IsNullOrEmpty(ctx.Mode);
            bool pvpMode = ctx.Mode == MatchResultService.ModeRanked || ctx.Mode == MatchResultService.ModeFriendly;
            _online = pvpMode && _boot.ResolvedMode == BootMode.FusionClient;
            if (!pvpMode) BackendSessionService.Existing?.ClearMatch(); // 앞 경기의 매칭 정보가 봇전 기록에 섞이지 않게

            _reporter = FindAnyObjectByType<MatchResultReporter>();
            if (_reporter == null) _reporter = new GameObject("[MatchResultReporter]").AddComponent<MatchResultReporter>();
            _reporter.FallbackMode = skillTest ? "" : MatchResultService.ModeBot; // 스킬 테스트는 전적에 남기지 않는다
            _reporter.OnResultSaved += HandleSaved;
            _reporter.OnRatingChanged += HandleRating;

            var ids = DeckService.ToSpellIds(MetaState.Deck.ToArray());
            var deck = new int[Deck.DeckData.SlotCount];
            for (int i = 0; i < deck.Length && i < ids.Length; i++) deck[i] = ids[i];
            _boot.BeginBattle(deck, _online, skillTest && _boot.recognizeAllRunes);
            MetaState.ApplySettings(); // 전투용 비네팅 등은 BeginBattle에서 생긴다

            _events = _boot.Session != null ? _boot.Session.Events : null;
            if (_events != null)
            {
                _events.OnPhaseInfo += HandlePhase;
                _events.OnMatchEnded += HandleMatchEnded;
                _events.OnLocalCastAccepted += HandleCast;
                _events.OnHpChanged += HandleHp;
            }
        }

        private void Unsubscribe()
        {
            if (_reporter != null)
            {
                _reporter.OnResultSaved -= HandleSaved;
                _reporter.OnRatingChanged -= HandleRating;
            }
            if (_events == null) return;
            _events.OnPhaseInfo -= HandlePhase;
            _events.OnMatchEnded -= HandleMatchEnded;
            _events.OnLocalCastAccepted -= HandleCast;
            _events.OnHpChanged -= HandleHp;
            _events = null;
        }

        private int _casts;
        private int _damageDealt, _damageTaken;

        private void HandlePhase(MatchPhase phase, float timeLeft)
        {
            Phase = phase;
            PhaseTimeLeft = timeLeft;
            _gotPhase = true;
            if (phase == MatchPhase.Playing && _ui.CurrentId == MetaScreenId.Countdown && _ui.Visible)
            {
                _casts = 0;
                _damageDealt = _damageTaken = 0;
                _lastHp[0] = _lastHp[1] = -1;
                _ui.SetVisible(false); // C12 전투 HUD만 남긴다
            }
        }

        private void HandleCast(int slot, int spellId) => _casts++;

        private void HandleHp(Team team, int current, int max)
        {
            int i = (int)team;
            if (i < 0 || i > 1) return;
            int prev = _lastHp[i];
            _lastHp[i] = current;
            if (prev < 0 || current >= prev || Phase != MatchPhase.Playing) return;
            if (_events != null && team == _events.LocalTeam) _damageTaken += prev - current;
            else _damageDealt += prev - current;
        }

        private void HandleMatchEnded(MatchResult result, MatchEndReason reason, float duration)
        {
            if (_resultAt > 0f || MetaState.LastMatch != null) return; // 한 경기에 한 번 (기권 직후 서버 결과가 또 와도)
            if (_menuOpen)
            {
                _menuOpen = false;
                _ui.SetVisible(false);
            }
            var ctx = MetaState.Match ?? new MatchContext();
            bool skillTest = string.IsNullOrEmpty(ctx.Mode);
            MetaState.LastMatch = new MatchRecord
            {
                Result = result,
                Reason = _forfeited && result == MatchResult.Lose ? "경기 중 기권했습니다." : ReasonText(result, reason),
                DurationSec = duration,
                Casts = _casts,
                DamageDealt = _damageDealt,
                DamageTaken = _damageTaken,
                Mode = ctx.Mode,
                OpponentName = ctx.OpponentName,
                Saved = skillTest || MetaState.Offline || !AuthService.IsLoggedIn ? false : (bool?)null,
                SaveMessage = skillTest ? "스킬 테스트는 전적에 기록하지 않습니다."
                            : MetaState.Offline ? "오프라인 경기는 전적에 저장되지 않습니다."
                            : !AuthService.IsLoggedIn ? "로그인하지 않아 전적을 저장하지 않았습니다." : "",
            };
            _resultAt = Time.unscaledTime + resultDelay;
        }

        private void HandleSaved(bool ok, string message)
        {
            var m = MetaState.LastMatch;
            if (m == null) return;
            m.Saved = ok;
            m.SaveMessage = message;
            MetaState.RefreshProfile(() => MetaState.Notify("lastMatch"));
            MetaState.Notify("lastMatch");
        }

        private void HandleRating(int delta, int newRating)
        {
            var m = MetaState.LastMatch;
            if (m == null) return;
            m.RatingDelta = delta;
            m.NewRating = newRating;
            MetaState.Notify("lastMatch");
        }

        private void Update()
        {
            if (_resultAt > 0f && Time.unscaledTime >= _resultAt)
            {
                _resultAt = -1f;
                _ui.Go(MetaScreenId.Result);
            }
            if (!_battleStarted) return;

            // PC 게임 서버에 붙지 못함 (서버가 꺼져 있거나 다른 네트워크)
            if (_online && !_gotPhase && Time.unscaledTime - _battleStartedAt > onlineConnectTimeout)
            {
                _online = false;
                LeaveBattle("PC 게임 서버에 연결하지 못했습니다. 서버(RunServer.bat)가 켜져 있는지 확인하세요.");
                return;
            }

            // 경기 중 메뉴 (왼손 메뉴 제스처 · 컨트롤러 ☰ · Esc)
            if (Phase == MatchPhase.Playing && _resultAt < 0f && MetaState.LastMatch == null && MenuPressed())
            {
                if (_menuOpen) CloseBattleMenu();
                else OpenBattleMenu();
            }
        }

        private static bool MenuPressed()
        {
            if (KeyInput.Down(Key.Escape)) return true;
            try { return OVRInput.GetDown(OVRInput.Button.Start); }
            catch (System.Exception) { return false; }
        }

        // ── 경기 중 메뉴 · 기권 · 나가기 ─────────────────────────

        public void OpenBattleMenu()
        {
            if (_menuOpen || !_battleStarted || Phase != MatchPhase.Playing) return;
            _menuOpen = true;
            _boot.SetBattleInputEnabled(false);
            _ui.Go(MetaScreenId.BattleMenu);
        }

        public void CloseBattleMenu()
        {
            if (!_menuOpen) return;
            _menuOpen = false;
            _ui.SetVisible(false);
            _boot.SetBattleInputEnabled(true);
        }

        /// <summary>기권 — 패배로 기록되고 결과 화면(C13)으로 간다</summary>
        public void Forfeit()
        {
            _forfeited = true;
            _menuOpen = false;
            _ui.SetVisible(false);
            if (!_boot.Forfeit()) LeaveBattle(null); // 아직 시작 전이면 그냥 로비로
        }

        /// <summary>경기가 시작되기 전에 나가기 (C11 대기 중 취소 · 접속 실패)</summary>
        public void LeaveBattle(string message)
        {
            if (!string.IsNullOrEmpty(message)) MetaState.PendingToast = message;
            var connector = FindAnyObjectByType<ClientConnector>();
            if (connector != null) connector.Leave();
            ReturnTo(MetaScreenId.Lobby);
        }

        /// <summary>게임 종료 (확인 창)</summary>
        public void QuitGame()
        {
            _ui.Confirm("게임 종료", "War of Wizards를 종료할까요?", "종료", true, ok =>
            {
                if (!ok) return;
                Debug.Log("[MetaFlow] 게임 종료");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
        }

        private static string ReasonText(MatchResult result, MatchEndReason reason)
        {
            switch (reason)
            {
                case MatchEndReason.PlayerDeath: return result == MatchResult.Win ? "상대 마법사를 쓰러뜨렸습니다." : "마법사가 쓰러졌습니다.";
                case MatchEndReason.Afk: return result == MatchResult.Win ? "상대가 오래 움직이지 않아 기권했습니다." : "오래 움직이지 않아 기권 처리되었습니다. (AFK)";
                case MatchEndReason.Disconnect: return result == MatchResult.Win ? "상대의 연결이 끊겼습니다." : "연결이 끊겼습니다.";
                case MatchEndReason.Draw: return "시간이 다 되어 무승부입니다.";
                default: return "무효 경기입니다. 전적에 기록되지 않습니다.";
            }
        }

        /// <summary>전투가 끝난 뒤 다른 화면으로: 씬을 다시 불러와 전투를 깨끗이 지운다</summary>
        public void ReturnTo(MetaScreenId screen)
        {
            if (!_battleStarted)
            {
                _ui.Go(screen);
                return;
            }
            MetaState.PendingScreen = screen;
            Unsubscribe();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>대전 시작 (C02 봇 연습 · C06 친선전 · C10 매칭 완료 → C11)</summary>
        public void StartMatch(MatchContext ctx)
        {
            MetaState.Match = ctx;
            if (_battleStarted) ReturnTo(MetaScreenId.Countdown); // 한 씬에서 한 경기 — 새 경기는 새 씬에서
            else _ui.Go(MetaScreenId.Countdown);
        }
    }
}
