using System;

namespace WarOfWizards.API
{
    /// <summary>
    /// 회원가입 / 로그인 / 자동 로그인 / 로그아웃. 씬에 붙일 필요 없이 AuthApi.Login(...) 처럼 바로 호출한다.
    /// 웹 UI 에서 가입한 계정으로 Unity 에서도 그대로 로그인된다 (같은 서버·같은 DB).
    /// </summary>
    public static class AuthApi
    {
        public static AuthResponseDto CurrentUser { get; private set; }
        public static bool IsLoggedIn => CurrentUser != null && ApiClient.Instance.IsAuthenticated;
        public static long PlayerId => CurrentUser != null ? CurrentUser.playerId : 0;

        public static event Action<AuthResponseDto> OnLoginSuccess;
        public static event Action<string> OnAuthFailed;
        public static event Action OnLoggedOut;

        public static void Register(string email, string password, string nickname,
            Action<AuthResponseDto> callback = null, Action<string> onError = null)
        {
            var req = new RegisterRequestDto { email = email, password = password, nickname = nickname };
            ApiClient.Instance.Post<RegisterRequestDto, AuthResponseDto>("/api/auth/register", req,
                res => HandleAuth(res, callback), err => HandleError(err, onError));
        }

        public static void Login(string email, string password,
            Action<AuthResponseDto> callback = null, Action<string> onError = null)
        {
            var req = new LoginRequestDto { email = email, password = password };
            ApiClient.Instance.Post<LoginRequestDto, AuthResponseDto>("/api/auth/login", req,
                res => HandleAuth(res, callback), err => HandleError(err, onError));
        }

        /// <summary>저장된 토큰(또는 WebGL 에서 웹 UI 로그인 토큰)으로 자동 로그인</summary>
        public static void RestoreSession(Action<AuthResponseDto> callback = null, Action<string> onError = null)
        {
            if (!ApiClient.Instance.IsAuthenticated)
            {
                onError?.Invoke("저장된 로그인 정보가 없습니다.");
                return;
            }
            ApiClient.Instance.Get<AuthResponseDto>("/api/auth/me",
                res => HandleAuth(res, callback),
                err =>
                {
                    ApiClient.Instance.ClearAuthToken();
                    HandleError(err, onError);
                });
        }

        public static void CheckNickname(string nickname, Action<NicknameCheckDto> callback, Action<string> onError = null)
        {
            ApiClient.Instance.Get("/api/auth/nickname-available?nickname=" + Uri.EscapeDataString(nickname), callback, onError);
        }

        public static void Logout()
        {
            if (ApiClient.Instance.IsAuthenticated)
            {
                ApiClient.Instance.PostEmpty<ActionResultDto>("/api/auth/logout", null);
            }
            ApiClient.Instance.ClearAuthToken();
            CurrentUser = null;
            OnLoggedOut?.Invoke();
        }

        private static void HandleAuth(AuthResponseDto res, Action<AuthResponseDto> callback)
        {
            CurrentUser = res;
            ApiClient.Instance.SetAuthToken(res.accessToken);
            OnLoginSuccess?.Invoke(res);
            callback?.Invoke(res);
        }

        private static void HandleError(string error, Action<string> onError)
        {
            OnAuthFailed?.Invoke(error);
            onError?.Invoke(error);
        }
    }
}
