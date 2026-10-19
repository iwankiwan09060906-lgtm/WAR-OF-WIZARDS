package com.wow.dto.auth;

import jakarta.validation.constraints.Email;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/**
 * 회원가입. username 을 비우면 email 을 로그인 아이디로 사용한다.
 * (예전 Unity AuthApi 가 보내던 favoriteElement 등 모르는 필드는 무시된다)
 */
public record RegisterRequest(
        @NotBlank(message = "이메일을 입력해주세요.")
        @Email(message = "올바른 이메일 주소를 입력해주세요.")
        @Size(max = 100, message = "이메일이 너무 깁니다.")
        String email,

        @NotBlank(message = "비밀번호를 입력해주세요.")
        @Size(min = 6, max = 100, message = "비밀번호는 6자 이상이어야 합니다.")
        String password,

        @NotBlank(message = "닉네임을 입력해주세요.")
        String nickname,

        @Size(max = 100, message = "아이디가 너무 깁니다.")
        String username
) {
}
