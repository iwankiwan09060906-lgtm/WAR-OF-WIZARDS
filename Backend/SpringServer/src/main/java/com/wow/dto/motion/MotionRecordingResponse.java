package com.wow.dto.motion;

import java.time.Instant;
import java.util.List;

/** frames 는 Python 내보내기(/api/server/motions)에서만 채운다. 목록 조회에서는 null. */
public record MotionRecordingResponse(
        Long id,
        Long playerId,
        String label,
        String hand,
        int frameCount,
        int jointCount,
        int fps,
        int durationMs,
        String device,
        String source,
        Instant createdAt,
        List<Float> frames
) {
}
