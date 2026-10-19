package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.UpdateTimestamp;

import java.time.Instant;

/**
 * 게임 설정값 (통합 DB game_configs) — 관리자가 바꾸면 접속 중인 Unity / 웹에 즉시 전달된다.
 * config_key 는 클라이언트 규칙 이름과 같게 둔다 (예: cooldown.normal → 웹 WOW.RULES.cooldown.normal).
 * value_type: NUMBER / TEXT / BOOL
 */
@Entity
@Table(name = "game_configs")
@Getter
@Setter
@NoArgsConstructor
public class GameConfig {

    @Id
    @Column(name = "config_key", length = 60)
    private String key;

    @Column(name = "config_value", nullable = false, length = 1000)
    private String value;

    @Column(name = "value_type", nullable = false, length = 10)
    private String type;

    /** 전투 / 운영 / 이벤트 … (관리자 화면 묶음) */
    @Column(nullable = false, length = 20)
    private String category;

    @Column(length = 200)
    private String description;

    /** 바뀔 때마다 1씩 증가 — 클라이언트가 캐시를 비교할 때 사용 */
    @Column(nullable = false)
    private long version = 1;

    @Column(name = "updated_by", length = 100)
    private String updatedBy;

    @UpdateTimestamp
    @Column(name = "updated_at", nullable = false)
    private Instant updatedAt;

    public GameConfig(String key, String value, String type, String category, String description) {
        this.key = key;
        this.value = value;
        this.type = type;
        this.category = category;
        this.description = description;
    }
}
