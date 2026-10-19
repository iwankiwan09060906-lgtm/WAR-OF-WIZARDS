package com.wow.dto.auth;

public record NicknameCheckResponse(String nickname, boolean available, String message) {
}
