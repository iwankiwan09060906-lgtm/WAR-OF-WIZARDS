package com.wow.dto.auth;

/** 비밀번호 변경 (PUT /api/auth/password). 검증은 AuthService.changePassword 에서 한다. */
public record PasswordChangeRequest(String currentPassword, String newPassword) {
}
