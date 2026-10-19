package com.wow.service;

import com.wow.common.ApiException;
import com.wow.dto.auth.AuthResponse;
import com.wow.dto.auth.LoginRequest;
import com.wow.dto.auth.NicknameCheckResponse;
import com.wow.dto.auth.RegisterRequest;
import com.wow.entity.LoginHistory;
import com.wow.entity.Player;
import com.wow.entity.User;
import com.wow.entity.UserRole;
import com.wow.dto.common.ActionResponse;
import com.wow.repository.LoginHistoryRepository;
import com.wow.repository.PlayerRepository;
import com.wow.repository.UserRepository;
import com.wow.security.JwtTokenProvider;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.Duration;
import java.time.Instant;
import java.util.Locale;

@Service
@RequiredArgsConstructor
public class AuthService {

    private static final String LOGIN_FAILED = "이메일 또는 비밀번호가 올바르지 않습니다.";
    /** 로그인 잠금: LOCK_MINUTES 분 안에 MAX_FAILS 번 틀리면 잠시 로그인 불가 (비밀번호 무차별 대입 방지) */
    static final int MAX_FAILS = 5;
    static final int LOCK_MINUTES = 15;

    private final UserRepository userRepository;
    private final PlayerRepository playerRepository;
    private final PasswordEncoder passwordEncoder;
    private final JwtTokenProvider tokenProvider;
    private final MailService mailService;
    private final LoginHistoryService loginHistory;
    private final LoginHistoryRepository loginHistoryRepository;

    @Transactional
    public AuthResponse register(RegisterRequest request) {
        String email = request.email().trim().toLowerCase(Locale.ROOT);
        String username = request.username() == null || request.username().isBlank()
                ? email
                : request.username().trim();
        String nickname = PlayerService.validateNickname(request.nickname());

        if (userRepository.existsByEmailIgnoreCase(email)) {
            throw ApiException.conflict("이미 가입된 이메일입니다.");
        }
        if (userRepository.existsByUsernameIgnoreCase(username)) {
            throw ApiException.conflict("이미 사용 중인 아이디입니다.");
        }
        if (playerRepository.existsByNicknameIgnoreCase(nickname)) {
            throw ApiException.conflict("이미 사용 중인 닉네임입니다.");
        }

        User user = userRepository.save(new User(username, email, passwordEncoder.encode(request.password()), UserRole.ROLE_USER));
        Player player = playerRepository.save(new Player(user, nickname));
        mailService.sendWelcomeMail(player);
        user.setLastLoginAt(Instant.now());
        user.setPasswordChangedAt(Instant.now());
        loginHistory.record(LoginHistory.EventType.REGISTER, user.getId(), email, null);

        return toAuthResponse(user, player);
    }

    /** 성공/실패 모두 login_history 에 남긴다 (관리자 페이지 '로그인 기록') */
    @Transactional
    public AuthResponse login(LoginRequest request) {
        String id = request.identifier();
        if (id.isEmpty()) {
            throw ApiException.badRequest("이메일을 입력해주세요.");
        }
        User user = userRepository.findByEmailIgnoreCase(id)
                .or(() -> userRepository.findByUsernameIgnoreCase(id))
                .orElse(null);
        if (user == null) {
            loginHistory.record(LoginHistory.EventType.LOGIN_FAIL, null, id, "등록되지 않은 계정");
            throw ApiException.unauthorized(LOGIN_FAILED);
        }
        if (recentFailures(user) >= MAX_FAILS) {
            loginHistory.record(LoginHistory.EventType.LOGIN_FAIL, user.getId(), user.getEmail(), "로그인 잠금 중 시도");
            throw new ApiException(HttpStatus.TOO_MANY_REQUESTS,
                    "비밀번호를 " + MAX_FAILS + "번 이상 틀려 로그인이 잠겼습니다. " + LOCK_MINUTES + "분 뒤에 다시 시도하거나 관리자에게 문의해주세요.");
        }
        if (!passwordEncoder.matches(request.password(), user.getPasswordHash())) {
            loginHistory.record(LoginHistory.EventType.LOGIN_FAIL, user.getId(), user.getEmail(), "비밀번호 불일치");
            throw ApiException.unauthorized(LOGIN_FAILED);
        }
        if (!user.isActive()) {
            loginHistory.record(LoginHistory.EventType.LOGIN_FAIL, user.getId(), user.getEmail(), "정지된 계정");
            throw ApiException.forbidden("이용이 정지된 계정입니다. 관리자에게 문의해주세요.");
        }
        Player player = playerRepository.findByUserId(user.getId())
                .orElseThrow(() -> ApiException.notFound("플레이어 정보가 없습니다. 관리자에게 문의하세요."));
        user.setLastLoginAt(Instant.now());
        loginHistory.record(LoginHistory.EventType.LOGIN_SUCCESS, user.getId(), user.getEmail(), null);
        return toAuthResponse(user, player);
    }

    /** 마지막 성공 로그인 · 비밀번호 변경 이후, 최근 LOCK_MINUTES 분 동안의 실패 횟수 */
    private long recentFailures(User user) {
        Instant since = Instant.now().minus(Duration.ofMinutes(LOCK_MINUTES));
        if (user.getLastLoginAt() != null && user.getLastLoginAt().isAfter(since)) {
            since = user.getLastLoginAt();
        }
        if (user.getPasswordChangedAt() != null && user.getPasswordChangedAt().isAfter(since)) {
            since = user.getPasswordChangedAt();
        }
        return loginHistoryRepository.countByUserIdAndEventTypeAndCreatedAtAfter(user.getId(), LoginHistory.EventType.LOGIN_FAIL, since);
    }

    /** 비밀번호 변경 (로그인한 본인). 관리자가 발급한 임시 비밀번호를 바꿀 때도 사용 */
    @Transactional
    public ActionResponse changePassword(Long userId, String currentPassword, String newPassword) {
        User user = userRepository.findById(userId)
                .orElseThrow(() -> ApiException.unauthorized("로그인이 필요합니다."));
        if (currentPassword == null || !passwordEncoder.matches(currentPassword, user.getPasswordHash())) {
            throw ApiException.badRequest("현재 비밀번호가 올바르지 않습니다.");
        }
        if (newPassword == null || newPassword.length() < 6 || newPassword.length() > 100) {
            throw ApiException.badRequest("새 비밀번호는 6~100자여야 합니다.");
        }
        if (passwordEncoder.matches(newPassword, user.getPasswordHash())) {
            throw ApiException.badRequest("새 비밀번호가 현재 비밀번호와 같습니다.");
        }
        user.setPasswordHash(passwordEncoder.encode(newPassword));
        user.setPasswordChangedAt(Instant.now());
        loginHistory.record(LoginHistory.EventType.PASSWORD_CHANGE, user.getId(), user.getEmail(), null);
        return ActionResponse.ok("비밀번호를 변경했습니다.");
    }

    public void recordLogout(Long userId) {
        userRepository.findById(userId).ifPresent(u ->
                loginHistory.record(LoginHistory.EventType.LOGOUT, u.getId(), u.getEmail(), null));
    }

    /** 저장된 토큰으로 세션 복구 — 새 토큰도 함께 발급해 만료 시간을 연장한다. */
    @Transactional(readOnly = true)
    public AuthResponse me(Long userId) {
        User user = userRepository.findById(userId)
                .filter(User::isActive)
                .orElseThrow(() -> ApiException.unauthorized("로그인이 필요합니다."));
        Player player = playerRepository.findByUserId(userId)
                .orElseThrow(() -> ApiException.notFound("플레이어 정보가 없습니다."));
        return toAuthResponse(user, player);
    }

    @Transactional(readOnly = true)
    public NicknameCheckResponse checkNickname(String nickname) {
        String nick = nickname == null ? "" : nickname.trim();
        if (!PlayerService.isValidNickname(nick)) {
            return new NicknameCheckResponse(nick, false, "닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.");
        }
        if (playerRepository.existsByNicknameIgnoreCase(nick)) {
            return new NicknameCheckResponse(nick, false, "이미 사용 중인 닉네임입니다.");
        }
        return new NicknameCheckResponse(nick, true, "사용할 수 있는 닉네임입니다.");
    }

    private AuthResponse toAuthResponse(User user, Player player) {
        return new AuthResponse(
                "Bearer",
                tokenProvider.createToken(user, player),
                tokenProvider.getExpirationSeconds(),
                user.getId(),
                player.getId(),
                user.getUsername(),
                user.getEmail(),
                player.getNickname(),
                player.getRating(),
                user.getRole().name());
    }
}
