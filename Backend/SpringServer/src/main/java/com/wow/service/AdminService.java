package com.wow.service;

import com.wow.common.ApiException;
import com.wow.config.MotionDbConfig;
import com.wow.dto.admin.AdminDtos.*;
import com.wow.entity.AdminAuditLog;
import com.wow.entity.LoginHistory;
import com.wow.entity.Player;
import com.wow.entity.User;
import com.wow.entity.UserRole;
import com.wow.motion.repository.GestureModelRepository;
import com.wow.motion.repository.GestureSampleRepository;
import com.wow.motion.repository.MotionRecordingRepository;
import com.wow.repository.*;
import com.wow.security.UserPrincipal;
import lombok.RequiredArgsConstructor;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.PageRequest;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.sql.DataSource;
import java.security.SecureRandom;
import java.sql.Connection;
import java.sql.DatabaseMetaData;
import java.time.Instant;
import java.time.LocalDate;
import java.time.ZoneId;
import java.util.*;

/**
 * 관리자 기능 — 통합 DB(회원·로그인 기록·감사 로그)와 모션 DB(제스처·모션)를 함께 관리한다.
 * 회원을 바꾸는 작업은 모두 admin_audit_log 에 남긴다.
 */
@Service
@RequiredArgsConstructor
public class AdminService {

    private static final ZoneId KST = ZoneId.of("Asia/Seoul");

    // 통합 DB
    private final UserRepository userRepository;
    private final PlayerRepository playerRepository;
    private final MatchRepository matchRepository;
    private final FriendshipRepository friendshipRepository;
    private final MailRepository mailRepository;
    private final NoticeRepository noticeRepository;
    private final GameRoomRepository roomRepository;
    private final LoginHistoryRepository loginHistoryRepository;
    private final AdminAuditLogRepository auditRepository;
    private final GameConfigRepository gameConfigRepository;
    // 모션 DB
    private final GestureSampleRepository gestureRepository;
    private final MotionRecordingRepository motionRepository;
    private final GestureModelRepository modelRepository;

    private final PresenceService presenceService;
    private final MotionService motionService;
    private final PasswordEncoder passwordEncoder;
    @Qualifier("dataSource")
    private final DataSource mainDataSource;
    @Qualifier("motionDataSource")
    private final DataSource motionDataSource;

    private final SecureRandom random = new SecureRandom();

    // ── 대시보드 ─────────────────────────────────────────────────────────

    public Dashboard dashboard() {
        Map<String, JpaRepository<?, ?>> mainTables = new LinkedHashMap<>();
        mainTables.put("users", userRepository);
        mainTables.put("players", playerRepository);
        mainTables.put("matches", matchRepository);
        mainTables.put("friendships", friendshipRepository);
        mainTables.put("mails", mailRepository);
        mainTables.put("notices", noticeRepository);
        mainTables.put("game_rooms", roomRepository);
        mainTables.put("login_history", loginHistoryRepository);
        mainTables.put("admin_audit_log", auditRepository);
        mainTables.put("game_configs", gameConfigRepository);

        Map<String, JpaRepository<?, ?>> motionTables = new LinkedHashMap<>();
        motionTables.put("gesture_samples", gestureRepository);
        motionTables.put("motion_recordings", motionRepository);
        motionTables.put("gesture_models", modelRepository);

        DbStatus main = dbStatus("통합 DB", "회원 · 로그인 · 게임 데이터", mainDataSource, mainTables);
        DbStatus motion = dbStatus("모션 DB", "제스처 · 손 모션 · AI 모델", motionDataSource, motionTables);

        Instant today = LocalDate.now(KST).atStartOfDay(KST).toInstant();
        return new Dashboard(List.of(main, motion),
                safeCount(playerRepository::countByBotFalse),
                safeCount(() -> loginHistoryRepository.countByEventTypeAndCreatedAtAfter(LoginHistory.EventType.LOGIN_SUCCESS, today)),
                safeCount(() -> loginHistoryRepository.countByEventTypeAndCreatedAtAfter(LoginHistory.EventType.REGISTER, today)),
                safeCount(() -> loginHistoryRepository.countByEventTypeAndCreatedAtAfter(LoginHistory.EventType.LOGIN_FAIL, today)),
                presenceService.onlineCount(),
                main.tables().getOrDefault("matches", 0L),
                motion.tables().getOrDefault("gesture_samples", 0L),
                motion.tables().getOrDefault("motion_recordings", 0L));
    }

    /** 한쪽 DB 가 꺼져 있어도 대시보드는 열리도록 예외를 잡아 DOWN 으로 표시 */
    private DbStatus dbStatus(String name, String role, DataSource ds, Map<String, JpaRepository<?, ?>> tables) {
        try (Connection c = ds.getConnection()) {
            DatabaseMetaData md = c.getMetaData();
            Map<String, Long> counts = new LinkedHashMap<>();
            tables.forEach((table, repo) -> counts.put(table, safeCount(repo::count)));
            return new DbStatus(name, role, true, md.getDatabaseProductName() + " " + md.getDatabaseProductVersion().split("\n")[0],
                    md.getURL(), md.getUserName(), counts, null);
        } catch (Exception ex) {
            return new DbStatus(name, role, false, null, null, null, Map.of(), ex.getMessage());
        }
    }

    private static long safeCount(java.util.function.LongSupplier s) {
        try {
            return s.getAsLong();
        } catch (Exception ex) {
            return -1;
        }
    }

    // ── 회원 관리 ────────────────────────────────────────────────────────

    @Transactional(readOnly = true)
    public MemberPage members(String q, int page, int size) {
        String like = "%" + (q == null ? "" : q.trim().toLowerCase(Locale.ROOT)) + "%";
        int safeSize = Math.min(Math.max(size, 1), 100);
        Page<Player> result = playerRepository.searchMembers(like, PageRequest.of(Math.max(page, 0), safeSize));
        return new MemberPage(result.getContent().stream().map(AdminService::toRow).toList(),
                result.getNumber(), result.getSize(), result.getTotalElements());
    }

    @Transactional(readOnly = true)
    public MemberDetail member(Long userId) {
        Player p = playerByUser(userId);
        return new MemberDetail(toRow(p),
                (long) p.getWins() + p.getLosses() + p.getDraws(),
                safeCount(() -> gestureRepository.countByPlayerId(p.getId())),   // 모션 DB
                safeCount(() -> motionRepository.countByPlayerId(p.getId())),    // 모션 DB
                loginHistoryRepository.findByUserIdOrderByIdDesc(userId, PageRequest.of(0, 20)).stream().map(AdminService::toLoginRow).toList());
    }

    @Transactional
    public MemberRow setActive(UserPrincipal admin, Long userId, boolean active, String reason) {
        if (admin.userId().equals(userId) && !active) {
            throw ApiException.badRequest("자기 자신의 계정은 정지할 수 없습니다.");
        }
        User u = user(userId);
        u.setActive(active);
        audit(admin, active ? "USER_ENABLE" : "USER_DISABLE", "user", userId, u.getEmail() + (reason == null || reason.isBlank() ? "" : " · " + reason));
        return toRow(playerByUser(userId));
    }

    @Transactional
    public MemberRow setRole(UserPrincipal admin, Long userId, String role) {
        UserRole r;
        try {
            r = UserRole.valueOf(role == null ? "" : role.trim().toUpperCase(Locale.ROOT));
        } catch (IllegalArgumentException ex) {
            throw ApiException.badRequest("role 은 ROLE_USER 또는 ROLE_ADMIN 이어야 합니다.");
        }
        if (admin.userId().equals(userId) && r != UserRole.ROLE_ADMIN) {
            throw ApiException.badRequest("자기 자신의 관리자 권한은 해제할 수 없습니다.");
        }
        User u = user(userId);
        u.setRole(r);
        audit(admin, "USER_ROLE", "user", userId, u.getEmail() + " → " + r.name());
        return toRow(playerByUser(userId));
    }

    /** 임시 비밀번호(10자리)를 만들어 돌려준다. 화면에 한 번만 보여주고 저장하지 않는다. */
    @Transactional
    public PasswordResetResponse resetPassword(UserPrincipal admin, Long userId) {
        User u = user(userId);
        String chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 10; i++) {
            sb.append(chars.charAt(random.nextInt(chars.length())));
        }
        u.setPasswordHash(passwordEncoder.encode(sb.toString()));
        u.setPasswordChangedAt(java.time.Instant.now());   // 로그인 잠금도 함께 풀린다
        audit(admin, "USER_PASSWORD_RESET", "user", userId, u.getEmail());
        return new PasswordResetResponse(sb.toString(), "임시 비밀번호를 사용자에게 전달하고, 로그인 후 바꾸도록 안내하세요.");
    }

    // ── 기록 조회 ────────────────────────────────────────────────────────

    @Transactional(readOnly = true)
    public List<LoginRow> loginHistory(String type, Long userId, int limit) {
        PageRequest page = PageRequest.of(0, Math.min(Math.max(limit, 1), 500));
        List<LoginHistory> list;
        if (userId != null) {
            list = loginHistoryRepository.findByUserIdOrderByIdDesc(userId, page);
        } else if (type != null && !type.isBlank()) {
            LoginHistory.EventType t;
            try {
                t = LoginHistory.EventType.valueOf(type.trim().toUpperCase(Locale.ROOT));
            } catch (IllegalArgumentException ex) {
                throw ApiException.badRequest("type 은 LOGIN_SUCCESS / LOGIN_FAIL / REGISTER / LOGOUT 중 하나입니다.");
            }
            list = loginHistoryRepository.findByEventTypeOrderByIdDesc(t, page);
        } else {
            list = loginHistoryRepository.findAllByOrderByIdDesc(page);
        }
        return list.stream().map(AdminService::toLoginRow).toList();
    }

    @Transactional(readOnly = true)
    public List<AuditRow> auditLog(int limit) {
        return auditRepository.findAllByOrderByIdDesc(PageRequest.of(0, Math.min(Math.max(limit, 1), 500))).stream()
                .map(a -> new AuditRow(a.getId(), a.getAdminEmail(), a.getAction(), a.getTargetType(), a.getTargetId(), a.getDetail(), a.getCreatedAt()))
                .toList();
    }

    /** 공지·우편 같은 다른 관리자 작업도 여기로 기록 */
    @Transactional
    public void audit(UserPrincipal admin, String action, String targetType, Object targetId, String detail) {
        AdminAuditLog log = new AdminAuditLog();
        log.setAdminUserId(admin.userId());
        log.setAdminEmail(admin.username());
        log.setAction(action);
        log.setTargetType(targetType);
        log.setTargetId(targetId == null ? null : String.valueOf(targetId));
        log.setDetail(detail != null && detail.length() > 500 ? detail.substring(0, 500) : detail);
        auditRepository.save(log);
    }

    // ── 모션 DB ──────────────────────────────────────────────────────────

    @Transactional(transactionManager = MotionDbConfig.TX, readOnly = true)
    public MotionOverview motion() {
        return new MotionOverview(
                labelCounts(gestureRepository.countByLabel()),
                labelCounts(motionRepository.countByLabel()),
                motionService.recent(30),
                modelRepository.findTop20ByOrderByIdDesc().stream()
                        .map(m -> new ModelRow(m.getId(), m.getFileName(), m.getKind(), m.getSizeBytes(), m.getSha256(), m.getSampleCount(), m.getUploadedAt()))
                        .toList());
    }

    @Transactional(transactionManager = MotionDbConfig.TX)
    public void deleteGesture(UserPrincipal admin, Long id) {
        if (!gestureRepository.existsById(id)) {
            throw ApiException.notFound("제스처 샘플을 찾을 수 없습니다.");
        }
        gestureRepository.deleteById(id);
        audit(admin, "GESTURE_DELETE", "gesture_sample", id, null);
    }

    @Transactional(transactionManager = MotionDbConfig.TX)
    public void deleteMotion(UserPrincipal admin, Long id) {
        if (!motionRepository.existsById(id)) {
            throw ApiException.notFound("모션 녹화를 찾을 수 없습니다.");
        }
        motionRepository.deleteById(id);
        audit(admin, "MOTION_DELETE", "motion_recording", id, null);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private User user(Long userId) {
        return userRepository.findById(userId).orElseThrow(() -> ApiException.notFound("회원을 찾을 수 없습니다."));
    }

    private Player playerByUser(Long userId) {
        return playerRepository.findByUserId(userId).orElseThrow(() -> ApiException.notFound("플레이어 정보가 없습니다."));
    }

    private static MemberRow toRow(Player p) {
        User u = p.getUser();
        return new MemberRow(u.getId(), p.getId(), u.getEmail(), p.getNickname(), u.getRole().name(), u.isActive(),
                p.getRating(), p.getWins(), p.getLosses(), u.getCreatedAt(), u.getLastLoginAt());
    }

    private static LoginRow toLoginRow(LoginHistory h) {
        return new LoginRow(h.getId(), h.getUserId(), h.getEmail(), h.getEventType().name(), h.getReason(),
                h.getIpAddress(), h.getUserAgent(), h.getCreatedAt());
    }

    private static List<LabelCount> labelCounts(List<Object[]> rows) {
        return rows.stream().map(r -> new LabelCount((String) r[0], ((Number) r[1]).longValue())).toList();
    }
}
