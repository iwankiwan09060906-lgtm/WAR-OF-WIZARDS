package com.wow.dto.admin;

import java.time.Instant;
import java.util.List;
import java.util.Map;

/**
 * 관리자 API 응답/요청 모음 (한 파일에 모아 둠).
 * 필요하면 파일을 나눠도 된다: AdminDtos.MemberRow → MemberRow.java
 */
public final class AdminDtos {

    private AdminDtos() {
    }

    /** DB 1개 상태: 연결 여부, 제품, 계정, 테이블별 행 수 */
    public record DbStatus(String name, String role, boolean up, String product, String url, String username,
                           Map<String, Long> tables, String error) {
    }

    public record Dashboard(List<DbStatus> databases, long members, long todayLogins, long todaySignups,
                            long todayLoginFails, long onlineNow, long matches, long gestureSamples, long motionRecordings) {
    }

    public record MemberRow(Long userId, Long playerId, String email, String nickname, String role, boolean active,
                            int rating, int wins, int losses, Instant createdAt, Instant lastLoginAt) {
    }

    public record MemberPage(List<MemberRow> items, int page, int size, long total) {
    }

    public record MemberDetail(MemberRow member, long matches, long gestureSamples, long motionRecordings,
                               List<LoginRow> recentLogins) {
    }

    public record LoginRow(Long id, Long userId, String email, String eventType, String reason, String ipAddress,
                           String userAgent, Instant createdAt) {
    }

    public record AuditRow(Long id, String adminEmail, String action, String targetType, String targetId,
                           String detail, Instant createdAt) {
    }

    public record LabelCount(String label, long count) {
    }

    public record ModelRow(Long id, String fileName, String kind, long sizeBytes, String sha256, long sampleCount,
                           Instant uploadedAt) {
    }

    public record MotionOverview(List<LabelCount> gestureByLabel, List<LabelCount> motionByLabel,
                                 List<com.wow.dto.motion.MotionRecordingResponse> recentRecordings,
                                 List<ModelRow> models) {
    }

    // ── 요청 ──
    public record ActiveRequest(boolean active, String reason) {
    }

    public record RoleRequest(String role) {
    }

    public record PasswordResetResponse(String tempPassword, String message) {
    }
}
