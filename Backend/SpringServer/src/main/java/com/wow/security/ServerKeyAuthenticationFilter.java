package com.wow.security;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.lang.NonNull;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.List;

/**
 * /api/server/** 요청의 X-Server-Key 헤더를 검사해 ROLE_SERVER 권한을 준다.
 * Photon 전용 서버, 헤드리스 Unity 서버처럼 "결과를 판정하는 쪽"만 이 키를 가진다.
 */
public class ServerKeyAuthenticationFilter extends OncePerRequestFilter {

    public static final String HEADER = "X-Server-Key";

    private final byte[] expectedKey;

    public ServerKeyAuthenticationFilter(String serverApiKey) {
        this.expectedKey = serverApiKey == null ? new byte[0] : serverApiKey.getBytes(StandardCharsets.UTF_8);
    }

    @Override
    protected boolean shouldNotFilter(@NonNull HttpServletRequest request) {
        return !request.getRequestURI().startsWith(request.getContextPath() + "/api/server/");
    }

    @Override
    protected void doFilterInternal(
            @NonNull HttpServletRequest request,
            @NonNull HttpServletResponse response,
            @NonNull FilterChain filterChain
    ) throws ServletException, IOException {
        String provided = request.getHeader(HEADER);
        if (provided != null && expectedKey.length > 0
                && MessageDigest.isEqual(expectedKey, provided.getBytes(StandardCharsets.UTF_8))) {
            UsernamePasswordAuthenticationToken authentication = new UsernamePasswordAuthenticationToken(
                    "game-server", null, List.of(new SimpleGrantedAuthority("ROLE_SERVER")));
            SecurityContextHolder.getContext().setAuthentication(authentication);
        }
        filterChain.doFilter(request, response);
    }
}
