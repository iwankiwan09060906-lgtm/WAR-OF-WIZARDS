package com.wow.dto.auth;

import jakarta.validation.constraints.NotBlank;

/**
 * 로그인. email 또는 usernameOrEmail 중 하나에 아이디(이메일)를 넣는다.
 */
public record LoginRequest(
        String email,
        String usernameOrEmail,
        @NotBlank(message = "비밀번호를 입력해주세요.")
        String password
) {

    public String identifier() {
        if (usernameOrEmail != null && !usernameOrEmail.isBlank()) {
            return usernameOrEmail.trim();
        }
        return email == null ? "" : email.trim();
    }
}
