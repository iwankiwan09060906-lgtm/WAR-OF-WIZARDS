// 접속 상태 (§23.3 Presence 대체 — 주기 조회 방식). 하트비트는 BackendClient 가 로그인 중 자동으로 보낸다.

using System;

namespace SpellboundVR.Backend
{
    public static class PresenceService
    {
        public static void Heartbeat(Action onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.PostEmpty<MessageDto>("/api/presence/heartbeat", _ => onSuccess?.Invoke(), onError);

        /// <summary>지금 접속 중인 플레이어 id 목록 (최근 60초 하트비트)</summary>
        public static void GetOnline(Action<long[]> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.Get<OnlineDto>("/api/presence/online", res => onSuccess?.Invoke(res.online ?? new long[0]), onError);
    }
}
