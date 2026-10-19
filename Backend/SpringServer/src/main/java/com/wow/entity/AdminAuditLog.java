package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;

import java.time.Instant;

/**
 * 관리자 작업 기록 (통합 DB). 누가 · 언제 · 무엇을 바꿨는지 남긴다.
 * 예) USER_DISABLE user 12 "욕설 신고 3회", NOTICE_CREATE notice 5 "점검 안내"
 */
@Entity
@Table(name = "admin_audit_log", indexes = @Index(name = "idx_audit_created", columnList = "created_at"))
@Getter
@Setter
@NoArgsConstructor
public class AdminAuditLog {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "admin_user_id")
    private Long adminUserId;

    @Column(name = "admin_email", length = 100)
    private String adminEmail;

    /** USER_DISABLE, USER_ENABLE, USER_ROLE, USER_PASSWORD_RESET, NOTICE_CREATE, MAIL_SEND, MOTION_DELETE … */
    @Column(nullable = false, length = 40)
    private String action;

    @Column(name = "target_type", length = 30)
    private String targetType;

    @Column(name = "target_id", length = 50)
    private String targetId;

    @Column(length = 500)
    private String detail;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;
}
