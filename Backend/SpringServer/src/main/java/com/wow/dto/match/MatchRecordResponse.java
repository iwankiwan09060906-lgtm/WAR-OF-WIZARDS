package com.wow.dto.match;

import java.time.Instant;

/** 전적 한 줄 (웹 UI MatchRecord 와 같은 모양 + 상세 수치) */
public record MatchRecordResponse(
        Long id,
        String matchToken,
        String mode,
        String result,
        String opponent,
        int durationSec,
        int casts,
        int damageDealt,
        int damageTaken,
        int ratingBefore,
        int ratingAfter,
        int ratingDelta,
        Instant endedAt
) {
}
