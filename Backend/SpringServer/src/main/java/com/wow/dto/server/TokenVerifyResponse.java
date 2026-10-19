package com.wow.dto.server;

public record TokenVerifyResponse(boolean valid, Long playerId, String nickname, int rating) {

    public static TokenVerifyResponse invalid() {
        return new TokenVerifyResponse(false, null, null, 0);
    }
}
