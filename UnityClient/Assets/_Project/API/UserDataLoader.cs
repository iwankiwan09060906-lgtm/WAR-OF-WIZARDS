using System;
using UnityEngine;
using WarOfWizards.Core;
using WarOfWizards.Network;

namespace WarOfWizards.API
{
    /// <summary>
    /// 게임 시작 시 Spring 서버에서 내 정보를 불러오는 예제/부트스트랩 컴포넌트.
    /// 1) 저장된 토큰으로 자동 로그인 (없으면 인스펙터의 테스트 계정으로 로그인)
    /// 2) 프로필 · 마법서 덱 불러오기 → PlayerCastingManager 에 덱 적용
    /// 3) 접속 상태 하트비트 시작
    /// (예전 Supabase 직접 조회 방식은 Spring 서버 경유로 바뀌었다)
    /// </summary>
    public class UserDataLoader : MonoBehaviour
    {
        [Header("테스트 계정 (저장된 로그인 정보가 없을 때)")]
        [SerializeField] private bool loginWithTestAccount = true;
        [SerializeField] private string testEmail = "demo@wow.dev";
        [SerializeField] private string testPassword = "demo1234";

        [Header("연결 대상 (선택)")]
        [SerializeField] private PlayerCastingManager castingManager;

        public ProfileDto Profile { get; private set; }
        public DeckDto Deck { get; private set; }

        public event Action<ProfileDto, DeckDto> OnUserDataLoaded;
        public event Action<string> OnLoadFailed;

        private void Start()
        {
            ServerApi.Health(
                health => Debug.Log($"[UserDataLoader] 서버 연결 OK ({ApiClient.Instance.BaseUrl}, DB: {string.Join(",", health.db)})"),
                err => Debug.LogWarning($"[UserDataLoader] 서버 연결 실패: {err}"));

            AuthApi.RestoreSession(_ => LoadMyProfileData(), _ =>
            {
                if (loginWithTestAccount)
                {
                    AuthApi.Login(testEmail, testPassword, __ => LoadMyProfileData(), Fail);
                }
            });
        }

        public void LoadMyProfileData()
        {
            PlayerApi.GetMe(profile =>
            {
                Profile = profile;
                Debug.Log($"환영합니다! 닉네임: {profile.nickname} (레이팅: {profile.rating}, {profile.tier})");
                Debug.Log($"전적: {profile.wins}승 {profile.losses}패 (승률 {profile.winRate}%)");

                DeckApi.GetMine(deck =>
                {
                    Deck = deck;
                    Debug.Log($"마법서 덱: {string.Join(", ", deck.skills)}{(deck.complete ? "" : " (8장 미완성)")}");
                    if (castingManager != null) castingManager.SetDeck(deck.skills);
                    EnsureHeartbeat();
                    OnUserDataLoaded?.Invoke(Profile, Deck);
                }, Fail);
            }, Fail);
        }

        /// <summary>게임이 끝나고 결과를 저장하는 예시 (봇전 / 친선전)</summary>
        public void SaveMatchResult(bool win, string opponent, int durationSec, int casts)
        {
            var record = new MatchRecordRequestDto
            {
                mode = "bot",
                result = win ? "win" : "lose",
                opponent = opponent,
                durationSec = durationSec,
                casts = casts,
            };
            MatchApi.Record(record,
                res => Debug.Log($"[UserDataLoader] 전적 저장 완료 (레이팅 {res.ratingBefore} → {res.ratingAfter})"),
                err => Debug.LogError($"[UserDataLoader] 전적 저장 실패: {err}"));
        }

        private void EnsureHeartbeat()
        {
            if (FindAnyObjectByType<PresenceHeartbeat>() == null)
            {
                gameObject.AddComponent<PresenceHeartbeat>();
            }
        }

        private void Fail(string error)
        {
            Debug.LogError($"[UserDataLoader] {error}");
            OnLoadFailed?.Invoke(error);
        }
    }
}
