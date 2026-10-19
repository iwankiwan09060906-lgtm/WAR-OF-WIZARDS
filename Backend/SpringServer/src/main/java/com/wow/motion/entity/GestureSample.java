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
 * 룬(제스처) 학습 데이터 1개. Unity 가 그린 3D 궤적을 올리고, Python(AI/Tools/fetch_dataset.py)이 내려받아 학습한다.
 * points_json = [x0,y0,z0,x1,y1,z1,...] (Unity JsonUtility 가 2차원 배열을 못 다뤄서 1차원으로 저장)
 *
 * 모션 DB(WOW_MOTION) 에 저장된다. 통합 DB 의 players 와는 외래키 없이 player_id 숫자로만 연결.
 */
@Entity
@Table(name = "gesture_samples", indexes = {
        @Index(name = "idx_gesture_label", columnList = "label"),
        @Index(name = "idx_gesture_player", columnList = "player_id")
})
@Getter
@Setter
@NoArgsConstructor
public class GestureSample {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    /** 통합 DB players.id (Python 이 올린 샘플은 null) */
    @Column(name = "player_id")
    private Long playerId;

    /** 스킬 코드 (S01~S13, H01~H06) = 학습 클래스 */
    @Column(nullable = false, length = 10)
    private String label;

    @Column(name = "point_count", nullable = false)
    private int pointCount;

    // 긴 문자열: Oracle clob / PostgreSQL text / H2 varchar
    @JdbcTypeCode(SqlTypes.LONG32VARCHAR)
    @Column(name = "points_json", nullable = false)
    private String pointsJson;

    @Column(name = "duration_ms", nullable = false)
    private int durationMs;

    @Column(length = 10)
    private String hand;

    @Column(nullable = false, length = 20)
    private String source;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;
}
