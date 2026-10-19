package com.wow.dto.match;

import java.util.List;

/** 서버 판정 결과 기록 응답. duplicate=true 면 이미 기록된 경기(재전송)였다는 뜻. */
public record MatchReportResponse(
        Long matchId,
        String matchToken,
        String mode,
        boolean duplicate,
        List<PlayerResult> players
) {

    public record PlayerResult(
            Long playerId,
            String nickname,
            String result,
            int ratingBefore,
            int ratingAfter,
            int ratingDelta
    ) {
    }
}
