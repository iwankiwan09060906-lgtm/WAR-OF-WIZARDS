package com.wow.common;

import lombok.Getter;
import org.springframework.http.HttpStatus;

/**
 * 서비스 계층에서 던지는 예외. GlobalExceptionHandler 가 {status, message} JSON 으로 바꿔 응답한다.
 * message 는 웹 UI / Unity 화면에 그대로 표시되므로 한글 문장으로 작성한다.
 */
@Getter
public class ApiException extends RuntimeException {

    private final HttpStatus status;

    public ApiException(HttpStatus status, String message) {
        super(message);
        this.status = status;
    }

    public static ApiException badRequest(String message) {
        return new ApiException(HttpStatus.BAD_REQUEST, message);
    }

    public static ApiException unauthorized(String message) {
        return new ApiException(HttpStatus.UNAUTHORIZED, message);
    }

    public static ApiException forbidden(String message) {
        return new ApiException(HttpStatus.FORBIDDEN, message);
    }

    public static ApiException notFound(String message) {
        return new ApiException(HttpStatus.NOT_FOUND, message);
    }

    public static ApiException conflict(String message) {
        return new ApiException(HttpStatus.CONFLICT, message);
    }
}
