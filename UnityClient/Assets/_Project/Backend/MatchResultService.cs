// §23.4 전적 저장 흐름 · C13 전투 결과 · C03 마이페이지 전적
//
// 두 가지 저장 경로:
//   1) 매칭 큐로 잡힌 경기 (matchId 있음) → Join / ReportResult / Leave  (서버 게임 방 match_sessions, 랭킹전 레이팅 ±)
//   2) 그 밖의 경기 (봇 연습 · 친선전)    → Record(mode = "bot" / "friendly")  (레이팅 변화 없음)
// MatchResultReporter 컴포넌트가 IGameEvents.OnMatchEnded 를 받아 자동으로 둘 중 하나를 부른다.

using System;
using SpellboundVR.Contracts;

namespace SpellboundVR.Backend
{
    public static class MatchResultService
    {
        public const string ModeRanked = "ranked";
        public const string ModeFriendly = "friendly";
        public const string ModeBot = "bot";

        /// <summary>계약서 MatchResult → 서버 문자열. Void(무효 경기)는 null = 저장하지 않음</summary>
        public static string ToServerResult(MatchResult result)
        {
            switch (result)
            {
                case MatchResult.Win: return "WIN";
                case MatchResult.Lose: return "LOSE";
                case MatchResult.Draw: return "DRAW";
                default: return null;
            }
        }

        public static void Record(MatchRecordRequestDto record, Action<MatchRecordResultDto> onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.Post<MatchRecordRequestDto, MatchRecordResultDto>("/api/matches", record, onSuccess, onError);

        public static void GetHistory(long playerId, int limit, Action<MatchHistoryDto[]> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.GetArray($"/api/matches/player/{playerId}?limit={limit}", onSuccess, onError);

        // ── 매칭 게임 방 ──────────────────────────────────────

        /// <summary>매칭 후 내가 아직 안 끝낸 게임 방 (없으면 match == null)</summary>
        public static void GetCurrent(Action<MatchSessionDto> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.Get<MatchSessionResponseDto>("/api/match/current", res => onSuccess?.Invoke(res.match), onError);

        /// <summary>Unity 가 이 경기에 들어왔음 (둘 다 들어오면 PLAYING, 봇 상대는 혼자여도 PLAYING)</summary>
        public static void Join(long matchId, Action<MatchSessionDto> onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.PostEmpty<MatchSessionResponseDto>($"/api/match/{matchId}/join", res => onSuccess?.Invoke(res.match), onError);

        /// <summary>내 결과 보고 → 전적 + 레이팅 반영 (각자 한 번씩)</summary>
        public static void ReportResult(long matchId, MatchResultRequestDto result, Action<MatchSessionDto> onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.Post<MatchResultRequestDto, MatchSessionResponseDto>($"/api/match/{matchId}/result", result,
                res => onSuccess?.Invoke(res.match), onError);

        /// <summary>시작 전 취소 / 연결 끊김</summary>
        public static void Leave(long matchId, Action<MatchSessionDto> onSuccess = null, Action<string> onError = null)
            => BackendClient.Instance.PostEmpty<MatchSessionResponseDto>($"/api/match/{matchId}/leave", res => onSuccess?.Invoke(res.match), onError);
    }
}
