package com.wow.dto.matchmaking;

/**
 * 매칭 상태.
 * state: none(대기열에 없음) / searching(찾는 중) / matched(매칭 완료)
 * range: 현재 허용 레이팅 차이 (±), botTimeoutSec: 이 시간이 지나면 봇 매칭
 * matched 일 때 opponent* 가 채워진다. 봇이면 opponentPlayerId = null, bot = true.
 */
public record MatchmakingStatus(
        String state,
        int waitedSec,
        int range,
        int queueSize,
        int botTimeoutSec,
        Long opponentPlayerId,
        String opponentNickname,
        Integer opponentRating,
        boolean bot,
        String matchId
) {
}
