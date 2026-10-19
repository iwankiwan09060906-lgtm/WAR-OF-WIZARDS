package com.wow.entity;

import com.wow.common.ApiException;

import java.util.Locale;

/** 랭크전 / 친선전 / 봇 연습전 */
public enum MatchMode {
    RANKED,
    FRIENDLY,
    BOT;

    /** "ranked", "RANKED" 모두 허용. 비어 있으면 기본값. */
    public static MatchMode from(String value, MatchMode defaultMode) {
        if (value == null || value.isBlank()) {
            return defaultMode;
        }
        try {
            return valueOf(value.trim().toUpperCase(Locale.ROOT));
        } catch (IllegalArgumentException ex) {
            throw ApiException.badRequest("잘못된 게임 모드입니다: " + value);
        }
    }

    public String lower() {
        return name().toLowerCase(Locale.ROOT);
    }
}
