using System;
using UnityEngine;

// 게임 기능별 API (씬에 붙일 필요 없음). 모든 콜백은 메인 스레드에서 호출된다.
// 예) DeckApi.GetMine(deck => Debug.Log(string.Join(",", deck.skills)), err => Debug.LogWarning(err));
namespace WarOfWizards.API
{
    public static class ServerApi
    {
        public static void Health(Action<HealthDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Get("/api/health", onSuccess, onError);
    }

    public static class PlayerApi
    {
        public static void GetMe(Action<ProfileDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Get("/api/players/me", onSuccess, onError);

        public static void Get(long playerId, Action<ProfileDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Get($"/api/players/{playerId}", onSuccess, onError);

        public static void UpdateNickname(string nickname, Action<ProfileDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Put<NicknameUpdateDto, ProfileDto>("/api/players/me", new NicknameUpdateDto { nickname = nickname }, onSuccess, onError);

        public static void GetRanking(int limit, Action<RankEntryDto[]> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetArray($"/api/players/ranking?limit={limit}", onSuccess, onError);
    }

    public static class SkillApi
    {
        public static void GetAll(Action<SkillDto[]> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetArray("/api/skills", onSuccess, onError);
    }

    /// <summary>마법서: 19종 중 8장, 중복 불가, 고위력(H) 최대 2장 — 서버가 검사하고 한글 오류를 돌려준다</summary>
    public static class DeckApi
    {
        public static void GetMine(Action<DeckDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Get("/api/decks/me", onSuccess, onError);

        public static void Save(string[] skills, Action<DeckDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Put<DeckRequestDto, DeckDto>("/api/decks/me", new DeckRequestDto { skills = skills }, onSuccess, onError);
    }

    public static class MatchApi
    {
        /// <summary>봇전 / 친선전 / (프로토타입) 랭크전 결과 신고</summary>
        public static void Record(MatchRecordRequestDto record, Action<MatchRecordDto> onSuccess, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(record.matchToken)) record.matchToken = Guid.NewGuid().ToString();
            ApiClient.Instance.Post<MatchRecordRequestDto, MatchRecordDto>("/api/matches", record, onSuccess, onError);
        }

        public static void GetMine(int limit, Action<MatchRecordDto[]> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetArray($"/api/matches/me?limit={limit}", onSuccess, onError);

        public static void GetPlayer(long playerId, int limit, Action<MatchRecordDto[]> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetArray($"/api/matches/player/{playerId}?limit={limit}", onSuccess, onError);
    }

    public static class FriendApi
    {
        public static void List(Action<FriendDto[]> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetArray("/api/friends", onSuccess, onError);

        public static void Request(string nickname, Action<ActionResultDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Post<FriendRequestDto, ActionResultDto>("/api/friends/requests", new FriendRequestDto { nickname = nickname }, onSuccess, onError);

        public static void Accept(long playerId, Action<ActionResultDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.PostEmpty($"/api/friends/{playerId}/accept", onSuccess, onError);

        public static void Reject(long playerId, Action<ActionResultDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.PostEmpty($"/api/friends/{playerId}/reject", onSuccess, onError);

        /// <summary>친구 삭제 또는 보낸 요청 취소</summary>
        public static void Remove(long playerId, Action<ActionResultDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Delete($"/api/friends/{playerId}", onSuccess, onError);
    }

    public static class MailApi
    {
        public static void List(Action<MailDto[]> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetArray("/api/mails", onSuccess, onError);

        public static void UnreadCount(Action<UnreadCountDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Get("/api/mails/unread-count", onSuccess, onError);

        public static void MarkRead(string mailId, Action<ActionResultDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.PostEmpty($"/api/mails/{mailId}/read", onSuccess, onError);

        public static void Delete(string mailId, Action<ActionResultDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Delete($"/api/mails/{mailId}", onSuccess, onError);

        /// <summary>친구에게 친선전 초대 (roomCode = Photon 룸 이름)</summary>
        public static void SendInvite(long friendId, string roomCode, Action<ActionResultDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Post<InviteRequestDto, ActionResultDto>("/api/mails/invite",
                new InviteRequestDto { friendId = friendId, roomCode = roomCode }, onSuccess, onError);
    }

    public static class PresenceApi
    {
        /// <summary>20초마다 호출 (PresenceHeartbeat 컴포넌트가 대신 해준다)</summary>
        public static void Heartbeat(Action<PresenceDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.PostEmpty("/api/presence/heartbeat", onSuccess, onError);

        public static void Friends(Action<PresenceDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Get("/api/presence/friends", onSuccess, onError);
    }

    /// <summary>친선전 룸 코드 발급 — 받은 photonRoomName 으로 Photon 룸을 만든다</summary>
    public static class RoomApi
    {
        public static void Create(string mode, Action<RoomDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Post<RoomCreateRequestDto, RoomDto>("/api/rooms", new RoomCreateRequestDto { mode = mode }, onSuccess, onError);

        public static void Get(string roomCode, Action<RoomDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Get($"/api/rooms/{roomCode}", onSuccess, onError);

        public static void Close(string roomCode, Action<RoomDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.PostEmpty($"/api/rooms/{roomCode}/close", onSuccess, onError);
    }

    /// <summary>룬 제스처 AI — 학습 데이터 업로드 / 수집 현황 / 학습된 모델 다운로드</summary>
    public static class GestureApi
    {
        /// <summary>그린 궤적 업로드. points 는 머리(카메라) 기준 좌표를 권장.</summary>
        public static void UploadSample(string skillCode, Vector3[] points, float durationSeconds, string hand,
            Action<GestureSampleDto> onSuccess, Action<string> onError = null)
        {
            float[] flat = new float[points.Length * 3];
            for (int i = 0; i < points.Length; i++)
            {
                flat[i * 3] = points[i].x;
                flat[i * 3 + 1] = points[i].y;
                flat[i * 3 + 2] = points[i].z;
            }
            var req = new GestureSampleRequestDto
            {
                label = skillCode,
                points = flat,
                durationMs = Mathf.RoundToInt(durationSeconds * 1000f),
                hand = hand,
            };
            ApiClient.Instance.Post<GestureSampleRequestDto, GestureSampleDto>("/api/gestures/samples", req, onSuccess, onError);
        }

        public static void GetStats(Action<GestureStatDto[]> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetArray("/api/gestures/stats", onSuccess, onError);

        public static void GetModelInfo(Action<ModelInfoDto> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.Get("/api/gestures/model/info", onSuccess, onError);

        /// <summary>순수 C# 추론용 가중치 JSON (GestureLstmClassifier)</summary>
        public static void DownloadWeights(Action<string> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetText("/api/gestures/model/weights", onSuccess, onError);

        /// <summary>ONNX 원본 (Sentis 사용 시)</summary>
        public static void DownloadOnnx(Action<byte[]> onSuccess, Action<string> onError = null)
            => ApiClient.Instance.GetBytes("/api/gestures/model", onSuccess, onError);
    }
}
