using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace WarOfWizards.API
{
    /// <summary>
    /// Spring 백엔드와 HTTP(JSON) 통신. 에디터 / PC / Quest / WebGL 모두 같은 코드로 동작한다.
    /// - 씬에 없으면 ApiClient.Instance 를 처음 쓸 때 자동으로 만들어진다.
    /// - 로그인 토큰은 PlayerPrefs 에 저장되어 다음 실행 때 자동 로그인(AuthApi.RestoreSession)에 쓰인다.
    /// - WebGL 빌드를 Spring 서버(/game/)에서 열면 웹 UI 에서 로그인한 토큰을 그대로 이어받는다.
    /// </summary>
    public class ApiClient : MonoBehaviour
    {
        private const string TokenPrefsKey = "wow.token";
        private const string BaseUrlPrefsKey = "wow.baseUrl";
        /// <summary>React 웹 UI 가 localStorage 에 저장하는 토큰 키 (같은 서버에서 열리면 공유)</summary>
        public const string WebTokenKey = "wow.spring.token";

        private static ApiClient instance;

        public static ApiClient Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<ApiClient>();
                    if (instance == null)
                    {
                        instance = new GameObject("[ApiClient]").AddComponent<ApiClient>();
                    }
                }
                return instance;
            }
        }

        /// <summary>토큰 만료 등으로 401 을 받았을 때 (로그인 화면으로 보내면 된다)</summary>
        public static event Action OnUnauthorized;

        [Header("Server")]
        [Tooltip("Spring 서버 주소. 다른 PC / Quest 에서는 서버 PC 의 IP 사용 (예: http://192.168.0.10:8080)")]
        [SerializeField] private string baseUrl = "http://localhost:8080";

        [Tooltip("WebGL 빌드를 Spring 서버(/game/)에서 열 때 페이지 주소를 서버 주소로 사용. itch.io 등 다른 곳에 올리면 끄세요.")]
        [SerializeField] private bool usePageOriginInWebGL = true;

        [SerializeField] private int timeoutSeconds = 15;

        private string authToken = string.Empty;
        private bool initialized;

        public string BaseUrl => baseUrl;
        public bool IsAuthenticated => !string.IsNullOrEmpty(authToken);
        public string AuthToken => authToken;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            string savedUrl = PlayerPrefs.GetString(BaseUrlPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(savedUrl)) baseUrl = savedUrl;

            if (usePageOriginInWebGL && WebGLBridge.IsWebGL)
            {
                string origin = WebGLBridge.GetPageOrigin();
                if (!string.IsNullOrEmpty(origin) && origin.StartsWith("http")) baseUrl = origin;
            }
            baseUrl = baseUrl.TrimEnd('/');

            authToken = PlayerPrefs.GetString(TokenPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(authToken) && WebGLBridge.IsWebGL)
            {
                authToken = WebGLBridge.GetLocalStorage(WebTokenKey) ?? string.Empty;
            }
        }

        /// <summary>설정 화면 등에서 서버 주소 변경 (저장됨)</summary>
        public void SetBaseUrl(string url)
        {
            baseUrl = url.Trim().TrimEnd('/');
            PlayerPrefs.SetString(BaseUrlPrefsKey, baseUrl);
            PlayerPrefs.Save();
        }

        public void SetAuthToken(string token)
        {
            authToken = token ?? string.Empty;
            PlayerPrefs.SetString(TokenPrefsKey, authToken);
            PlayerPrefs.Save();
            if (WebGLBridge.IsWebGL) WebGLBridge.SetLocalStorage(WebTokenKey, authToken);
        }

        public void ClearAuthToken()
        {
            authToken = string.Empty;
            PlayerPrefs.DeleteKey(TokenPrefsKey);
            PlayerPrefs.Save();
            if (WebGLBridge.IsWebGL) WebGLBridge.RemoveLocalStorage(WebTokenKey);
        }

        // ── 요청 메서드 ────────────────────────────────────────────────────

        public void Get<TResponse>(string path, Action<TResponse> onSuccess, Action<string> onError = null)
        {
            Send("GET", path, null, onSuccess, onError);
        }

        /// <summary>응답이 JSON 배열([...])일 때 (JsonUtility 는 최상위 배열을 직접 못 읽는다)</summary>
        public void GetArray<T>(string path, Action<T[]> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(SendRaw("GET", path, null,
                text => onSuccess?.Invoke(JsonHelper.FromJsonArray<T>(text)), onError));
        }

        public void Post<TRequest, TResponse>(string path, TRequest body, Action<TResponse> onSuccess, Action<string> onError = null)
        {
            Send("POST", path, JsonUtility.ToJson(body), onSuccess, onError);
        }

        /// <summary>본문 없이 POST (수락, 읽음 처리, 하트비트 등)</summary>
        public void PostEmpty<TResponse>(string path, Action<TResponse> onSuccess, Action<string> onError = null)
        {
            Send("POST", path, "{}", onSuccess, onError);
        }

        public void Put<TRequest, TResponse>(string path, TRequest body, Action<TResponse> onSuccess, Action<string> onError = null)
        {
            Send("PUT", path, JsonUtility.ToJson(body), onSuccess, onError);
        }

        public void Delete<TResponse>(string path, Action<TResponse> onSuccess, Action<string> onError = null)
        {
            Send("DELETE", path, null, onSuccess, onError);
        }

        /// <summary>파일 다운로드 (제스처 모델 등)</summary>
        public void GetBytes(string path, Action<byte[]> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(SendRequest("GET", path, null, req => onSuccess?.Invoke(req.downloadHandler.data), onError));
        }

        /// <summary>문자열 그대로 받기 (가중치 JSON 등)</summary>
        public void GetText(string path, Action<string> onSuccess, Action<string> onError = null)
        {
            StartCoroutine(SendRaw("GET", path, null, onSuccess, onError));
        }

        private void Send<TResponse>(string method, string path, string json, Action<TResponse> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRaw(method, path, json, text =>
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

        private IEnumerator SendRaw(string method, string path, string json, Action<string> onText, Action<string> onError)
        {
            return SendRequest(method, path, json, req => onText?.Invoke(req.downloadHandler.text), onError);
        }

        private IEnumerator SendRequest(string method, string path, string json, Action<UnityWebRequest> onDone, Action<string> onError)
        {
            Initialize();
            string url = path.StartsWith("http") ? path : baseUrl + path;
            using (UnityWebRequest request = new UnityWebRequest(url, method))
            {
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeoutSeconds;
                if (!string.IsNullOrEmpty(authToken))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + authToken);
                }

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onDone?.Invoke(request);
                    yield break;
                }

                string message = ExtractErrorMessage(request);
                if (request.responseCode == 401)
                {
                    OnUnauthorized?.Invoke();
                }
                Debug.LogWarning($"[ApiClient] {method} {url} 실패 ({request.responseCode}): {message}");
                onError?.Invoke(message);
            }
        }

        /// <summary>서버 오류 응답 {"message": "..."} 에서 한글 메시지를 꺼낸다</summary>
        private static string ExtractErrorMessage(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                return "서버에 연결할 수 없습니다. 서버 주소와 실행 여부를 확인하세요.";
            }
            string text = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
            {
                try
                {
                    ApiErrorDto error = JsonUtility.FromJson<ApiErrorDto>(text);
                    if (error != null && !string.IsNullOrEmpty(error.message)) return error.message;
                }
                catch (Exception)
                {
                    // JSON 이 아니면 아래 기본 메시지
                }
            }
            return $"요청 실패 (HTTP {request.responseCode}) {request.error}";
        }
    }

    /// <summary>JsonUtility 로 JSON 배열 읽기</summary>
    public static class JsonHelper
    {
        public static T[] FromJsonArray<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return new T[0];
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>("{\"items\":" + json + "}");
            return wrapper != null && wrapper.items != null ? wrapper.items : new T[0];
        }

        [Serializable]
        private class Wrapper<T>
        {
            public T[] items;
        }
    }
}
