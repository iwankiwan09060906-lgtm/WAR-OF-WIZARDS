using System;

// Spring 서버 JSON 과 1:1 로 맞춘 데이터 모양 (필드 이름이 JSON 키와 같아야 JsonUtility 가 읽는다)
namespace WarOfWizards.API
{
    // ── 공통 ──────────────────────────────────────────────────────────────
    [Serializable] public class ApiErrorDto { public int status; public string error; public string message; public string path; }
    [Serializable] public class ActionResultDto { public bool ok; public string message; }
    [Serializable] public class HealthDto { public string status; public string service; public string[] db; public string time; }

    // ── 인증 ──────────────────────────────────────────────────────────────
    [Serializable] public class RegisterRequestDto { public string email; public string password; public string nickname; public string username; }
    [Serializable] public class LoginRequestDto { public string email; public string password; }

    [Serializable]
    public class AuthResponseDto
    {
        public string tokenType;
        public string accessToken;
        public long expiresIn;
        public long userId;
        public long playerId;
        public string username;
        public string email;
        public string nickname;
        public int rating;
        public string role;
    }

    [Serializable] public class NicknameCheckDto { public string nickname; public bool available; public string message; }

    // ── 플레이어 / 랭킹 ───────────────────────────────────────────────────
    [Serializable]
    public class ProfileDto
    {
        public long id;
        public long userId;
        public string nickname;
        public int rating;
        public int wins;
        public int losses;
        public int draws;
        public double winRate;
        public int level;
        public int experience;
        public string tier;
        public bool bot;
        public bool online;
        public string createdAt;
    }

    [Serializable] public class NicknameUpdateDto { public string nickname; }

    [Serializable]
    public class RankEntryDto
    {
        public int rank;
        public long playerId;
        public string nickname;
        public int rating;
        public int wins;
        public int losses;
        public double winRate;
        public string tier;
        public bool bot;
    }

    // ── 스킬 / 마법서 ─────────────────────────────────────────────────────
    [Serializable]
    public class SkillDto
    {
        public string code;          // S01~S13, H01~H06
        public string name;
        public string tier;          // normal / high
        public string kind;          // projectile / area / beam / buff / summon / install / global
        public string effect;
        public string color;         // "#f97316"
        public int cooldownSeconds;  // 5 / 20
        public int usesPerGame;      // 0 = 무제한, 3 = 고위력
        public int baseDamage;
    }

    [Serializable] public class DeckDto { public long playerId; public string[] skills; public bool complete; }
    [Serializable] public class DeckRequestDto { public string[] skills; }

    // ── 전적 ──────────────────────────────────────────────────────────────
    [Serializable]
    public class MatchRecordRequestDto
    {
        public string mode;       // ranked / friendly / bot
        public string result;     // win / lose / draw
        public string opponent;
        public int durationSec;
        public int casts;
        public int damageDealt;
        public int damageTaken;
        public string matchToken; // 같은 결과 재전송 방지용 (선택)
    }

    [Serializable]
    public class MatchRecordDto
    {
        public long id;
        public string matchToken;
        public string mode;
        public string result;
        public string opponent;
        public int durationSec;
        public int casts;
        public int damageDealt;
        public int damageTaken;
        public int ratingBefore;
        public int ratingAfter;
        public int ratingDelta;
        public string endedAt;
    }

    /// <summary>Photon(방장) / 전용 서버가 보내는 경기 결과. winnerPlayerId = 0 이면 무승부.</summary>
    [Serializable]
    public class ServerMatchReportDto
    {
        public string matchToken;
        public string mode;
        public string roomCode;
        public long winnerPlayerId;
        public int durationSec;
        public MatchPlayerStatDto[] players;
    }

    [Serializable] public class MatchPlayerStatDto { public long playerId; public int casts; public int damageDealt; public int damageTaken; }

    // ── 친구 / 접속 상태 ──────────────────────────────────────────────────
    [Serializable]
    public class FriendDto
    {
        public long id;
        public string nickname;
        public int rating;
        public string relation;   // friend / incoming / outgoing
        public bool online;
        public string status;     // online / in_game / offline
    }

    [Serializable] public class FriendRequestDto { public string nickname; }
    [Serializable] public class PresenceDto { public string status; public long[] onlineFriendIds; }

    // ── 우편 / 초대 / 룸 ──────────────────────────────────────────────────
    [Serializable]
    public class MailDto
    {
        public string id;         // "mail-12" / "notice-3"
        public string kind;       // mail / invite / notice
        public string title;
        public string body;
        public string from;
        public string roomCode;
        public string reward;
        public bool read;
        public string createdAt;
    }

    [Serializable] public class UnreadCountDto { public long count; }
    [Serializable] public class InviteRequestDto { public long friendId; public string roomCode; }
    [Serializable] public class RoomCreateRequestDto { public string mode; }

    [Serializable]
    public class RoomDto
    {
        public string roomCode;
        public string photonRoomName;
        public string mode;
        public string status;     // open / in_game / closed
        public long hostPlayerId;
        public string hostNickname;
        public int maxPlayers;
        public string createdAt;
    }

    // ── 제스처 AI ─────────────────────────────────────────────────────────
    [Serializable]
    public class GestureSampleRequestDto
    {
        public string label;      // 스킬 코드
        public float[] points;    // x0,y0,z0,x1,y1,z1,...
        public int durationMs;
        public string hand;       // left / right
    }

    [Serializable] public class GestureSampleDto { public long id; public string label; public int pointCount; public int durationMs; public string hand; public long playerId; public string createdAt; }
    [Serializable] public class GestureStatDto { public int classIndex; public string label; public string name; public long count; }

    [Serializable]
    public class ModelInfoDto
    {
        public bool available;
        public string fileName;
        public long sizeBytes;
        public string sha256;
        public string updatedAt;
        public string downloadUrl;
        public bool weightsAvailable;
        public string weightsSha256;
        public string weightsUrl;
        public int[] inputShape;
        public string[] classes;
    }
}
