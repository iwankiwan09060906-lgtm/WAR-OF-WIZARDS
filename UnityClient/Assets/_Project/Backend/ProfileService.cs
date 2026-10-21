// C03 마이페이지 (§23.1) — 닉네임 · 레이팅 · 승/패/무

using System;

namespace SpellboundVR.Backend
{
    public static class ProfileService
    {
        public static void GetMe(Action<PlayerDto> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.Get("/api/players/me", onSuccess, onError);

        public static void Get(long playerId, Action<PlayerDto> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.Get($"/api/players/{playerId}", onSuccess, onError);

        /// <summary>닉네임: 2~12자, 한글 · 영문 · 숫자 · _ (서버가 검사하고 한글 오류를 돌려준다)</summary>
        public static void UpdateNickname(string nickname, Action<string> onSuccess, Action<string> onError = null)
        {
            BackendClient.Instance.Put<NicknameUpdateDto, NicknameUpdateDto>("/api/players/me", new NicknameUpdateDto { nickname = nickname },
                res => onSuccess?.Invoke(res.nickname), onError);
        }
    }
}
