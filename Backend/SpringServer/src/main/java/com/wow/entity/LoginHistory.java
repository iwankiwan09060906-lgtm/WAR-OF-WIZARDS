package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;

import java.time.Instant;

/**
 * 로그인 · 회원가입 · 로그아웃 기록 (통합 DB). 관리자 페이지 '로그인 기록' 에서 조회한다.
 * 실패한 로그인은 user_id 가 없을 수 있어서 입력한 이메일도 따로 남긴다.
 */
@Entity
@Table(name = "login_history", indexes = {
        @Index(name = "idx_login_user", columnList = "user_id"),
        @Index(name = "idx_login_created", columnList = "created_at")
})
@Getter
@Setter
@NoArgsConstructor
public class LoginHistory {

    /** LOGIN_SUCCESS / LOGIN_FAIL / REGISTER / LOGOUT / PASSWORD_CHANGE */
    public enum EventType { LOGIN_SUCCESS, LOGIN_FAIL, REGISTER, LOGOUT, PASSWORD_CHANGE }

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id")
    private Long userId;

    @Column(length = 100)
    private String email;

    @Enumerated(EnumType.STRING)
    @Column(name = "event_type", nullable = false, length = 20)
    private EventType eventType;

    /** 실패 사유 (비밀번호 틀림, 정지된 계정 …) */
    @Column(length = 100)
    private String reason;

    @Column(name = "ip_address", length = 45)
    private String ipAddress;

    @Column(name = "user_agent", length = 300)
    private String userAgent;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;
}
