package com.wow.entity;

import com.wow.common.ApiException;

import java.util.Locale;

public enum MatchOutcome {
    WIN,
    LOSE,
    DRAW;

    public static MatchOutcome from(String value) {
        if (value == null || value.isBlank()) {
            throw ApiException.badRequest("경기 결과(result)가 필요합니다.");
        }
        try {
            return valueOf(value.trim().toUpperCase(Locale.ROOT));
        } catch (IllegalArgumentException ex) {
            throw ApiException.badRequest("잘못된 경기 결과입니다: " + value);
        }
    }

    public String lower() {
        return name().toLowerCase(Locale.ROOT);
    }
}
