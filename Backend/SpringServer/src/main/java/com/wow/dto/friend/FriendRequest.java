package com.wow.dto.friend;

import jakarta.validation.constraints.NotBlank;

public record FriendRequest(@NotBlank(message = "닉네임을 입력해주세요.") String nickname) {
}
