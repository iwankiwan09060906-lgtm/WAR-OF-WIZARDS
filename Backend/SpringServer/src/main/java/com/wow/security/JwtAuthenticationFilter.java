package com.wow.security;

import com.wow.repository.UserRepository;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import lombok.RequiredArgsConstructor;
import org.springframework.lang.NonNull;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.web.authentication.WebAuthenticationDetailsSource;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;
import java.util.List;

/**
 * Authorization: Bearer {token} 헤더를 읽어 로그인 상태로 만든다.
 * 권한(role)과 활성 여부는 토큰이 아니라 DB 기준으로 확인한다.
 * (SecurityConfig 에서 직접 생성 — @Component 로 등록하면 서블릿 필터로 한 번 더 실행되므로 등록하지 않는다)
 */
@RequiredArgsConstructor
public class JwtAuthenticationFilter extends OncePerRequestFilter {

    private final JwtTokenProvider tokenProvider;
    private final UserRepository userRepository;

    @Override
    protected void doFilterInternal(
            @NonNull HttpServletRequest request,
            @NonNull HttpServletResponse response,
            @NonNull FilterChain filterChain
    ) throws ServletException, IOException {
        String token = resolveToken(request);
        if (token != null && SecurityContextHolder.getContext().getAuthentication() == null) {
            tokenProvider.parse(token)
                    .filter(claims -> claims.playerId() != null)
                    .ifPresent(claims -> userRepository.findById(claims.userId())
                            .filter(user -> user.isActive())
                            .ifPresent(user -> {
                                UserPrincipal principal = new UserPrincipal(
                                        user.getId(), claims.playerId(), user.getUsername(), user.getRole().name());
                                UsernamePasswordAuthenticationToken authentication = new UsernamePasswordAuthenticationToken(
                                        principal, null, List.of(new SimpleGrantedAuthority(user.getRole().name())));
                                authentication.setDetails(new WebAuthenticationDetailsSource().buildDetails(request));
                                SecurityContextHolder.getContext().setAuthentication(authentication);
                            }));
        }
        filterChain.doFilter(request, response);
    }

    /** 실시간 이벤트(SSE)만 주소의 ?access_token= 도 허용 (브라우저 EventSource / Unity WebGL 은 헤더를 못 붙인다) */
    private static final String LIVE_EVENTS_PATH = "/api/live/events";

    private String resolveToken(HttpServletRequest request) {
        String header = request.getHeader("Authorization");
        if (header != null && header.startsWith("Bearer ")) {
            String token = header.substring(7).trim();
            return token.isEmpty() ? null : token;
        }
        if (LIVE_EVENTS_PATH.equals(request.getRequestURI())) {
            String token = request.getParameter("access_token");
            return token == null || token.isBlank() ? null : token.trim();
        }
        return null;
    }
}
