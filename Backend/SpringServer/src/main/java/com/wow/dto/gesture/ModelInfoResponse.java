package com.wow.dto.gesture;

import java.time.Instant;
import java.util.List;

/**
 * 제스처 인식 모델 정보. Unity 는 sha256 이 바뀌었을 때만 새로 받으면 된다.
 * - ONNX (Sentis 용)          : downloadUrl
 * - 가중치 JSON (순수 C# 추론) : weightsUrl  ← Unity 기본 경로 (Quest / PC / WebGL 공통)
 * classes[i] = 모델 출력 i 번째가 뜻하는 스킬 코드, 입력 모양 = inputShape (배치, 점 64개, xyz)
 */
public record ModelInfoResponse(
        boolean available,
        String fileName,
        long sizeBytes,
        String sha256,
        Instant updatedAt,
        String downloadUrl,
        boolean weightsAvailable,
        String weightsSha256,
        String weightsUrl,
        List<Integer> inputShape,
        List<String> classes
) {
}
