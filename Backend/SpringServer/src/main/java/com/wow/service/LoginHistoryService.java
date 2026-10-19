package com.wow.service;

import com.wow.entity.LoginHistory;
import com.wow.repository.LoginHistoryRepository;
import jakarta.servlet.http.HttpServletRequest;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.context.request.RequestContextHolder;
import org.springframework.web.context.request.ServletRequestAttributes;

/**
 * 로그인/가입/로그아웃 기록 저장 (통합 DB login_history).
 * REQUIRES_NEW: 로그인이 실패해서 바깥 트랜잭션이 롤백돼도 '실패 기록'은 남도록 따로 커밋한다.
 * 기록 저장이 실패해도 로그인 자체는 막지 않는다.
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class LoginHistoryService {

    private final LoginHistoryRepository repository;

    @Transactional(propagation = Propagation.REQUIRES_NEW)
    public void record(LoginHistory.EventType type, Long userId, String email, String reason) {
        try {
            LoginHistory h = new LoginHistory();
            h.setEventType(type);
            h.setUserId(userId);
            h.setEmail(truncate(email, 100));
            h.setReason(truncate(reason, 100));
            HttpServletRequest req = currentRequest();
            if (req != null) {
                h.setIpAddress(truncate(clientIp(req), 45));
                h.setUserAgent(truncate(req.getHeader("User-Agent"), 300));
            }
            repository.save(h);
        } catch (Exception ex) {
            log.warn("로그인 기록 저장 실패: {}", ex.getMessage());
        }
    }

    private static HttpServletRequest currentRequest() {
        return RequestContextHolder.getRequestAttributes() instanceof ServletRequestAttributes attrs ? attrs.getRequest() : null;
    }

    /** 프록시(Cloudflare, ngrok) 뒤에 있으면 X-Forwarded-For 의 첫 번째 주소 */
    private static String clientIp(HttpServletRequest req) {
        String fwd = req.getHeader("X-Forwarded-For");
        if (fwd != null && !fwd.isBlank()) {
            return fwd.split(",")[0].trim();
        }
        return req.getRemoteAddr();
    }

    private static String truncate(String v, int max) {
        return v == null ? null : v.length() > max ? v.substring(0, max) : v;
    }
}
