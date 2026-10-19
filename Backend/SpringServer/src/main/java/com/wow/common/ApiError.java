package com.wow.common;

import org.springframework.http.HttpStatus;

import java.time.Instant;

/**
 * 모든 오류 응답의 모양. 클라이언트는 message 만 읽어서 보여주면 된다.
 */
public record ApiError(Instant timestamp, int status, String error, String message, String path) {

    public static ApiError of(HttpStatus status, String message, String path) {
        return new ApiError(Instant.now(), status.value(), status.getReasonPhrase(), message, path);
    }
}
