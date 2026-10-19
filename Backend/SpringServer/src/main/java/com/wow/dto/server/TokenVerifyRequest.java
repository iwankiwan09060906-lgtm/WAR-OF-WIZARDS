package com.wow.dto.server;

import jakarta.validation.constraints.NotBlank;

public record TokenVerifyRequest(@NotBlank(message = "token 이 필요합니다.") String token) {
}
