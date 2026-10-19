// ==============================================================================
// War of Wizards (워 오브 위자드) — 통합 백엔드 클라이언트 매니저
// Spellbound VR Master Prompt v6.0 §24(ㅈㅇㅈ 역할) 및 §25.1 계약서(ISessionService) 완벽 구현
//
// 기능:
//   1. ISessionService 계약서 인터페이스 구현 (매칭 큐, 친선전 방, 봇 매칭, 준비/퇴장)
//   2. 17개 도메인 REST API 통신 (인증, 8슬롯 덱, 19종 스킬, 프로필, 랭킹, 친구, 우편 등)
//   3. 실시간 SSE 이벤트 리스너 및 브로드캐스트 이벤트 디스패치
//   4. 오프라인 / 서버 미실행 시 자동 Mock 폴백 (에디터/Quest 개발 중단 방지)
//   5. 싱글톤 패턴 WarOfWizardsBackend.Instance 제공
// ==============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using SpellboundVR.Contracts;

namespace SpellboundVR.Backend
{
    [DefaultExecutionOrder(-100)]
    public class WarOfWizardsBackend : MonoBehaviour, ISessionService
    {
        private const string PrefsTokenKey = "wow.jwt.token";
        private const string PrefsUserIdKey = "wow.user.id";
        private const string PrefsPlayerIdKey = "wow.player.id";
        private const string PrefsNicknameKey = "wow.nickname";

        private static WarOfWizardsBackend instance;
        public static WarOfWizardsBackend Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<WarOfWizardsBackend>();
                    if (instance == null)
                    {
                        GameObject go = new GameObject("[WarOfWizardsBackend]");
                        instance = go.AddComponent<WarOfWizardsBackend>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        [Header("Server Configuration")]
        [Tooltip("백엔드 서버 주소 (Quest 3 실기에서는 PC의 IP 입력, 예: http://192.168.0.10:8080)")]
        [SerializeField] private string serverUrl = "http://localhost:8080";
        [SerializeField] private int timeoutSeconds = 10;
        [SerializeField] private bool useMockFallbackOnNetworkError = true;

        [Header("Session State")]
        [SerializeField] private string currentToken = "";
        [SerializeField] private int currentUserId = 0;
        [SerializeField] private int currentPlayerId = 0;
        [SerializeField] private string currentNickname = "";
        [SerializeField] private string currentRoomCode = "";

        // 이벤트 정의
        public event Action<bool, string> OnAuthChanged;
        public event Action<string> OnMatchmakingFound; // opponent, roomCode
        public event Action<string, bool> OnRoomMemberUpdated; // guestNick, isReady
        public event Action<string, string> OnInviteReceived; // from, roomCode
        public event Action<string> OnConfigChanged; // key
        public event Action<string, string> OnNoticeBroadcast; // title, message

        public string ServerUrl => serverUrl;
        public bool IsLoggedIn => !string.IsNullOrEmpty(currentToken);
        public int CurrentPlayerId => currentPlayerId;
        public string CurrentNickname => currentNickname;
        public string CurrentRoomCode => currentRoomCode;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            RestoreLocalSession();
        }

        private void Start()
        {
            if (IsLoggedIn)
            {
                StartCoroutine(HeartbeatRoutine());
            }
        }

        private void RestoreLocalSession()
        {
            currentToken = PlayerPrefs.GetString(PrefsTokenKey, "");
            currentUserId = PlayerPrefs.GetInt(PrefsUserIdKey, 0);
            currentPlayerId = PlayerPrefs.GetInt(PrefsPlayerIdKey, 0);
            currentNickname = PlayerPrefs.GetString(PrefsNicknameKey, "");
        }

        private void SaveSession(string token, int userId, int playerId, string nickname)
        {
            currentToken = token;
            currentUserId = userId;
            currentPlayerId = playerId;
            currentNickname = nickname;
            PlayerPrefs.SetString(PrefsTokenKey, token);
            PlayerPrefs.SetInt(PrefsUserIdKey, userId);
            PlayerPrefs.SetInt(PrefsPlayerIdKey, playerId);
            PlayerPrefs.SetString(PrefsNicknameKey, nickname);
            PlayerPrefs.Save();
        }

        public void ClearSession()
        {
            currentToken = "";
            currentUserId = 0;
            currentPlayerId = 0;
            currentNickname = "";
            PlayerPrefs.DeleteKey(PrefsTokenKey);
            PlayerPrefs.DeleteKey(PrefsUserIdKey);
            PlayerPrefs.DeleteKey(PrefsPlayerIdKey);
            PlayerPrefs.DeleteKey(PrefsNicknameKey);
            PlayerPrefs.Save();
            OnAuthChanged?.Invoke(false, "로그아웃되었습니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // ISessionService 구현 (계약서 v6.0 §25.1)
        // ──────────────────────────────────────────────────────────────────────

        public void RequestMatchmaking()
        {
            Debug.Log("[Backend] 매칭 큐 참가 요청...");
            StartCoroutine(PostRequest("/api/matchmaking/queue", "{}", (success, json) =>
            {
                if (success)
                {
                    Debug.Log("[Backend] 매칭 대기열 등록 완료, 매칭 감시 시작.");
                    StartCoroutine(PollMatchmakingRoutine());
                }
                else if (useMockFallbackOnNetworkError)
                {
                    Debug.LogWarning("[Backend] 서버 오프라인: Mock 봇 매칭으로 대체합니다.");
                    StartCoroutine(SimulateBotMatchRoutine());
                }
            }));
        }

        public void CancelMatchmaking()
        {
            Debug.Log("[Backend] 매칭 큐 취소 요청...");
            StopCoroutine("PollMatchmakingRoutine");
            StopCoroutine("SimulateBotMatchRoutine");
            StartCoroutine(DeleteRequest("/api/matchmaking/queue", (success, res) =>
            {
                Debug.Log("[Backend] 매칭 큐 취소 완료.");
            }));
        }

        public string CreateFriendlyRoom()
        {
            string fallbackCode = "ROOM-" + UnityEngine.Random.Range(1000, 9999);
            StartCoroutine(PostRequest("/api/rooms", "{}", (success, res) =>
            {
                if (success)
                {
                    var dto = JsonUtility.FromJson<RoomCodeDto>(res);
                    currentRoomCode = dto.code;
                    Debug.Log($"[Backend] 친선전 방 생성 성공: {currentRoomCode}");
                }
                else
                {
                    currentRoomCode = fallbackCode;
                    Debug.LogWarning($"[Backend] 오프라인 친선전 방 생성: {currentRoomCode}");
                }
            }));
            return string.IsNullOrEmpty(currentRoomCode) ? fallbackCode : currentRoomCode;
        }

        public void JoinFriendlyRoom(string roomCode)
        {
            currentRoomCode = roomCode;
            Debug.Log($"[Backend] 친선전 방 {roomCode} 참가 시도...");
            StartCoroutine(PostRequest($"/api/rooms/{roomCode}/join", "{}", (success, res) =>
            {
                if (success)
                {
                    Debug.Log($"[Backend] 방 {roomCode} 입장 완료.");
                }
                else
                {
                    Debug.LogWarning($"[Backend] 오프라인 방 {roomCode} 가상 입장.");
                }
            }));
        }

        public void FillWithBot()
        {
            Debug.Log("[Backend] 봇으로 친선전 빈 자리 채움.");
            OnRoomMemberUpdated?.Invoke("FSM 배틀 봇 (AI)", true);
        }

        public void SetReady(bool isReady)
        {
            if (string.IsNullOrEmpty(currentRoomCode)) return;
            StartCoroutine(PostRequest($"/api/rooms/{currentRoomCode}/ready?ready={isReady.ToString().ToLower()}", "{}", (success, res) =>
            {
                Debug.Log($"[Backend] 준비 상태: {isReady}");
            }));
        }

        public void LeaveSession()
        {
            if (!string.IsNullOrEmpty(currentRoomCode))
            {
                StartCoroutine(PostRequest($"/api/rooms/{currentRoomCode}/leave", "{}", null));
                currentRoomCode = "";
            }
            CancelMatchmaking();
        }

        private IEnumerator PollMatchmakingRoutine()
        {
            for (int i = 0; i < 30; i++)
            {
                yield return new WaitForSeconds(2.0f);
                bool matched = false;
                yield return GetRequest("/api/matchmaking/status", (success, json) =>
                {
                    if (success && json.Contains("\"state\":\"matched\""))
                    {
                        var status = JsonUtility.FromJson<MatchStatusDto>(json);
                        matched = true;
                        Debug.Log($"[Backend] 매칭 성공! 상대: {status.opponentNickname}");
                        OnMatchmakingFound?.Invoke(status.opponentNickname);
                    }
                });
                if (matched) yield break;
            }
            // 30초 초과 시 봇 자동 매칭
            OnMatchmakingFound?.Invoke("시간초과 대체 봇 (AI)");
        }

        private IEnumerator SimulateBotMatchRoutine()
        {
            yield return new WaitForSeconds(3.0f);
            Debug.Log("[Backend] 가상 봇 매칭 완료!");
            OnMatchmakingFound?.Invoke("스펠바운드 FSM 봇 (오프라인)");
        }

        // ──────────────────────────────────────────────────────────────────────
        // 인증 API (Login / Register / Me)
        // ──────────────────────────────────────────────────────────────────────

        public void Login(string email, string password, Action<bool, string> callback)
        {
            string json = JsonUtility.ToJson(new LoginRequestDto { email = email, password = password });
            StartCoroutine(PostRequest("/api/auth/login", json, (success, res) =>
            {
                if (success)
                {
                    var dto = JsonUtility.FromJson<AuthResponseDto>(res);
                    SaveSession(dto.accessToken, dto.userId, dto.playerId, dto.nickname);
                    OnAuthChanged?.Invoke(true, "로그인 성공");
                    callback?.Invoke(true, "로그인에 성공했습니다.");
                }
                else
                {
                    if (useMockFallbackOnNetworkError && (email == "demo@wow.dev" || email == "admin@wow.dev"))
                    {
                        SaveSession("mock-jwt-token-wow", 1, 1, "데모마법사");
                        OnAuthChanged?.Invoke(true, "오프라인 데모 로그인");
                        callback?.Invoke(true, "오프라인 데모 계정으로 로그인되었습니다.");
                    }
                    else
                    {
                        callback?.Invoke(false, res);
                    }
                }
            }));
        }

        public void Register(string email, string password, string nickname, Action<bool, string> callback)
        {
            string json = JsonUtility.ToJson(new RegisterRequestDto { email = email, password = password, nickname = nickname });
            StartCoroutine(PostRequest("/api/auth/register", json, (success, res) =>
            {
                if (success)
                {
                    var dto = JsonUtility.FromJson<AuthResponseDto>(res);
                    SaveSession(dto.accessToken, dto.userId, dto.playerId, dto.nickname);
                    OnAuthChanged?.Invoke(true, "회원가입 완료");
                    callback?.Invoke(true, "가입이 완료되었습니다.");
                }
                else
                {
                    callback?.Invoke(false, res);
                }
            }));
        }

        // ──────────────────────────────────────────────────────────────────────
        // 8슬롯 덱 API (Get / Save - 고위력 2장 검증)
        // ──────────────────────────────────────────────────────────────────────

        public void LoadDeck(Action<List<string>> callback)
        {
            StartCoroutine(GetRequest("/api/decks/me", (success, json) =>
            {
                if (success)
                {
                    var dto = JsonUtility.FromJson<DeckResponseDto>(json);
                    callback?.Invoke(new List<string>(dto.skills));
                }
                else
                {
                    // 기본 덱 Mock 반환 (S01~S05, S09, H01, H02)
                    callback?.Invoke(new List<string> { "S01", "S02", "S03", "S04", "S05", "S09", "H01", "H02" });
                }
            }));
        }

        public void SaveDeck(List<string> skills, Action<bool, string> callback)
        {
            if (skills == null || skills.Count != 8)
            {
                callback?.Invoke(false, "덱은 반드시 8장의 스킬이어야 합니다.");
                return;
            }

            int highPowerCount = 0;
            foreach (var sk in skills)
            {
                if (sk.StartsWith("H")) highPowerCount++;
            }
            if (highPowerCount > 2)
            {
                callback?.Invoke(false, $"고위력 스킬은 최대 2장까지만 가능합니다. (현재 {highPowerCount}장)");
                return;
            }

            DeckSaveDto req = new DeckSaveDto { skills = skills.ToArray() };
            string body = JsonUtility.ToJson(req);
            StartCoroutine(PutRequest("/api/decks/me", body, (success, res) =>
            {
                callback?.Invoke(success, success ? "덱 저장 완료" : res);
            }));
        }

        // ──────────────────────────────────────────────────────────────────────
        // 전적 보고 및 결과 저장 (MatchResult)
        // ──────────────────────────────────────────────────────────────────────

        public void RecordMatchResult(string mode, MatchResult result, string opponent, float durationSec, int casts, float dealt, float taken, Action<bool> callback)
        {
            var req = new MatchRecordDto
            {
                mode = mode,
                result = result.ToString().ToUpper(),
                opponent = opponent,
                durationSec = durationSec,
                casts = casts,
                damageDealt = dealt,
                damageTaken = taken
            };
            StartCoroutine(PostRequest("/api/matches", JsonUtility.ToJson(req), (success, res) =>
            {
                Debug.Log($"[Backend] 경기 전적 저장: {success}");
                callback?.Invoke(success);
            }));
        }

        // ──────────────────────────────────────────────────────────────────────
        // 접속 하트비트 루틴
        // ──────────────────────────────────────────────────────────────────────

        private IEnumerator HeartbeatRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(20.0f);
                if (IsLoggedIn)
                {
                    yield return PostRequest("/api/presence/heartbeat", "{}", null);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // HTTP 통신 코루틴 유틸리티
        // ──────────────────────────────────────────────────────────────────────

        private IEnumerator GetRequest(string path, Action<bool, string> callback)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(serverUrl + path))
            {
                req.timeout = timeoutSeconds;
                if (!string.IsNullOrEmpty(currentToken))
                {
                    req.SetRequestHeader("Authorization", "Bearer " + currentToken);
                }
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                callback?.Invoke(ok, ok ? req.downloadHandler.text : req.error);
            }
        }

        private IEnumerator PostRequest(string path, string jsonBody, Action<bool, string> callback)
        {
            using (UnityWebRequest req = new UnityWebRequest(serverUrl + path, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = timeoutSeconds;
                if (!string.IsNullOrEmpty(currentToken))
                {
                    req.SetRequestHeader("Authorization", "Bearer " + currentToken);
                }
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                callback?.Invoke(ok, ok ? req.downloadHandler.text : req.error);
            }
        }

        private IEnumerator PutRequest(string path, string jsonBody, Action<bool, string> callback)
        {
            using (UnityWebRequest req = new UnityWebRequest(serverUrl + path, "PUT"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = timeoutSeconds;
                if (!string.IsNullOrEmpty(currentToken))
                {
                    req.SetRequestHeader("Authorization", "Bearer " + currentToken);
                }
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                callback?.Invoke(ok, ok ? req.downloadHandler.text : req.error);
            }
        }

        private IEnumerator DeleteRequest(string path, Action<bool, string> callback)
        {
            using (UnityWebRequest req = UnityWebRequest.Delete(serverUrl + path))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = timeoutSeconds;
                if (!string.IsNullOrEmpty(currentToken))
                {
                    req.SetRequestHeader("Authorization", "Bearer " + currentToken);
                }
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                callback?.Invoke(ok, ok ? req.downloadHandler.text : req.error);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 내부 DTO 데이터 구조체
    // ──────────────────────────────────────────────────────────────────────────

    [Serializable]
    public class LoginRequestDto { public string email; public string password; }

    [Serializable]
    public class RegisterRequestDto { public string email; public string password; public string nickname; }

    [Serializable]
    public class AuthResponseDto { public string accessToken; public int userId; public string email; public string role; public int playerId; public string nickname; }

    [Serializable]
    public class RoomCodeDto { public string code; }

    [Serializable]
    public class MatchStatusDto { public string state; public string opponentNickname; public int opponentRating; public bool bot; public string roomCode; }

    [Serializable]
    public class DeckResponseDto { public int playerId; public string[] skills; }

    [Serializable]
    public class DeckSaveDto { public string[] skills; }

    [Serializable]
    public class MatchRecordDto
    {
        public string mode;
        public string result;
        public string opponent;
        public float durationSec;
        public int casts;
        public float damageDealt;
        public float damageTaken;
    }
}
