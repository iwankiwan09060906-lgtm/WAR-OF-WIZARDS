package com.wow.motion.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;
import org.hibernate.annotations.JdbcTypeCode;
import org.hibernate.type.SqlTypes;

import java.time.Instant;

/**
 * VR 손 모션 녹화 1건 (Quest 3 핸드 트래킹). 모션 DB(WOW_MOTION) 에 저장.
 *
 * frames_json: 프레임마다 관절 좌표를 이어 붙인 1차원 배열
 *   [f0_j0_x, f0_j0_y, f0_j0_z, f0_j1_x, ... , f1_j0_x, ...]
 *   → 전체 길이 = frameCount × jointCount × 3
 * 룬 궤적(gesture_samples)보다 훨씬 크다: 26관절 × 72프레임 ≈ 5,600개 숫자.
 */
@Entity
@Table(name = "motion_recordings", indexes = {
        @Index(name = "idx_motion_player", columnList = "player_id"),
        @Index(name = "idx_motion_label", columnList = "label")
})
@Getter
@Setter
@NoArgsConstructor
public class MotionRecording {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    /** 통합 DB players.id (외래키 없음) */
    @Column(name = "player_id")
    private Long playerId;

    /** 무엇을 한 동작인지: 스킬 코드(S01~H06) 또는 FIST / POINT / SNAP_LEFT 같은 제스처 이름 */
    @Column(nullable = false, length = 30)
    private String label;

    /** left / right / both */
    @Column(nullable = false, length = 10)
    private String hand;

    @Column(name = "frame_count", nullable = false)
    private int frameCount;

    @Column(name = "joint_count", nullable = false)
    private int jointCount;

    @Column(nullable = false)
    private int fps;

    @Column(name = "duration_ms", nullable = false)
    private int durationMs;

    @JdbcTypeCode(SqlTypes.LONG32VARCHAR)
    @Column(name = "frames_json", nullable = false)
    private String framesJson;

    /** 녹화 기기 (quest3, pc, webgl …) */
    @Column(length = 30)
    private String device;

    /** 전투 중 녹화라면 매치 토큰 (선택) */
    @Column(name = "match_token", length = 64)
    private String matchToken;

    @Column(nullable = false, length = 20)
    private String source;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;
}
