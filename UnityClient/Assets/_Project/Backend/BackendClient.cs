// §23 메타 기능 백엔드 — 서버: Backend/wow_server.py (Python 단일 파일, SQLite)
// 기획서 §28 의 SupabaseClient.cs 자리. 모든 서비스(AuthService, DeckService ...)가 이 클라이언트로 HTTP(JSON) 요청을 보낸다.
//
// - 씬에 없으면 BackendClient.Instance 를 처음 쓸 때 자동으로 만들어진다 (DontDestroyOnLoad).
// - 로그인 토큰은 PlayerPrefs 에 저장되어 다음 실행 때 AuthService.RestoreSession 으로 이어진다.
// - WebGL 빌드를 서버(/game/)에서 열면 페이지 주소를 서버 주소로 쓰고, 웹 UI 로그인 토큰을 이어받는다.
// - Quest 실기에서는 serverUrl 을 서버 PC 의 IP 로 바꾼다 (예: http://192.168.0.10:8080, Backend/open-firewall.bat).

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SpellboundVR.Backend
{
    public sealed class BackendClient : MonoBehaviour
    {
        private const string TokenPrefsKey = "wow.token";
        private const string BaseUrlPrefsKey = "wow.baseUrl";

        /// <summary>웹 UI(static/app/wow-server-api.js)가 localStorage 에 저장하는 토큰 키 (같은 서버에서 열리면 공유)</summary>
        public const string WebTokenKey = "wow.spring.token";

        private static BackendClient _instance;

        public static BackendClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<BackendClient>();
                    if (_instance == null)
                    {
                        _instance = new GameObject("[BackendClient]").AddComponent<BackendClient>();
                    }
                }
                return _instance;
            }
        }

        /// <summary>토큰 만료 등으로 401 을 받았을 때 (로그인 화면으로 보내면 된다)</summary>
        public static event Action OnUnauthorized;

        [Header("Server")]
        [Tooltip("백엔드 서버 주소. Quest / 다른 PC 에서는 서버 PC 의 IP (예: http://192.168.0.10:8080)")]
        [SerializeField] private string serverUrl = "http://localhost:8080";

        [Tooltip("WebGL 빌드를 서버(/game/)에서 열 때 페이지 주소를 서버 주소로 사용")]
        [SerializeField] private bool usePageOriginInWebGL = true;

        [SerializeField] private int timeoutSeconds = 10;

        [Header("Presence")]
        [Tooltip("로그인 중이면 주기적으로 접속 하트비트를 보낸다 (서버는 60초 안에 온 하트비트를 '접속 중'으로 본다)")]
        [SerializeField] private bool sendPresenceHeartbeat = true;
        [SerializeField] private float heartbeatInterval = 20f;

        private string _token = string.Empty;
        private bool _initialized;
        private Coroutine _heartbeat;

        public string ServerUrl { get { Initialize(); return serverUrl; } }
        public bool HasToken { get { Initialize(); return !string.IsNullOrEmpty(_token); } }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            string savedUrl = PlayerPrefs.GetString(BaseUrlPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(savedUrl)) serverUrl = savedUrl;

            if (usePageOriginInWebGL && WebGLBridge.IsWebGL)
            {
                string origin = WebGLBridge.GetPageOrigin();
                if (!string.IsNullOrEmpty(origin) && origin.StartsWith("http")) serverUrl = origin;
            }
            serverUrl = serverUrl.TrimEnd('/');

            _token = PlayerPrefs.GetString(TokenPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(_token) && WebGLBridge.IsWebGL)
            {
                _token = WebGLBridge.GetLocalStorage(WebTokenKey) ?? string.Empty;
            }
        }

        /// <summary>설정 화면 등에서 서버 주소 변경 (저장됨)</summary>
        public void SetServerUrl(string url)
        {
            Initialize();
            serverUrl = (url ?? string.Empty).Trim().TrimEnd('/');
            PlayerPrefs.SetString(BaseUrlPrefsKey, serverUrl);
            PlayerPrefs.Save();
        }

        internal void SetToken(string token)
        {
            Initialize();
            _token = token ?? string.Empty;
            PlayerPrefs.SetString(TokenPrefsKey, _token);
            PlayerPrefs.Save();
            if (WebGLBridge.IsWebGL) WebGLBridge.SetLocalStorage(WebTokenKey, _token);
            UpdateHeartbeat();
        }

        internal void ClearToken()
        {
            _token = string.Empty;
            PlayerPrefs.DeleteKey(TokenPrefsKey);
            PlayerPrefs.Save();
            if (WebGLBridge.IsWebGL) WebGLBridge.RemoveLocalStorage(WebTokenKey);
            UpdateHeartbeat();
        }

        // ── 요청 ──────────────────────────────────────────────

        public void Get<TResponse>(string path, Action<TResponse> onSuccess, Action<string> onError = null)
            => Send("GET", path, null, onSuccess, onError);

        /// <summary>응답이 JSON 배열([...])일 때 (JsonUtility 는 최상위 배열을 직접 못 읽는다)</summary>
        public void GetArray<T>(string path, Action<T[]> onSuccess, Action<string> onError = null)
            => StartCoroutine(SendRequest("GET", path, null, text => onSuccess?.Invoke(JsonArray.From<T>(text)), onError));

        public void Post<TRequest, TResponse>(string path, TRequest body, Action<TResponse> onSuccess, Action<string> onError = null)
            => Send("POST", path, JsonUtility.ToJson(body), onSuccess, onError);

        /// <summary>본문 없이 POST (참가, 하트비트 등)</summary>
        public void PostEmpty<TResponse>(string path, Action<TResponse> onSuccess, Action<string> onError = null)
            => Send("POST", path, "{}", onSuccess, onError);

        public void Put<TRequest, TResponse>(string path, TRequest body, Action<TResponse> onSuccess, Action<string> onError = null)
            => Send("PUT", path, JsonUtility.ToJson(body), onSuccess, onError);

        public void Delete<TResponse>(string path, Action<TResponse> onSuccess, Action<string> onError = null)
            => Send("DELETE", path, null, onSuccess, onError);

        private void Send<TResponse>(string method, string path, string json, Action<TResponse> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRequest(method, path, json, text =>
            {
                TResponse result;
                try
                {
                    result = JsonUtility.FromJson<TResponse>(text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"응답을 해석하지 못했습니다: {ex.Message}");
                    return;
                }
                onSuccess?.Invoke(result);
            }, onError));
        }

        private IEnumerator SendRequest(string method, string path, string json, Action<string> onText, Action<string> onError)
        {
            Initialize();
            string url = path.StartsWith("http") ? path : serverUrl + path;
            using (var request = new UnityWebRequest(url, method))
            {
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeoutSeconds;
                if (!string.IsNullOrEmpty(_token))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + _token);
                }

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onText?.Invoke(request.downloadHandler.text);
                    yield break;
                }

                string message = ExtractErrorMessage(request);
                if (request.responseCode == 401) OnUnauthorized?.Invoke();
                Debug.LogWarning($"[BackendClient] {method} {path} 실패 ({request.responseCode}): {message}");
                onError?.Invoke(message);
            }
        }

        /// <summary>연결 자체가 안 될 때 돌려주는 메시지 (서비스가 오프라인 폴백을 판단할 때 쓴다)</summary>
        public const string ConnectionErrorMessage = "서버에 연결할 수 없습니다. 서버 주소와 실행 여부를 확인하세요.";

        /// <summary>서버 오류 응답 {"status":400,"message":"..."} 에서 한글 메시지를 꺼낸다</summary>
        private static string ExtractErrorMessage(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError) return ConnectionErrorMessage;

            string text = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
            {
                try
                {
                    var error = JsonUtility.FromJson<ErrorDto>(text);
                    if (error != null && !string.IsNullOrEmpty(error.message)) return error.message;
                }
                catch (Exception)
                {
                    // JSON 이 아니면 아래 기본 메시지
                }
            }
            return $"요청 실패 (HTTP {request.responseCode}) {request.error}";
        }

        // ── 접속 하트비트 ─────────────────────────────────────

        private void UpdateHeartbeat()
        {
            bool shouldRun = sendPresenceHeartbeat && !string.IsNullOrEmpty(_token) && isActiveAndEnabled;
            if (shouldRun && _heartbeat == null) _heartbeat = StartCoroutine(HeartbeatLoop());
            else if (!shouldRun && _heartbeat != null)
            {
                StopCoroutine(_heartbeat);
                _heartbeat = null;
            }
        }

        private IEnumerator HeartbeatLoop()
        {
            var wait = new WaitForSecondsRealtime(Mathf.Max(5f, heartbeatInterval));
            while (true)
            {
                PresenceService.Heartbeat();
                yield return wait;
            }
        }

        private void OnEnable()
        {
            if (_instance == this) UpdateHeartbeat();
        }

        private void OnDisable()
        {
            _heartbeat = null; // 비활성화되면 코루틴은 자동으로 멈춘다
        }
    }

    /// <summary>JsonUtility 로 최상위 JSON 배열 읽기</summary>
    public static class JsonArray
    {
        public static T[] From<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return new T[0];
            var wrapper = JsonUtility.FromJson<Wrapper<T>>("{\"items\":" + json + "}");
            return wrapper != null && wrapper.items != null ? wrapper.items : new T[0];
        }

        [Serializable]
        private class Wrapper<T>
        {
            public T[] items = null;
        }
    }
}
