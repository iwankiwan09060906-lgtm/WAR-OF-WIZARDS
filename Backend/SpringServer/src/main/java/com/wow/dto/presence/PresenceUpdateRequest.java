package com.wow.dto.presence;

import jakarta.validation.constraints.NotBlank;

/** status: online / in_game / offline, roomCode: 게임 중인 룸 (선택) */
public record PresenceUpdateRequest(@NotBlank(message = "status 가 필요합니다.") String status, String roomCode) {
}
