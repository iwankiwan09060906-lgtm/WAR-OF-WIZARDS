package com.wow.dto.motion;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;

import java.util.List;

/**
 * VR 손 모션 업로드 (Unity → POST /api/motions)
 * frames = 프레임×관절×xyz 를 1차원으로 이어 붙인 배열, 길이 = frameCount × jointCount × 3
 */
public record MotionRecordingRequest(
        @NotBlank(message = "label 이 필요합니다.") @Size(max = 30, message = "label 은 30자 이하여야 합니다.") String label,
        @Size(max = 10) String hand,
        Integer fps,
        @NotNull(message = "jointCount 가 필요합니다.") Integer jointCount,
        @NotNull(message = "frames 가 필요합니다.") List<Float> frames,
        Integer durationMs,
        @Size(max = 30) String device,
        @Size(max = 64) String matchToken
) {
}
