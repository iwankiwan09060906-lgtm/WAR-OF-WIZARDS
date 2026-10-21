// C05 친구 목록 (§23.1) — 요청 · 수락 · 거절 · 삭제. 접속 상태는 PresenceService.GetOnline 과 friendPlayerId 를 맞춰 본다.

using System;

namespace SpellboundVR.Backend
{
    public static class FriendService
    {
        /// <summary>includePending = true 면 받은(incoming) · 보낸(outgoing) 요청까지</summary>
        public static void List(bool includePending, Action<FriendDto[]> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.GetArray(includePending ? "/api/friends?all=1" : "/api/friends", onSuccess, onError);

        /// <summary>상대가 먼저 요청해 두었으면 바로 친구가 된다 (응답 status = ACCEPTED)</summary>
        public static void Request(string nickname, Action<MessageDto> onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.Post<FriendRequestDto, MessageDto>("/api/friends/requests", new FriendRequestDto { nickname = nickname }, onSuccess, onError);

        /// <summary>받은 요청에 응답 (requesterPlayerId = 요청 보낸 사람의 friendPlayerId)</summary>
        public static void Respond(long requesterPlayerId, bool accept, Action<bool> onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.Post<FriendRespondDto, FriendRespondResultDto>("/api/friends/respond",
                new FriendRespondDto { playerId = requesterPlayerId, accept = accept }, res => onSuccess?.Invoke(res.accepted), onError);

        /// <summary>친구 삭제 또는 보낸 요청 취소</summary>
        public static void Remove(long friendPlayerId, Action onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.Delete<MessageDto>($"/api/friends/player/{friendPlayerId}", _ => onSuccess?.Invoke(), onError);
    }
}
