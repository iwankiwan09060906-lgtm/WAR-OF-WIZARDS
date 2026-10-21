// §22 세션 흐름(매칭 · 친선전)의 서버 쪽 기록 — 계약서 ISessionService(§25.1) 구현.
// 개발 중 대역은 Contracts/Mocks/MockSessionService. 이 구현은 Backend/wow_server.py 의 큐 · 친선전 방 API 를 쓴다.
//
// - 매칭 큐: 등록 → pollInterval 마다 상태 조회 → 레이팅이 맞는 사람과 매칭, 서버 설정 matchmaking.bot_timeout(기본 15초) 초과 시 봇.
//   매칭되면 서버가 게임 방(matchId)을 만들고, MatchResultReporter 가 경기 시작/결과를 그 방에 보고한다.
// - 친선전: CreateFriendlyRoom 은 6자리 코드를 바로 돌려준다 (클라이언트가 만든 코드를 서버가 받아 씀, 겹치면 OnRoomCodeChanged).
//   상대가 없으면 방장이 FillWithBot, 준비가 끝나면 방장이 StartFriendlyMatch. 친선전 결과는 레이팅에 반영되지 않는다(§22.2).
// - 실제 Photon Fusion 세션 생성 · 입장은 이 서비스 밖(Network/Session)의 일이다. 이 서비스의 이벤트를 받아 시작하면 된다.
// - 서버에 연결할 수 없으면(botFallbackWhenOffline) 오프라인 봇 매칭으로 대신해 시연이 멈추지 않게 한다.

using System;
using System.Collections;
using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Backend
{
    public sealed class BackendSessionService : MonoBehaviour, ISessionService
    {
        public enum SessionKind { None, Matchmaking, FriendlyRoom }

        private static BackendSessionService _instance;

        public static BackendSessionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<BackendSessionService>();
                    if (_instance == null)
                    {
                        _instance = new GameObject("[BackendSessionService]").AddComponent<BackendSessionService>();
                    }
                }
                return _instance;
            }
        }

        /// <summary>씬에 이미 있을 때만 (없으면 만들지 않는다)</summary>
        public static BackendSessionService Existing => _instance != null ? _instance : FindAnyObjectByType<BackendSessionService>();

        [Tooltip("큐 상태 · 방 상태 조회 간격(초)")]
        [SerializeField] private float pollInterval = 1f;

        [Tooltip("서버에 연결할 수 없을 때 오프라인 봇 매칭으로 대신한다")]
        [SerializeField] private bool botFallbackWhenOffline = true;
        [SerializeField] private float offlineBotDelay = 3f;

        private const string RoomCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private Coroutine _poll;
        private bool _friendlyStarted;

        // ── 상태 ──────────────────────────────────────────────

        public SessionKind Kind { get; private set; }
        public string RoomCode { get; private set; } = string.Empty;
        public bool IsHost { get; private set; }
        public bool IsReady { get; private set; }
        public RoomDto Room { get; private set; }

        /// <summary>매칭 큐로 잡힌 서버 게임 방 번호 (없으면 0 — 봇 연습 · 친선전 · 오프라인)</summary>
        public long CurrentMatchId { get; private set; }

        /// <summary>결과 저장 모드: ranked · friendly · bot (없으면 빈 문자열)</summary>
        public string CurrentMode { get; private set; } = string.Empty;

        public string OpponentNickname { get; private set; } = string.Empty;
        public bool OpponentIsBot { get; private set; }

        // ── 이벤트 ────────────────────────────────────────────

        /// <summary>큐 대기 중 (경과 시간 · 레이팅 범위 · 봇 대체까지 남은 시간 표시용, C10)</summary>
        public event Action<MatchmakingStatusDto> OnMatchmakingProgress;

        /// <summary>매칭 완료 (bot = 봇 상대). 여기서 경기 세션을 시작한다.</summary>
        public event Action<MatchmakingStatusDto> OnMatchFound;

        /// <summary>친선전 방 상태가 바뀜 (게스트 입장 · 준비 · 퇴장)</summary>
        public event Action<RoomDto> OnRoomChanged;

        /// <summary>서버가 다른 방 코드를 정했을 때 (제안한 코드가 이미 쓰이는 중)</summary>
        public event Action<string> OnRoomCodeChanged;

        /// <summary>방장이 봇으로 빈 자리를 채움</summary>
        public event Action OnBotFilled;

        /// <summary>친선전 시작 (opponentNickname, isBot) — 방장이 시작하면 양쪽 모두에게 온다</summary>
        public event Action<string, bool> OnFriendlyMatchStarted;

        /// <summary>세션이 끝남 (이유: 나가기 · 강퇴 · 방 닫힘 · 오류)</summary>
        public event Action<string> OnSessionEnded;

        public event Action<string> OnSessionError;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ── ISessionService: 매칭 큐 (§22.1) ─────────────────

        public void RequestMatchmaking()
        {
            ResetSession(null);
            Kind = SessionKind.Matchmaking;
            BackendClient.Instance.PostEmpty<MessageDto>("/api/matchmaking/queue",
                _ => StartPoll(PollMatchmaking()),
                err =>
                {
                    if (botFallbackWhenOffline && err == BackendClient.ConnectionErrorMessage) StartPoll(OfflineBotMatch());
                    else Fail(err);
                });
        }

        public void CancelMatchmaking()
        {
            if (Kind != SessionKind.Matchmaking) return;
            StopPoll();
            BackendClient.Instance.Delete<MessageDto>("/api/matchmaking/queue", null);
            ResetSession("매칭 취소");
        }

        private IEnumerator PollMatchmaking()
        {
            var wait = new WaitForSecondsRealtime(pollInterval);
            while (Kind == SessionKind.Matchmaking)
            {
                bool done = false;
                MatchmakingStatusDto status = null;
                string error = null;
                BackendClient.Instance.Get<MatchmakingStatusDto>("/api/matchmaking/status",
                    s => { status = s; done = true; }, e => { error = e; done = true; });
                while (!done) yield return null;

                if (Kind != SessionKind.Matchmaking) yield break;
                if (error != null)
                {
                    Fail(error);
                    yield break;
                }
                if (status.IsMatched)
                {
                    _poll = null;
                    CompleteMatch(status, MatchResultService.ModeRanked);
                    yield break;
                }
                if (!status.IsSearching)
                {
                    Fail("매칭 대기열에서 빠졌습니다. 다시 시도하세요.");
                    yield break;
                }
                OnMatchmakingProgress?.Invoke(status);
                yield return wait;
            }
        }

        private IEnumerator OfflineBotMatch()
        {
            Debug.LogWarning("[BackendSessionService] 서버 오프라인: 오프라인 봇 매칭으로 대체합니다.");
            yield return new WaitForSecondsRealtime(offlineBotDelay);
            _poll = null;
            if (Kind != SessionKind.Matchmaking) yield break;
            // 오프라인 경기는 서버 게임 방이 없으므로 결과는 저장하지 않는다 (CurrentMode 빈 문자열)
            var status = new MatchmakingStatusDto { state = "matched", bot = true, opponentNickname = "Bot (오프라인)" };
            CompleteMatch(status, string.Empty);
        }

        private void CompleteMatch(MatchmakingStatusDto status, string mode)
        {
            CurrentMatchId = status.matchId;
            CurrentMode = mode;
            RoomCode = status.roomCode ?? string.Empty;
            OpponentNickname = status.opponentNickname ?? string.Empty;
            OpponentIsBot = status.bot;
            Kind = SessionKind.None; // 큐는 끝났고, 이제 경기 정보(CurrentMatchId 등)만 남는다
            Debug.Log($"[BackendSessionService] 매칭 완료: {OpponentNickname} ({(OpponentIsBot ? "봇" : "사람")}), matchId={CurrentMatchId}");
            OnMatchFound?.Invoke(status);
        }

        // ── ISessionService: 친선전 (§22.2) ──────────────────

        public string CreateFriendlyRoom()
        {
            ResetSession(null);
            Kind = SessionKind.FriendlyRoom;
            IsHost = true;
            IsReady = true;
            RoomCode = NewRoomCode();
            CurrentMode = MatchResultService.ModeFriendly;

            string requested = RoomCode;
            BackendClient.Instance.Post<RoomCreateRequestDto, RoomCodeDto>("/api/rooms", new RoomCreateRequestDto { code = requested },
                res =>
                {
                    if (Kind != SessionKind.FriendlyRoom || RoomCode != requested) return;
                    if (!string.IsNullOrEmpty(res.code) && res.code != requested)
                    {
                        RoomCode = res.code;
                        OnRoomCodeChanged?.Invoke(RoomCode);
                    }
                    StartPoll(PollRoom());
                },
                err =>
                {
                    // 오프라인이면 방은 이 기기에만 있다 (봇 채우기 · 시작은 가능, 결과 저장은 안 됨)
                    if (botFallbackWhenOffline && err == BackendClient.ConnectionErrorMessage) CurrentMode = string.Empty;
                    else Fail(err);
                });
            return RoomCode;
        }

        public void JoinFriendlyRoom(string roomCode)
        {
            ResetSession(null);
            string code = (roomCode ?? string.Empty).Trim().ToUpperInvariant();
            Kind = SessionKind.FriendlyRoom;
            IsHost = false;
            RoomCode = code;
            CurrentMode = MatchResultService.ModeFriendly;
            BackendClient.Instance.PostEmpty<RoomCodeDto>($"/api/rooms/{Uri.EscapeDataString(code)}/join",
                _ =>
                {
                    if (Kind == SessionKind.FriendlyRoom && RoomCode == code) StartPoll(PollRoom());
                },
                Fail);
        }

        public void FillWithBot()
        {
            if (Kind != SessionKind.FriendlyRoom || !IsHost)
            {
                Debug.LogWarning("[BackendSessionService] 봇 채우기는 친선전 방장만 할 수 있습니다.");
                return;
            }
            if (Room != null && Room.HasGuest)
            {
                Debug.LogWarning("[BackendSessionService] 이미 상대가 있어 봇을 채우지 않습니다.");
                return;
            }
            OpponentIsBot = true;
            OpponentNickname = "Bot";
            OnBotFilled?.Invoke();
        }

        /// <summary>게스트: 준비 토글. 방장은 항상 준비 상태이며 StartFriendlyMatch 로 시작한다.</summary>
        public void SetReady(bool isReady)
        {
            if (Kind != SessionKind.FriendlyRoom) return;
            IsReady = isReady || IsHost;
            if (IsHost) return;
            BackendClient.Instance.PostEmpty<ReadyDto>($"/api/rooms/{Uri.EscapeDataString(RoomCode)}/ready?ready={(isReady ? "true" : "false")}",
                null, err => OnSessionError?.Invoke(err));
        }

        /// <summary>방장: 친선전 시작 (게스트가 준비했거나 봇을 채운 뒤). 계약서 밖의 추가 메서드.</summary>
        public void StartFriendlyMatch()
        {
            if (Kind != SessionKind.FriendlyRoom || !IsHost) return;
            bool guestReady = Room != null && Room.HasGuest && Room.GuestReady;
            if (!guestReady && !OpponentIsBot)
            {
                OnSessionError?.Invoke("상대가 준비하지 않았습니다. 기다리거나 봇으로 채우세요.");
                return;
            }
            if (string.IsNullOrEmpty(CurrentMode))
            {
                RaiseFriendlyStarted(); // 오프라인 방
                return;
            }
            BackendClient.Instance.PostEmpty<MessageDto>($"/api/rooms/{Uri.EscapeDataString(RoomCode)}/start",
                _ => RaiseFriendlyStarted(), err => OnSessionError?.Invoke(err));
        }

        /// <summary>방장: 게스트 내보내기. 계약서 밖의 추가 메서드.</summary>
        public void KickGuest()
        {
            if (Kind != SessionKind.FriendlyRoom || !IsHost) return;
            BackendClient.Instance.PostEmpty<MessageDto>($"/api/rooms/{Uri.EscapeDataString(RoomCode)}/kick", null, err => OnSessionError?.Invoke(err));
        }

        private IEnumerator PollRoom()
        {
            var wait = new WaitForSecondsRealtime(pollInterval);
            string code = RoomCode;
            while (Kind == SessionKind.FriendlyRoom && RoomCode == code)
            {
                bool done = false;
                RoomDto room = null;
                string error = null;
                BackendClient.Instance.Get<RoomDto>($"/api/rooms/{Uri.EscapeDataString(code)}",
                    r => { room = r; done = true; }, e => { error = e; done = true; });
                while (!done) yield return null;

                if (Kind != SessionKind.FriendlyRoom || RoomCode != code) yield break;
                if (error != null)
                {
                    if (error == BackendClient.ConnectionErrorMessage) { yield return wait; continue; } // 잠깐 끊김은 다시 시도
                    Fail(error);
                    yield break;
                }

                Room = room;
                if (IsHost)
                {
                    if (room.HasGuest)
                    {
                        OpponentIsBot = false;
                        OpponentNickname = room.guestNickname ?? string.Empty;
                    }
                    else if (!OpponentIsBot) OpponentNickname = string.Empty;
                }
                else
                {
                    OpponentNickname = room.hostNickname ?? string.Empty;
                    OpponentIsBot = false;
                    if (room.status == "CLOSED") { _poll = null; ResetSession("방장이 방을 닫았습니다."); yield break; }
                    if (room.guest_player_id != AuthService.PlayerId && AuthService.PlayerId != 0)
                    {
                        _poll = null;
                        ResetSession("방에서 내보내졌습니다.");
                        yield break;
                    }
                }
                OnRoomChanged?.Invoke(room);

                if (room.status == "PLAYING" && !_friendlyStarted) RaiseFriendlyStarted();
                yield return wait;
            }
        }

        private void RaiseFriendlyStarted()
        {
            if (_friendlyStarted) return;
            _friendlyStarted = true;
            StopPoll();
            Debug.Log($"[BackendSessionService] 친선전 시작: 방 {RoomCode}, 상대 {OpponentNickname} ({(OpponentIsBot ? "봇" : "사람")})");
            OnFriendlyMatchStarted?.Invoke(OpponentNickname, OpponentIsBot);
        }

        // ── ISessionService: 나가기 ──────────────────────────

        public void LeaveSession()
        {
            StopPoll();
            if (Kind == SessionKind.Matchmaking)
            {
                BackendClient.Instance.Delete<MessageDto>("/api/matchmaking/queue", null);
            }
            if (Kind == SessionKind.FriendlyRoom && !string.IsNullOrEmpty(CurrentMode))
            {
                BackendClient.Instance.PostEmpty<MessageDto>($"/api/rooms/{Uri.EscapeDataString(RoomCode)}/leave", null);
            }
            if (CurrentMatchId > 0)
            {
                MatchResultService.Leave(CurrentMatchId); // 이미 결과를 보고한 방은 서버가 그대로 둔다
            }
            ResetSession("나가기");
        }

        /// <summary>경기 결과 저장이 끝난 뒤 MatchResultReporter 가 부른다 (다음 경기를 위해 정보 비우기)</summary>
        public void ClearMatch()
        {
            if (Kind == SessionKind.FriendlyRoom && _friendlyStarted && IsHost && !string.IsNullOrEmpty(CurrentMode))
            {
                BackendClient.Instance.PostEmpty<MessageDto>($"/api/rooms/{Uri.EscapeDataString(RoomCode)}/leave", null);
            }
            ResetSession(null);
        }

        // ── 내부 ──────────────────────────────────────────────

        private void StartPoll(IEnumerator routine)
        {
            StopPoll();
            _poll = StartCoroutine(routine);
        }

        private void StopPoll()
        {
            if (_poll != null) StopCoroutine(_poll);
            _poll = null;
        }

        private void Fail(string error)
        {
            StopPoll();
            Debug.LogWarning($"[BackendSessionService] {error}");
            OnSessionError?.Invoke(error);
            ResetSession(error);
        }

        private void ResetSession(string endReason)
        {
            StopPoll();
            bool hadSession = Kind != SessionKind.None || CurrentMatchId > 0 || _friendlyStarted;
            Kind = SessionKind.None;
            RoomCode = string.Empty;
            IsHost = false;
            IsReady = false;
            Room = null;
            CurrentMatchId = 0;
            CurrentMode = string.Empty;
            OpponentNickname = string.Empty;
            OpponentIsBot = false;
            _friendlyStarted = false;
            if (hadSession && endReason != null) OnSessionEnded?.Invoke(endReason);
        }

        private static string NewRoomCode()
        {
            var chars = new char[6];
            for (int i = 0; i < chars.Length; i++) chars[i] = RoomCodeChars[UnityEngine.Random.Range(0, RoomCodeChars.Length)];
            return new string(chars);
        }
    }
}
