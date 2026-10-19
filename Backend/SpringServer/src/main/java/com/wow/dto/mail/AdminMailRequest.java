package com.wow.dto.mail;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/** 운영 우편 발송. toPlayerId 가 null 이면 봇을 제외한 전체 플레이어에게 보낸다. */
public record AdminMailRequest(
        Long toPlayerId,
        @NotBlank(message = "제목을 입력해주세요.") @Size(max = 100, message = "제목은 100자 이하여야 합니다.") String title,
        @Size(max = 1000, message = "내용은 1000자 이하여야 합니다.") String body,
        @Size(max = 50, message = "보상 문구는 50자 이하여야 합니다.") String reward
) {
}
