package com.wow.dto.gesture;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;

import java.util.List;

/**
 * Unity 에서 그린 룬 궤적 업로드.
 * points = [x0,y0,z0, x1,y1,z1, ...] (점 4~1024개), label = 스킬 코드 (예: "S03")
 */
public record GestureSampleRequest(
        @NotBlank(message = "label(스킬 코드)이 필요합니다.") String label,
        @NotNull(message = "points 가 필요합니다.") List<Float> points,
        Integer durationMs,
        String hand
) {
}
