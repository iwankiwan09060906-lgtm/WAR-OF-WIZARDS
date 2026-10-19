package com.wow.dto.gesture;

import java.time.Instant;
import java.util.List;

/** 샘플 정보. points 는 Python 내보내기(export)에서만 채운다. */
public record GestureSampleResponse(
        Long id,
        String label,
        int pointCount,
        int durationMs,
        String hand,
        Long playerId,
        Instant createdAt,
        List<Float> points
) {
}
