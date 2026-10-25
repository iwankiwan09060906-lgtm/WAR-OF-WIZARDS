// 서버(Backend/wow_server.py) JSON 응답/요청 형식. 필드 이름은 서버 JSON 키와 같아야 JsonUtility 가 읽는다.
// 서버가 null 을 보내는 숫자 필드는 0 으로 읽힌다.

using System;

namespace SpellboundVR.Backend
{
    [Serializable] public class ErrorDto { public int status; public string message; }
    [Serializable] public class MessageDto { public string message; public string status; }
    [Serializable] public class PasswordResetRequestDto { public string email; }
    [Serializable] public class HealthDto { public string status; public string server; public string version; public string time; }

    // ── 인증 ─────────────────────────────────────────────

    [Serializable] public class LoginRequestDto { public string email; public string password; }
    [Serializable] public class RegisterRequestDto { public string email; public string password; public string nickname; }
    [Serializable] public class GuestRequestDto { public string nickname; }
    /// <summary>/api/auth/google — credential = Google id_token, accessToken = Google access_token (둘 중 하나 이상)</summary>
    [Serializable] public class GoogleLoginRequestDto { public string credential; public string accessToken; }
    [Serializable] public class PasswordChangeRequestDto { public string currentPassword; public string newPassword; }

    [Serializable]
    public class AuthResponseDto
    {
        public string accessToken;
        public long userId;
        public string email;
        /// <summary>ROLE_USER · ROLE_ADMIN</summary>
        public string role;
        public long playerId;
        public string nickname;
        public bool guest;
    }

    [Serializable] public class NicknameCheckDto { public bool available; }

    // ── 프로필 · 랭킹 ─────────────────────────────────────

    /// <summary>GET /api/players/me · /api/players/{id}</summary>
    [Serializable]
    public class PlayerDto
    {
        public long id;
        public long user_id;
        public string nickname;
        public int rating;
        public int wins;
        public int losses;
        public int draws;
        public string created_at;

        public int TotalGames => wins + losses + draws;
        public float WinRate => TotalGames > 0 ? (float)wins / TotalGames : 0f;
    }

    [Serializable] public class NicknameUpdateDto { public string nickname; }

    /// <summary>GET /api/players/ranking (매칭 대전 기준, 관리자 제외)</summary>
    [Serializable]
    public class RankEntryDto
    {
        public long id;
        public string nickname;
        public int rating;
        public int wins;
        public int losses;
        public int draws;
        public int rank;
    }

    // ── 스킬 · 덱 ─────────────────────────────────────────

    /// <summary>GET /api/skills — 19종. code: S01~S13 · H01~H06, id: 계약서 SpellId (1~13 · 101~106)</summary>
    [Serializable]
    public class SkillDto
    {
        public int id;
        public string code;
        public string name;
        public string name_en;
        /// <summary>Normal · HighPower</summary>
        public string tier;
        public string targeting;
        public float cooldown;
        public float damage;
        public float heal;
        public string description;

        public bool IsHighPower => tier == "HighPower";
    }

    [Serializable] public class DeckDto { public long playerId; public string[] skills; }
    [Serializable] public class DeckRequestDto { public string[] skills; }

    // ── 전적 · 게임 방 ────────────────────────────────────

    /// <summary>POST /api/matches — 매칭 게임 방(matchId) 밖에서 치른 경기 (봇 연습 · 친선전)</summary>
    [Serializable]
    public class MatchRecordRequestDto
    {
        /// <summary>ranked · friendly · bot (ranked 만 레이팅 반영)</summary>
        public string mode;
        /// <summary>WIN · LOSE · DRAW</summary>
        public string result;
        public string opponent;
        public float durationSec;
        public int casts;
        public float damageDealt;
        public float damageTaken;
    }

    /// <summary>POST /api/match/{id}/result</summary>
    [Serializable]
    public class MatchResultRequestDto
    {
        public string result;
        public float durationSec;
        public int casts;
        public float damageDealt;
        public float damageTaken;
    }

    [Serializable] public class MatchRecordResultDto { public long matchId; public string result; public int ratingDelta; public int newRating; }

    /// <summary>GET /api/matches/player/{id}</summary>
    [Serializable]
    public class MatchHistoryDto
    {
        public long id;
        public string mode;
        public string result;
        public string opponent;
        public float durationSec;
        public int casts;
        public float damageDealt;
        public float damageTaken;
        public int ratingDelta;
        public string recordedAt;
    }

    [Serializable] public class MatchPlayerDto { public long playerId; public string nickname; public int rating; public bool bot; }
    [Serializable] public class MatchUnityDto { public bool me; public bool opponent; }

    /// <summary>게임 방(match_sessions) — 나 기준</summary>
    [Serializable]
    public class MatchSessionDto
    {
        public long matchId;
        public string roomCode;
        public string mode;
        /// <summary>MATCHED · PLAYING · ENDED · CANCELLED</summary>
        public string status;
        public MatchPlayerDto me;
        public MatchPlayerDto opponent;
        public MatchUnityDto unity;
        public string result;
        public int ratingDelta;
        public string opponentResult;
    }

    [Serializable] public class MatchSessionResponseDto { public MatchSessionDto match; }

    // ── 매칭 큐 ───────────────────────────────────────────

    /// <summary>GET /api/matchmaking/status</summary>
    [Serializable]
    public class MatchmakingStatusDto
    {
        /// <summary>none · searching · matched</summary>
        public string state;
        public int elapsedSeconds;
        public int ratingWindow;
        public float botTimeout;
        public int queueSize;

        public string opponentNickname;
        public int opponentRating;
        public long opponentPlayerId;
        public bool bot;
        public string roomCode;
        public long matchId;

        public bool IsMatched => state == "matched";
        public bool IsSearching => state == "searching";
    }

    // ── 친선전 방 ─────────────────────────────────────────

    [Serializable] public class RoomCreateRequestDto { public string code; }
    [Serializable] public class RoomCodeDto { public string code; public string roomCode; public string message; }

    /// <summary>GET /api/rooms/{code}</summary>
    [Serializable]
    public class RoomDto
    {
        public long id;
        public string code;
        public long host_player_id;
        public long guest_player_id;
        public string mode;
        /// <summary>WAITING · READY · PLAYING · CLOSED</summary>
        public string status;
        public int guest_ready;
        public string hostNickname;
        public int hostRating;
        public string guestNickname;
        public int guestRating;

        public bool HasGuest => guest_player_id != 0;
        public bool GuestReady => guest_ready != 0;
    }

    [Serializable] public class ReadyDto { public bool ready; }

    // ── 친구 · 접속 ───────────────────────────────────────

    /// <summary>GET /api/friends(?all=1)</summary>
    [Serializable]
    public class FriendDto
    {
        public long id;
        public long friendPlayerId;
        public string nickname;
        public int rating;
        /// <summary>PENDING · ACCEPTED</summary>
        public string status;
        /// <summary>friend · incoming(받은 요청) · outgoing(보낸 요청)</summary>
        public string relation;
        /// <summary>데모 NPC 마법사 (랭킹 · 친구 화면 채우기용, 로그인 불가)</summary>
        public bool npc;
    }

    [Serializable] public class FriendRequestDto { public string nickname; }
    [Serializable] public class FriendRespondDto { public long playerId; public bool accept; }
    [Serializable] public class FriendRespondResultDto { public bool accepted; }
    [Serializable] public class OnlineDto { public long[] online; }

    // ── 우편 · 공지 ───────────────────────────────────────

    /// <summary>GET /api/mailbox — 우편과 공지를 한 목록으로. id: "m12"(우편) · "n3"(공지)</summary>
    [Serializable]
    public class MailboxItemDto
    {
        public string id;
        /// <summary>mail · invite · notice</summary>
        public string kind;
        public string title;
        public string body;
        public string from;
        public string roomCode;
        public bool read;
        public string createdAt;

        public bool IsNotice => kind == "notice";
        public bool IsInvite => kind == "invite";
    }

    [Serializable] public class MailboxReadRequestDto { public string id; }
    [Serializable] public class MailboxReadResultDto { public bool read; }
    [Serializable] public class UnreadCountDto { public int count; }
    [Serializable] public class InviteRequestDto { public long friendId; public string roomCode; }

    /// <summary>GET /api/notices</summary>
    [Serializable]
    public class NoticeDto
    {
        public long id;
        public string title;
        public string content;
        public string author_name;
        public string created_at;
    }
}
