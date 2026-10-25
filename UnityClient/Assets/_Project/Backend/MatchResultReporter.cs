// §23.4 전적 저장 흐름 — "VR 클라이언트: 결과 이벤트 수신 → 결과 화면 → 서버 저장"
//
// 게임 코드를 고치지 않고 붙이는 컴포넌트. 씬의 GameBootstrap 이 만든 ClientSession.Events(IGameEvents)를 구독해서
//   OnMatchStarted → (매칭 게임 방이면) MatchResultService.Join
//   OnMatchEnded   → 매칭 게임 방이면 ReportResult(레이팅 ±), 아니면 Record(봇 연습 · 친선전)
// 를 자동으로 부른다. 로그인하지 않았거나 전용 서버 빌드(DedicatedServer)에서는 아무것도 하지 않는다.
//
// 사용법: 게임 씬의 빈 GameObject 에 붙인다 (GameBootstrap 은 비워 두면 자동으로 찾는다).

using System;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using UnityEngine;

namespace SpellboundVR.Backend
{
    public sealed class MatchResultReporter : MonoBehaviour
    {
        [SerializeField] private GameBootstrap bootstrap;

        [Tooltip("시작할 때 저장된 토큰으로 자동 로그인 (AuthService.RestoreSession)")]
        [SerializeField] private bool restoreLoginOnStart = true;

        [Tooltip("매칭 · 친선전 세션 없이 시작한 경기(로컬 봇전)를 저장할 모드. 비우면 저장하지 않는다.")]
        [SerializeField] private string fallbackMode = MatchResultService.ModeBot;

        /// <summary>세션 없이 시작한 경기를 저장할 모드 ("" = 저장 안 함 — 스킬 테스트)</summary>
        public string FallbackMode { get => fallbackMode; set => fallbackMode = value; }

        /// <summary>저장 결과 (성공 여부, 안내 문구) — 결과 화면(C13)에서 레이팅 변화를 보여줄 때</summary>
        public event Action<bool, string> OnResultSaved;

        /// <summary>랭킹전 결과가 저장됨 (레이팅 변화, 새 레이팅) — C13</summary>
        public event Action<int, int> OnRatingChanged;

        private NetworkGameEventPublisher _events;
        private Team _localTeam = Team.Home;
        private bool _inMatch;
        private readonly int[] _lastHp = { -1, -1 };
        private float _damageDealt;
        private float _damageTaken;

        /// <summary>씬에 직접 붙이지 않아도, GameBootstrap 이 있는 씬이 열리면 자동으로 하나 만든다</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<GameBootstrap>() == null || FindAnyObjectByType<MatchResultReporter>() != null) return;
            new GameObject("[MatchResultReporter]").AddComponent<MatchResultReporter>();
        }

        private void Start()
        {
            if (restoreLoginOnStart && !AuthService.IsLoggedIn && BackendClient.Instance.HasToken)
            {
                AuthService.RestoreSession(
                    user => Debug.Log($"[MatchResultReporter] 자동 로그인: {user.nickname}"),
                    err => Debug.LogWarning($"[MatchResultReporter] 자동 로그인 실패: {err}"));
            }
        }

        private void Update()
        {
            if (_events != null) return;
            if (bootstrap == null) bootstrap = FindAnyObjectByType<GameBootstrap>();
            if (bootstrap == null) return;
            if (bootstrap.ResolvedMode == BootMode.DedicatedServer)
            {
                enabled = false; // 결과 저장은 VR 클라이언트 몫 (§23.4)
                return;
            }
            var publisher = bootstrap.Session?.Events;
            if (publisher == null) return;

            _events = publisher;
            _localTeam = publisher.LocalTeam;
            _events.OnLocalTeamAssigned += HandleLocalTeam;
            _events.OnMatchStarted += HandleMatchStarted;
            _events.OnHpChanged += HandleHpChanged;
            _events.OnMatchEnded += HandleMatchEnded;
        }

        private void OnDestroy()
        {
            if (_events == null) return;
            _events.OnLocalTeamAssigned -= HandleLocalTeam;
            _events.OnMatchStarted -= HandleMatchStarted;
            _events.OnHpChanged -= HandleHpChanged;
            _events.OnMatchEnded -= HandleMatchEnded;
        }

        private void HandleLocalTeam(Team team) => _localTeam = team;

        private void HandleMatchStarted(OpponentType opponentType)
        {
            _inMatch = true;
            _lastHp[0] = _lastHp[1] = -1;
            _damageDealt = _damageTaken = 0f;

            var session = BackendSessionService.Existing;
            if (AuthService.IsLoggedIn && session != null && session.CurrentMatchId > 0)
            {
                MatchResultService.Join(session.CurrentMatchId, null,
                    err => Debug.LogWarning($"[MatchResultReporter] 게임 방 참가 알림 실패: {err}"));
            }
        }

        private void HandleHpChanged(Team team, int current, int max)
        {
            int i = (int)team;
            if (i < 0 || i >= _lastHp.Length) return;
            int previous = _lastHp[i];
            _lastHp[i] = current;
            if (!_inMatch || previous < 0 || current >= previous) return;

            if (team == _localTeam) _damageTaken += previous - current;
            else _damageDealt += previous - current;
        }

        private void HandleMatchEnded(MatchResult result, MatchEndReason reason, float duration)
        {
            if (!_inMatch) return;
            _inMatch = false;

            var session = BackendSessionService.Existing;
            long matchId = session != null ? session.CurrentMatchId : 0;
            string serverResult = MatchResultService.ToServerResult(result);

            if (!AuthService.IsLoggedIn)
            {
                Debug.Log("[MatchResultReporter] 로그인하지 않아 전적을 저장하지 않습니다.");
                return;
            }
            if (serverResult == null)
            {
                // 무효 경기(§10): 기록하지 않고 게임 방만 정리
                if (matchId > 0) MatchResultService.Leave(matchId);
                session?.ClearMatch();
                return;
            }

            if (matchId > 0)
            {
                var req = new MatchResultRequestDto
                {
                    result = serverResult,
                    durationSec = duration,
                    damageDealt = _damageDealt,
                    damageTaken = _damageTaken,
                };
                MatchResultService.ReportResult(matchId, req,
                    match =>
                    {
                        int newRating = match.me != null ? match.me.rating : 0;
                        OnRatingChanged?.Invoke(match.ratingDelta, newRating);
                        Saved($"전적 저장 완료 (레이팅 {match.ratingDelta:+0;-0;0} → {newRating})");
                    },
                    err => Saved(null, err));
            }
            else
            {
                string mode = session != null && !string.IsNullOrEmpty(session.CurrentMode) ? session.CurrentMode : fallbackMode;
                if (string.IsNullOrEmpty(mode))
                {
                    session?.ClearMatch();
                    return;
                }
                string opponent = session != null && !string.IsNullOrEmpty(session.OpponentNickname) ? session.OpponentNickname : "Bot";
                var record = new MatchRecordRequestDto
                {
                    mode = mode,
                    result = serverResult,
                    opponent = opponent,
                    durationSec = duration,
                    damageDealt = _damageDealt,
                    damageTaken = _damageTaken,
                };
                MatchResultService.Record(record, _ => Saved("전적 저장 완료"), err => Saved(null, err));
            }
            session?.ClearMatch();
        }

        private void Saved(string message, string error = null)
        {
            if (error == null) Debug.Log($"[MatchResultReporter] {message}");
            else Debug.LogWarning($"[MatchResultReporter] 전적 저장 실패: {error}");
            OnResultSaved?.Invoke(error == null, error ?? message);
        }
    }
}
