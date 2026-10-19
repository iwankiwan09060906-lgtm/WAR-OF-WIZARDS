package com.wow.controller;

import com.wow.dto.admin.AdminDtos.*;
import com.wow.dto.common.ActionResponse;
import com.wow.dto.live.LiveDtos.BroadcastRequest;
import com.wow.dto.live.LiveDtos.ConfigEntry;
import com.wow.dto.live.LiveDtos.ConfigUpdateRequest;
import com.wow.dto.live.LiveDtos.LiveStatus;
import com.wow.dto.mail.AdminMailRequest;
import com.wow.dto.mail.MailResponse;
import com.wow.dto.mail.NoticeRequest;
import com.wow.security.UserPrincipal;
import com.wow.common.ApiException;
import com.wow.service.AdminService;
import com.wow.service.GameConfigService;
import com.wow.service.LiveEventService;
import com.wow.service.MailService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

import java.util.List;

/**
 * 운영 도구 (ROLE_ADMIN 만). 기본 관리자 계정: admin@wow.dev
 *
 *  대시보드   GET  /api/admin/dashboard                  두 DB 상태 + 테이블별 행 수 + 오늘 통계
 *  회원      GET  /api/admin/users?q=&page=0&size=20    검색 (이메일/닉네임)
 *           GET  /api/admin/users/{userId}              상세 (+ 최근 로그인, 모션 데이터 수)
 *           PUT  /api/admin/users/{userId}/active       {active, reason}  정지/해제
 *           PUT  /api/admin/users/{userId}/role         {role}            ROLE_USER / ROLE_ADMIN
 *           POST /api/admin/users/{userId}/reset-password                 임시 비밀번호 발급
 *  기록      GET  /api/admin/login-history?type=&userId=&limit=100
 *           GET  /api/admin/audit-logs?limit=100
 *  모션 DB   GET  /api/admin/motion
 *           DELETE /api/admin/motion/gestures/{id}, /api/admin/motion/recordings/{id}
 *  공지/우편 POST /api/admin/notices, /api/admin/mails
 *  게임 설정 GET  /api/admin/configs,  PUT /api/admin/configs/{key} {value}   → 접속 중인 Unity/웹에 즉시 전달
 *  실시간    GET  /api/admin/live/status                                    지금 연결된 클라이언트 수
 *           POST /api/admin/live/broadcast {toPlayerId?, type, title, message, payload}  → Unity/웹으로 데이터 보내기
 */
@RestController
@RequestMapping("/api/admin")
@RequiredArgsConstructor
public class AdminController {

    private final MailService mailService;
    private final AdminService adminService;
    private final GameConfigService gameConfigService;
    private final LiveEventService liveEventService;

    @GetMapping("/dashboard")
    public ResponseEntity<Dashboard> dashboard() {
        return ResponseEntity.ok(adminService.dashboard());
    }

    // ── 회원 ──

    @GetMapping("/users")
    public ResponseEntity<MemberPage> users(
            @RequestParam(required = false) String q,
            @RequestParam(defaultValue = "0") int page,
            @RequestParam(defaultValue = "20") int size
    ) {
        return ResponseEntity.ok(adminService.members(q, page, size));
    }

    @GetMapping("/users/{userId}")
    public ResponseEntity<MemberDetail> user(@PathVariable Long userId) {
        return ResponseEntity.ok(adminService.member(userId));
    }

    @PutMapping("/users/{userId}/active")
    public ResponseEntity<MemberRow> setActive(@AuthenticationPrincipal UserPrincipal me, @PathVariable Long userId,
                                               @RequestBody ActiveRequest request) {
        return ResponseEntity.ok(adminService.setActive(me, userId, request.active(), request.reason()));
    }

    @PutMapping("/users/{userId}/role")
    public ResponseEntity<MemberRow> setRole(@AuthenticationPrincipal UserPrincipal me, @PathVariable Long userId,
                                             @RequestBody RoleRequest request) {
        return ResponseEntity.ok(adminService.setRole(me, userId, request.role()));
    }

    @PostMapping("/users/{userId}/reset-password")
    public ResponseEntity<PasswordResetResponse> resetPassword(@AuthenticationPrincipal UserPrincipal me, @PathVariable Long userId) {
        return ResponseEntity.ok(adminService.resetPassword(me, userId));
    }

    // ── 기록 ──

    @GetMapping("/login-history")
    public ResponseEntity<List<LoginRow>> loginHistory(
            @RequestParam(required = false) String type,
            @RequestParam(required = false) Long userId,
            @RequestParam(defaultValue = "100") int limit
    ) {
        return ResponseEntity.ok(adminService.loginHistory(type, userId, limit));
    }

    @GetMapping("/audit-logs")
    public ResponseEntity<List<AuditRow>> auditLogs(@RequestParam(defaultValue = "100") int limit) {
        return ResponseEntity.ok(adminService.auditLog(limit));
    }

    // ── 모션 DB ──

    @GetMapping("/motion")
    public ResponseEntity<MotionOverview> motion() {
        return ResponseEntity.ok(adminService.motion());
    }

    @DeleteMapping("/motion/gestures/{id}")
    public ResponseEntity<ActionResponse> deleteGesture(@AuthenticationPrincipal UserPrincipal me, @PathVariable Long id) {
        adminService.deleteGesture(me, id);
        return ResponseEntity.ok(ActionResponse.ok("제스처 샘플을 삭제했습니다."));
    }

    @DeleteMapping("/motion/recordings/{id}")
    public ResponseEntity<ActionResponse> deleteMotion(@AuthenticationPrincipal UserPrincipal me, @PathVariable Long id) {
        adminService.deleteMotion(me, id);
        return ResponseEntity.ok(ActionResponse.ok("모션 녹화를 삭제했습니다."));
    }

    // ── 게임 설정 · 실시간 전송 (DB → Unity / 웹) ──

    @GetMapping("/configs")
    public ResponseEntity<List<ConfigEntry>> configs() {
        return ResponseEntity.ok(gameConfigService.list());
    }

    @PutMapping("/configs/{key}")
    public ResponseEntity<ConfigEntry> updateConfig(@AuthenticationPrincipal UserPrincipal me, @PathVariable String key,
                                                    @RequestBody ConfigUpdateRequest request) {
        return ResponseEntity.ok(gameConfigService.update(me, key, request.value()));
    }

    @GetMapping("/live/status")
    public ResponseEntity<LiveStatus> liveStatus() {
        return ResponseEntity.ok(liveEventService.status());
    }

    @PostMapping("/live/broadcast")
    public ResponseEntity<ActionResponse> broadcast(@AuthenticationPrincipal UserPrincipal me, @RequestBody BroadcastRequest request) {
        if (request.title() == null || request.title().isBlank()) {
            throw ApiException.badRequest("제목을 입력해주세요.");
        }
        String type = request.type() == null || request.type().isBlank() ? "broadcast" : request.type().trim();
        if (!type.matches("[a-z][a-z0-9_]{0,29}")) {
            throw ApiException.badRequest("type 은 영문 소문자로 시작하는 30자 이하 이름이어야 합니다. (예: broadcast, event_start)");
        }
        if (request.toPlayerId() == null) {
            liveEventService.broadcast(type, request.title(), request.message(), request.payload());
        } else {
            liveEventService.sendToPlayer(request.toPlayerId(), type, request.title(), request.message(), request.payload());
        }
        adminService.audit(me, "LIVE_PUSH", "player", request.toPlayerId() == null ? "ALL" : request.toPlayerId(), type + " · " + request.title());
        LiveStatus st = liveEventService.status();
        return ResponseEntity.ok(ActionResponse.ok(request.toPlayerId() == null
                ? "접속 중인 " + st.connectedPlayers() + "명에게 보냈습니다."
                : (st.playerIds().contains(request.toPlayerId()) ? "보냈습니다." : "보냈지만 이 플레이어는 지금 접속 중이 아닙니다.")));
    }

    // ── 공지 / 우편 ──

    @PostMapping("/notices")
    public ResponseEntity<MailResponse> createNotice(@AuthenticationPrincipal UserPrincipal me, @Valid @RequestBody NoticeRequest request) {
        MailResponse notice = mailService.createNotice(request.title(), request.body());
        adminService.audit(me, "NOTICE_CREATE", "notice", notice.id(), request.title());
        return ResponseEntity.ok(notice);
    }

    @PostMapping("/mails")
    public ResponseEntity<ActionResponse> sendMail(@AuthenticationPrincipal UserPrincipal me, @Valid @RequestBody AdminMailRequest request) {
        ActionResponse result = mailService.sendAdminMail(request);
        adminService.audit(me, "MAIL_SEND", "player", request.toPlayerId() == null ? "ALL" : request.toPlayerId(), request.title());
        return ResponseEntity.ok(result);
    }
}
