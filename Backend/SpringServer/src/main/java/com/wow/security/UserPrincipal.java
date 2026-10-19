package com.wow.security;

/**
 * 로그인한 사용자. 컨트롤러에서 @AuthenticationPrincipal UserPrincipal me 로 받는다.
 */
public record UserPrincipal(Long userId, Long playerId, String username, String role) {
}
