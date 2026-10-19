package com.wow.dto.player;

import java.time.Instant;

public record ProfileResponse(
        Long id,
        Long userId,
        String nickname,
        int rating,
        int wins,
        int losses,
        int draws,
        double winRate,
        int level,
        int experience,
        String tier,
        boolean bot,
        boolean online,
        Instant createdAt
) {
}
