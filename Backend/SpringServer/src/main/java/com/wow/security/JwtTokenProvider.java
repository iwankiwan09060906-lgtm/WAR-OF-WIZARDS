package com.wow.security;

import com.wow.config.WowProperties;
import com.wow.entity.Player;
import com.wow.entity.User;
import io.jsonwebtoken.Claims;
import io.jsonwebtoken.JwtException;
import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.io.Decoders;
import io.jsonwebtoken.security.Keys;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import javax.crypto.SecretKey;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Date;
import java.util.Optional;

/**
 * 로그인 토큰(JWT) 발급/검증.
 * 같은 토큰을 웹 UI, Unity(에디터·WebGL·Quest), Photon Custom Auth 가 함께 사용한다.
 * subject = userId, pid = playerId (게임 안에서 쓰는 ID, Photon UserId 와 동일)
 */
@Slf4j
@Component
public class JwtTokenProvider {

    private final SecretKey key;
    private final long expirationMs;

    public JwtTokenProvider(WowProperties props) {
        this.key = Keys.hmacShaKeyFor(decodeSecret(props.jwt().secret()));
        this.expirationMs = Duration.ofHours(props.jwt().expirationHours()).toMillis();
    }

    public String createToken(User user, Player player) {
        Date now = new Date();
        return Jwts.builder()
                .subject(String.valueOf(user.getId()))
                .claim("pid", player.getId())
                .claim("name", user.getUsername())
                .claim("role", user.getRole().name())
                .issuedAt(now)
                .expiration(new Date(now.getTime() + expirationMs))
                .signWith(key)
                .compact();
    }

    public long getExpirationSeconds() {
        return expirationMs / 1000;
    }

    /** 서명·만료를 검사하고 내용을 돌려준다. 잘못된 토큰이면 empty. */
    public Optional<TokenClaims> parse(String token) {
        if (token == null || token.isBlank()) {
            return Optional.empty();
        }
        try {
            Claims claims = Jwts.parser().verifyWith(key).build().parseSignedClaims(token).getPayload();
            Number pid = claims.get("pid", Number.class);
            return Optional.of(new TokenClaims(
                    Long.valueOf(claims.getSubject()),
                    pid != null ? pid.longValue() : null,
                    claims.get("name", String.class),
                    claims.get("role", String.class)));
        } catch (JwtException | IllegalArgumentException ex) {
            log.debug("잘못된 JWT: {}", ex.getMessage());
            return Optional.empty();
        }
    }

    private static byte[] decodeSecret(String secret) {
        if (secret == null || secret.isBlank()) {
            throw new IllegalStateException("wow.jwt.secret 이 비어 있습니다.");
        }
        byte[] bytes;
        try {
            bytes = Decoders.BASE64.decode(secret);
        } catch (RuntimeException notBase64) {
            bytes = secret.getBytes(StandardCharsets.UTF_8);
        }
        if (bytes.length < 32) {
            bytes = secret.getBytes(StandardCharsets.UTF_8);
        }
        if (bytes.length < 32) {
            throw new IllegalStateException("wow.jwt.secret 은 32바이트(영문 32자) 이상이어야 합니다.");
        }
        return bytes;
    }

    public record TokenClaims(Long userId, Long playerId, String username, String role) {
    }
}
