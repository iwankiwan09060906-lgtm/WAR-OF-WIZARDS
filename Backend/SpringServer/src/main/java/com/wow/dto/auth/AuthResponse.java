package com.wow.dto.auth;

/**
 * 로그인/가입/세션 확인 응답. accessToken 을 Authorization: Bearer {accessToken} 으로 보내면 된다.
 * Unity JsonUtility 로 바로 읽을 수 있도록 중첩 없이 평평하게 구성.
 */
public record AuthResponse(
        String tokenType,
        String accessToken,
        long expiresIn,
        Long userId,
        Long playerId,
        String username,
        String email,
        String nickname,
        int rating,
        String role
) {
}
