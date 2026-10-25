// Google OAuth 2.0 Device Authorization Grant (Device Flow, RFC 8628) — 키보드 · 브라우저가 없는 Quest에서 Google 로그인.
//
//   1. POST https://oauth2.googleapis.com/device/code (client_id, scope)
//        → device_code · user_code · verification_url(https://www.google.com/device) · expires_in · interval
//   2. 화면에 "주소 + 코드 + 남은 시간"을 띄운다. 유저는 휴대폰 · PC 브라우저로 주소를 열어 코드를 입력하고 계정을 고른다.
//   3. interval 초마다 POST https://oauth2.googleapis.com/token (grant_type = device_code) 을 폴링
//        authorization_pending → 계속 기다림 / slow_down → 간격 +5초 / access_denied · expired_token → 실패
//   4. 성공하면 access_token · id_token 을 받는다 → AuthService.LoginWithGoogle 로 게임 서버(/api/auth/google)에 넘겨 게임 토큰으로 바꾼다.
//
// 설정
//   · 클라이언트 ID: 아래 DefaultClientId (Google Cloud Console → OAuth 클라이언트 유형 "TV 및 입력 제한 기기").
//   · 클라이언트 보안 비밀(client_secret): Google은 이 유형도 토큰 교환 때 client_secret 을 요구한다.
//     저장소에 올리지 않도록 Assets/_Project/Backend/Resources/GoogleOAuth/device_client.json 에 둔다 (.gitignore 처리됨).
//     없으면 코드를 발급하기 전에 바로 안내 메시지로 실패한다 (유저가 코드를 입력해 놓고 마지막에 실패하지 않게).
//     Console에서 받은 JSON 그대로({"installed":{...}}) 또는 {"client_id":"...","client_secret":"..."} 둘 다 읽는다.
//   · 게임 서버도 이 클라이언트 ID로 발급된 토큰을 받아야 한다 — 관리자 설정 auth.google_device_client_id (기본값 = 같은 ID).
//
// 쓰는 법
//   · 코드로 만든 UI: AddComponent 후 이벤트(OnCodeIssued · OnStatus · OnSucceeded · OnFailed)를 구독하고 Begin().
//   · 씬에 배치: urlText · userCodeText · expiryText · statusText 에 UI Text를 연결하면 직접 채운다. beginOnStart 로 자동 시작.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace SpellboundVR.Backend
{
    public sealed class GoogleDeviceAuth : MonoBehaviour
    {
        public const string DefaultClientId = "538311580843-8ikodv8md34jflgqn1d8ijevuocpu80m.apps.googleusercontent.com";
        private const string DeviceCodeUrl = "https://oauth2.googleapis.com/device/code";
        private const string TokenUrl = "https://oauth2.googleapis.com/token";
        private const string GrantType = "urn:ietf:params:oauth:grant-type:device_code";
        private const string SecretResource = "GoogleOAuth/device_client";

        [Header("OAuth 클라이언트 (TV 및 입력 제한 기기)")]
        public string clientId = DefaultClientId;
        [Tooltip("비우면 Resources/GoogleOAuth/device_client.json 에서 읽는다 (저장소에 올리지 말 것)")]
        public string clientSecret = "";
        [Tooltip("id_token(이메일 · 이름)을 받으려면 openid email profile")]
        public string scope = "openid email profile";

        [Header("표시할 UI Text (선택)")]
        public Text urlText;
        public Text userCodeText;
        public Text expiryText;
        public Text statusText;

        [Header("동작")]
        public bool beginOnStart;
        [SerializeField] private int requestTimeoutSeconds = 15;

        /// <summary>코드 발급됨 — 화면에 주소 · 코드 · 남은 시간을 띄운다</summary>
        public event Action<DeviceCode> OnCodeIssued;
        /// <summary>진행 상황 한 줄 (기다리는 중 · 간격 늘림 등)</summary>
        public event Action<string> OnStatus;
        /// <summary>유저가 승인해 토큰을 받음</summary>
        public event Action<GoogleTokens> OnSucceeded;
        /// <summary>거절 · 만료 · 설정 오류 · 네트워크 오류 (한글 메시지)</summary>
        public event Action<string> OnFailed;

        public bool IsRunning => _routine != null;
        public DeviceCode Current { get; private set; }
        /// <summary>코드가 만료될 때까지 남은 초 (진행 중이 아니면 0)</summary>
        public float SecondsLeft => IsRunning && Current != null ? Mathf.Max(0f, (float)(_expiresAt - Time.realtimeSinceStartupAsDouble)) : 0f;

        private Coroutine _routine;
        private double _expiresAt;
        private int _lastShownSecond = -1;

        [Serializable]
        public sealed class DeviceCode
        {
            public string device_code;
            public string user_code;
            public string verification_url;   // Google
            public string verification_uri;   // RFC 8628 표준 이름 (둘 중 있는 것)
            public int expires_in;
            public int interval;

            public string Url => !string.IsNullOrEmpty(verification_url) ? verification_url
                               : !string.IsNullOrEmpty(verification_uri) ? verification_uri : "https://www.google.com/device";
        }

        [Serializable]
        public sealed class GoogleTokens
        {
            public string access_token;
            public string id_token;
            public string refresh_token;
            public int expires_in;
            public string token_type;
            public string scope;
        }

        [Serializable]
        private sealed class OAuthError
        {
            public string error;
            public string error_description;
        }

        [Serializable]
        private sealed class ClientKeys
        {
            public string client_id;
            public string client_secret;
        }

        /// <summary>Console에서 받은 JSON {"installed":{...}} 또는 평평한 {"client_id","client_secret"}</summary>
        [Serializable]
        private sealed class ClientFile
        {
            public string client_id;
            public string client_secret;
            public ClientKeys installed;
        }

        private void Start()
        {
            if (beginOnStart) Begin();
        }

        private void OnDisable() => Cancel();

        /// <summary>코드 발급부터 다시 시작 (진행 중이면 처음부터)</summary>
        public void Begin()
        {
            Cancel();
            LoadSecretIfNeeded();
            _routine = StartCoroutine(Run());
        }

        /// <summary>폴링을 멈춘다 (화면을 닫거나 취소 버튼)</summary>
        public void Cancel()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            Current = null;
            _lastShownSecond = -1;
        }

        private void LoadSecretIfNeeded()
        {
            if (!string.IsNullOrEmpty(clientSecret)) return;
            var asset = Resources.Load<TextAsset>(SecretResource);
            if (asset == null) return;
            try
            {
                var file = JsonUtility.FromJson<ClientFile>(asset.text);
                if (file == null) return;
                bool nested = file.installed != null && !string.IsNullOrEmpty(file.installed.client_secret);
                string secret = nested ? file.installed.client_secret : file.client_secret;
                string id = nested ? file.installed.client_id : file.client_id;
                if (!string.IsNullOrEmpty(secret)) clientSecret = secret.Trim();
                if (!string.IsNullOrEmpty(id)) clientId = id.Trim();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GoogleDeviceAuth] {SecretResource}.json 을 읽지 못했습니다: {e.Message}");
            }
        }

        /// <summary>client_secret 이 없을 때 안내 (Google은 이 유형도 토큰 교환에 client_secret 을 요구한다)</summary>
        public const string MissingSecretMessage =
            "Google 기기 로그인 설정이 끝나지 않았습니다: client_secret 이 없습니다. " +
            "Google Cloud Console에서 이 OAuth 클라이언트의 JSON을 받아 Assets/_Project/Backend/Resources/GoogleOAuth/device_client.json 으로 저장하세요.";

        private IEnumerator Run()
        {
            // 코드를 입력하게 해 놓고 마지막에 실패하지 않도록 먼저 확인
            if (string.IsNullOrEmpty(clientSecret)) { Fail(MissingSecretMessage); yield break; }
            Status("Google에 로그인 코드를 요청하는 중…");

            // 1. 기기 코드 발급
            string codeText = null, codeError = null;
            long codeHttp = 0;
            yield return PostForm(DeviceCodeUrl, new Dictionary<string, string> { { "client_id", clientId }, { "scope", scope } },
                                  (t, http) => { codeText = t; codeHttp = http; }, e => codeError = e);
            if (codeError != null) { Fail(codeError); yield break; }

            DeviceCode code = null;
            try { code = JsonUtility.FromJson<DeviceCode>(codeText); }
            catch (Exception) { /* 아래에서 처리 */ }
            if (code == null || string.IsNullOrEmpty(code.device_code))
            {
                Fail(Explain(ParseError(codeText), codeHttp));
                yield break;
            }

            Current = code;
            _expiresAt = Time.realtimeSinceStartupAsDouble + Mathf.Max(30, code.expires_in);
            float interval = Mathf.Max(1, code.interval > 0 ? code.interval : 5);
            ShowCode(code);
            OnCodeIssued?.Invoke(code);
            Status("코드를 입력하면 자동으로 로그인됩니다. 승인을 기다리는 중…");

            // 2. 폴링
            while (true)
            {
                double wait = Time.realtimeSinceStartupAsDouble + interval;
                while (Time.realtimeSinceStartupAsDouble < wait)
                {
                    if (Time.realtimeSinceStartupAsDouble >= _expiresAt) { Fail("코드가 만료되었습니다. 다시 시도해 주세요."); yield break; }
                    yield return null;
                }

                var form = new Dictionary<string, string>
                {
                    { "client_id", clientId },
                    { "device_code", code.device_code },
                    { "grant_type", GrantType },
                };
                form["client_secret"] = clientSecret;

                string text = null, netError = null;
                long http = 0;
                yield return PostForm(TokenUrl, form, (t, h) => { text = t; http = h; }, e => netError = e);
                if (netError != null)
                {
                    // 잠깐 끊긴 것일 수 있으니 만료 전까지는 계속 시도
                    Status("네트워크가 불안정합니다. 다시 시도하는 중…");
                    continue;
                }

                if (http == 200)
                {
                    GoogleTokens tokens = null;
                    try { tokens = JsonUtility.FromJson<GoogleTokens>(text); }
                    catch (Exception) { /* 아래에서 처리 */ }
                    if (tokens == null || (string.IsNullOrEmpty(tokens.access_token) && string.IsNullOrEmpty(tokens.id_token)))
                    {
                        Fail("Google 응답에 토큰이 없습니다.");
                        yield break;
                    }
                    _routine = null;
                    Status("Google 승인 완료!");
                    OnSucceeded?.Invoke(tokens);
                    yield break;
                }

                var err = ParseError(text);
                switch (err.error)
                {
                    case "authorization_pending":
                        break; // 아직 입력 안 함
                    case "slow_down":
                        interval += 5f;
                        Status("요청이 너무 잦아 확인 간격을 늘렸습니다. 승인을 기다리는 중…");
                        break;
                    default:
                        Fail(Explain(err, http));
                        yield break;
                }
            }
        }

        private void Update()
        {
            if (!IsRunning || Current == null || expiryText == null) return;
            int s = Mathf.CeilToInt(SecondsLeft);
            if (s == _lastShownSecond) return;
            _lastShownSecond = s;
            expiryText.text = FormatExpiry(s);
        }

        /// <summary>"남은 시간 14:32"</summary>
        public static string FormatExpiry(int seconds) => $"남은 시간 {seconds / 60}:{seconds % 60:00}";

        private void ShowCode(DeviceCode code)
        {
            if (urlText != null) urlText.text = code.Url;
            if (userCodeText != null) userCodeText.text = code.user_code;
            if (expiryText != null) expiryText.text = FormatExpiry(code.expires_in);
        }

        private void Status(string message)
        {
            if (statusText != null) statusText.text = message;
            OnStatus?.Invoke(message);
        }

        private void Fail(string message)
        {
            _routine = null;
            Current = null;
            Debug.LogWarning("[GoogleDeviceAuth] " + message);
            if (statusText != null) statusText.text = message;
            OnFailed?.Invoke(message);
        }

        private static OAuthError ParseError(string text)
        {
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
            {
                try
                {
                    var e = JsonUtility.FromJson<OAuthError>(text);
                    if (e != null && !string.IsNullOrEmpty(e.error)) return e;
                }
                catch (Exception) { /* 아래 기본값 */ }
            }
            return new OAuthError { error = "unknown", error_description = text };
        }

        private static string Explain(OAuthError e, long http)
        {
            switch (e.error)
            {
                case "access_denied": return "Google 로그인이 거부되었습니다.";
                case "expired_token": return "코드가 만료되었습니다. 다시 시도해 주세요.";
                case "invalid_client":
                    return (e.error_description ?? "").IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "client_secret 이 없거나 틀립니다. Resources/GoogleOAuth/device_client.json 을 확인하세요."
                        : "OAuth 클라이언트 ID가 올바르지 않습니다.";
                case "invalid_request" when (e.error_description ?? "").IndexOf("client_secret", StringComparison.OrdinalIgnoreCase) >= 0:
                    return MissingSecretMessage;
                case "unauthorized_client":
                case "invalid_request" when (e.error_description ?? "").IndexOf("client type", StringComparison.OrdinalIgnoreCase) >= 0:
                    return "이 클라이언트 ID로는 기기 로그인을 쓸 수 없습니다. Google Cloud Console에서 유형이 'TV 및 입력 제한 기기'인 OAuth 클라이언트를 만드세요.";
                case "invalid_scope": return "요청한 권한(scope)을 쓸 수 없습니다: " + e.error_description;
                case "invalid_grant": return "로그인 코드가 더 이상 유효하지 않습니다. 다시 시도해 주세요.";
                default:
                    return $"Google 로그인 실패 (HTTP {http}) {e.error} {e.error_description}".Trim();
            }
        }

        private IEnumerator PostForm(string url, Dictionary<string, string> fields, Action<string, long> onResponse, Action<string> onNetworkError)
        {
            var body = new StringBuilder();
            foreach (var kv in fields)
            {
                if (body.Length > 0) body.Append('&');
                body.Append(UnityWebRequest.EscapeURL(kv.Key)).Append('=').Append(UnityWebRequest.EscapeURL(kv.Value ?? ""));
            }
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                req.timeout = requestTimeoutSeconds;
                yield return req.SendWebRequest();
                // 4xx 도 본문(JSON 오류)이 의미 있으므로 응답으로 넘긴다 — 연결 자체 실패만 네트워크 오류
                if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                    onNetworkError?.Invoke("Google 서버에 연결할 수 없습니다. 인터넷 연결을 확인하세요. (" + req.error + ")");
                else
                    onResponse?.Invoke(req.downloadHandler.text, req.responseCode);
            }
        }
    }
}
