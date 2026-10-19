package com.wow.dto.match;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/**
 * 클라이언트(웹 UI / Unity)가 자기 경기 결과를 신고할 때 사용.
 * matchToken 을 보내면 같은 결과를 두 번 보내도 한 번만 기록된다.
 */
public record MatchRecordRequest(
        @NotBlank(message = "게임 모드(mode)가 필요합니다.") String mode,
        @NotBlank(message = "경기 결과(result)가 필요합니다.") String result,
        @Size(max = 30, message = "상대 이름이 너무 깁니다.") String opponent,
        Integer durationSec,
        Integer casts,
        Integer damageDealt,
        Integer damageTaken,
        @Size(max = 64, message = "matchToken 은 64자 이하여야 합니다.") String matchToken
) {
}
