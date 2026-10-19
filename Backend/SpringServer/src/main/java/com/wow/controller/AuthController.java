package com.wow.controller;

import com.wow.dto.auth.AuthResponse;
import com.wow.dto.auth.LoginRequest;
import com.wow.dto.auth.PasswordChangeRequest;
import com.wow.dto.auth.NicknameCheckResponse;
import com.wow.dto.auth.RegisterRequest;
import com.wow.dto.common.ActionResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.AuthService;
import com.wow.service.PresenceService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/api/auth")
@RequiredArgsConstructor
public class AuthController {

    private final AuthService authService;
    private final PresenceService presenceService;

    @PostMapping("/register")
    public ResponseEntity<AuthResponse> register(@Valid @RequestBody RegisterRequest request) {
        return ResponseEntity.ok(authService.register(request));
    }

    @PostMapping("/login")
    public ResponseEntity<AuthResponse> login(@Valid @RequestBody LoginRequest request) {
        return ResponseEntity.ok(authService.login(request));
    }

    /** 저장해 둔 토큰으로 세션 복구 (새 토큰 재발급) */
    @GetMapping("/me")
    public ResponseEntity<AuthResponse> me(@AuthenticationPrincipal UserPrincipal me) {
        return ResponseEntity.ok(authService.me(me.userId()));
    }

    /** JWT 는 서버에 저장하지 않으므로 클라이언트가 토큰을 지우면 된다. 여기서는 접속 상태만 오프라인으로. */
    @PostMapping("/logout")
    public ResponseEntity<ActionResponse> logout(@AuthenticationPrincipal UserPrincipal me) {
        presenceService.update(me.playerId(), PresenceService.OFFLINE, null);
        authService.recordLogout(me.userId());
        return ResponseEntity.ok(ActionResponse.ok("로그아웃했습니다."));
    }

    /** 비밀번호 변경: {currentPassword, newPassword} */
    @PutMapping("/password")
    public ResponseEntity<ActionResponse> changePassword(
            @AuthenticationPrincipal UserPrincipal me,
            @RequestBody PasswordChangeRequest request
    ) {
        return ResponseEntity.ok(authService.changePassword(me.userId(), request.currentPassword(), request.newPassword()));
    }

    @GetMapping("/nickname-available")
    public ResponseEntity<NicknameCheckResponse> nicknameAvailable(@RequestParam String nickname) {
        return ResponseEntity.ok(authService.checkNickname(nickname));
    }
}
