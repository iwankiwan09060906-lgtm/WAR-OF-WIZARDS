package com.wow.motion.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;

import java.time.Instant;

/**
 * AI 제스처 모델 업로드 이력 (Python train.py --upload 할 때마다 1줄). 모션 DB(WOW_MOTION).
 * 모델 파일 자체는 서버 디스크(wow.ai.model-path)에 있고, 여기는 버전 기록만 남긴다.
 */
@Entity
@Table(name = "gesture_models")
@Getter
@Setter
@NoArgsConstructor
public class GestureModel {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "file_name", nullable = false, length = 200)
    private String fileName;

    /** onnx / weights */
    @Column(nullable = false, length = 10)
    private String kind;

    @Column(name = "size_bytes", nullable = false)
    private long sizeBytes;

    @Column(length = 64)
    private String sha256;

    /** 업로드 시점의 학습 샘플 수 */
    @Column(name = "sample_count", nullable = false)
    private long sampleCount;

    @CreationTimestamp
    @Column(name = "uploaded_at", nullable = false, updatable = false)
    private Instant uploadedAt;
}
