// C01 로그인 · 회원가입 (§23.1). 웹 UI 에서 가입한 계정으로 Unity 에서도 그대로 로그인된다 (같은 서버 · 같은 DB).
// 예) AuthService.Login("demo@wow.dev", "demo1234", user => Debug.Log(user.nickname), err => Debug.LogWarning(err));

using System;

namespace SpellboundVR.Backend
{
    public static class AuthService
    {
        public static AuthResponseDto CurrentUser { get; private set; }
        public static bool IsLoggedIn => CurrentUser != null && BackendClient.Instance.HasToken;
        public static long PlayerId => CurrentUser != null ? CurrentUser.playerId : 0;
        public static string Nickname => CurrentUser != null ? CurrentUser.nickname : string.Empty;
        public static bool IsAdmin => CurrentUser != null && CurrentUser.role == "ROLE_ADMIN";

        public static event Action<AuthResponseDto> OnLoggedIn;
        public static event Action OnLoggedOut;

        public static void Register(string email, string password, string nickname,
            Action<AuthResponseDto> onSuccess = null, Action<string> onError = null)
        {
            var req = new RegisterRequestDto { email = email, password = password, nickname = nickname };
            BackendClient.Instance.Post<RegisterRequestDto, AuthResponseDto>("/api/auth/register", req,
                res => HandleAuth(res, onSuccess), onError);
        }

        public static void Login(string email, string password,
            Action<AuthResponseDto> onSuccess = null, Action<string> onError = null)
        {
            var req = new LoginRequestDto { email = email, password = password };
            BackendClient.Instance.Post<LoginRequestDto, AuthResponseDto>("/api/auth/login", req,
                res => HandleAuth(res, onSuccess), onError);
        }

        /// <summary>닉네임만으로 바로 입장 (시연 · 에디터 두 번째 플레이어 테스트용)</summary>
        public static void LoginAsGuest(string nickname, Action<AuthResponseDto> onSuccess = null, Action<string> onError = null)
        {
            BackendClient.Instance.Post<GuestRequestDto, AuthResponseDto>("/api/auth/guest", new GuestRequestDto { nickname = nickname },
                res => HandleAuth(res, onSuccess), onError);
        }

        /// <summary>저장된 토큰(또는 WebGL 에서 웹 UI 로그인 토큰)으로 자동 로그인. 성공하면 토큰도 새로 받는다.</summary>
        public static void RestoreSession(Action<AuthResponseDto> onSuccess = null, Action<string> onError = null)
        {
            if (!BackendClient.Instance.HasToken)
            {
                onError?.Invoke("저장된 로그인 정보가 없습니다.");
                return;
            }
            BackendClient.Instance.Get<AuthResponseDto>("/api/auth/me",
                res => HandleAuth(res, onSuccess),
                err =>
                {
                    // 연결 실패는 토큰을 지우지 않는다 (서버만 꺼져 있을 수 있음)
                    if (err != BackendClient.ConnectionErrorMessage) BackendClient.Instance.ClearToken();
                    onError?.Invoke(err);
                });
        }

        public static void CheckNickname(string nickname, Action<bool> onResult, Action<string> onError = null)
        {
            BackendClient.Instance.Get<NicknameCheckDto>("/api/auth/nickname-available?nickname=" + Uri.EscapeDataString(nickname ?? ""),
                res => onResult?.Invoke(res.available), onError);
        }

        public static void ChangePassword(string currentPassword, string newPassword, Action onSuccess = null, Action<string> onError = null)
        {
            var req = new PasswordChangeRequestDto { currentPassword = currentPassword, newPassword = newPassword };
            BackendClient.Instance.Put<PasswordChangeRequestDto, MessageDto>("/api/auth/password", req, _ => onSuccess?.Invoke(), onError);
        }

        /// <summary>비밀번호 재설정 요청 — 메일 서버가 없어 관리자 콘솔 기록으로 남는다 (서버가 돌려준 안내 문구를 넘긴다)</summary>
        /// <summary>Google 로그인 — GoogleDeviceAuth(기기 로그인)로 받은 토큰을 서버가 Google에 확인하고 게임 토큰을 준다.
        /// 처음 오는 Google 계정은 서버가 자동으로 계정 · 플레이어를 만든다.</summary>
        public static void LoginWithGoogle(string idToken, string accessToken,
            Action<AuthResponseDto> onSuccess = null, Action<string> onError = null)
        {
            var req = new GoogleLoginRequestDto { credential = idToken ?? string.Empty, accessToken = accessToken ?? string.Empty };
            BackendClient.Instance.Post<GoogleLoginRequestDto, AuthResponseDto>("/api/auth/google", req,
                res => HandleAuth(res, onSuccess), onError);
        }

        public static void RequestPasswordReset(string email, Action<string> onSuccess = null, Action<string> onError = null)
        {
            var req = new PasswordResetRequestDto { email = email };
            BackendClient.Instance.Post<PasswordResetRequestDto, MessageDto>("/api/auth/password-reset", req, res => onSuccess?.Invoke(res.message), onError);
        }

        public static void Logout()
        {
            if (BackendClient.Instance.HasToken)
            {
                BackendClient.Instance.PostEmpty<MessageDto>("/api/auth/logout", null);
            }
            BackendClient.Instance.ClearToken();
            CurrentUser = null;
            OnLoggedOut?.Invoke();
        }

        private static void HandleAuth(AuthResponseDto res, Action<AuthResponseDto> onSuccess)
        {
            CurrentUser = res;
            BackendClient.Instance.SetToken(res.accessToken);
            OnLoggedIn?.Invoke(res);
            onSuccess?.Invoke(res);
        }
    }
}
