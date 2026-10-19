package com.wow.dto.player;

public record RankEntryResponse(
        int rank,
        Long playerId,
        String nickname,
        int rating,
        int wins,
        int losses,
        double winRate,
        String tier,
        boolean bot
) {
}
