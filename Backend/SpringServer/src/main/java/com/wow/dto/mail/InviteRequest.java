package com.wow.dto.mail;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;

public record InviteRequest(
        @NotNull(message = "초대할 친구를 선택해주세요.") Long friendId,
        @NotBlank(message = "룸 코드가 필요합니다.") String roomCode
) {
}
