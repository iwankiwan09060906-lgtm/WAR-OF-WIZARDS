package com.wow.dto.mail;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

public record NoticeRequest(
        @NotBlank(message = "제목을 입력해주세요.") @Size(max = 100, message = "제목은 100자 이하여야 합니다.") String title,
        @NotBlank(message = "내용을 입력해주세요.") @Size(max = 1000, message = "내용은 1000자 이하여야 합니다.") String body
) {
}
