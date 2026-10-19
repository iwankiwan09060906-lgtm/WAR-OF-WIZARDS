package com.wow.config;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.wow.common.ApiError;
import com.wow.repository.UserRepository;
import com.wow.security.JwtAuthenticationFilter;
import com.wow.security.JwtTokenProvider;
import com.wow.security.ServerKeyAuthenticationFilter;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import lombok.RequiredArgsConstructor;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.http.HttpMethod;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.security.config.Customizer;
import org.springframework.security.config.annotation.method.configuration.EnableMethodSecurity;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configuration.EnableWebSecurity;
import org.springframework.security.config.annotation.web.configurers.AbstractHttpConfigurer;
import org.springframework.security.config.annotation.web.configurers.HeadersConfigurer;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.UsernamePasswordAuthenticationFilter;

import java.io.IOException;
import java.nio.charset.StandardCharsets;

@Configuration
@EnableWebSecurity
@EnableMethodSecurity
@RequiredArgsConstructor
public class SecurityConfig {

    private final JwtTokenProvider tokenProvider;
    private final UserRepository userRepository;
    private final WowProperties props;
    private final ObjectMapper objectMapper;

    @Bean
    public SecurityFilterChain securityFilterChain(HttpSecurity http) throws Exception {
        http
                .cors(Customizer.withDefaults())
                .csrf(AbstractHttpConfigurer::disable)
                .httpBasic(AbstractHttpConfigurer::disable)
                .formLogin(AbstractHttpConfigurer::disable)
                .headers(headers -> headers.frameOptions(HeadersConfigurer.FrameOptionsConfig::sameOrigin)) // H2 콘솔
                .sessionManagement(session -> session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .exceptionHandling(ex -> ex
                        .authenticationEntryPoint((req, res, e) -> writeError(req, res, HttpStatus.UNAUTHORIZED, "로그인이 필요합니다."))
                        .accessDeniedHandler((req, res, e) -> writeError(req, res, HttpStatus.FORBIDDEN, "권한이 없습니다.")))
                .authorizeHttpRequests(auth -> auth
                        // SSE(실시간 이벤트) 연결이 끝날 때의 비동기 디스패치 / 오류 페이지 디스패치
                        .dispatcherTypeMatchers(jakarta.servlet.DispatcherType.ASYNC, jakarta.servlet.DispatcherType.ERROR).permitAll()
                        // 공개 API
                        .requestMatchers("/api/auth/login", "/api/auth/register", "/api/auth/nickname-available").permitAll()
                        .requestMatchers(HttpMethod.GET, "/api/skills/**", "/api/spells/**", "/api/players/ranking", "/api/health",
                                "/api/gestures/stats", "/api/gestures/model", "/api/gestures/model/info",
                                "/api/gestures/model/weights").permitAll()
                        // Photon 서버가 호출 (토큰/비밀키는 컨트롤러에서 직접 검증)
                        .requestMatchers("/api/photon/**").permitAll()
                        // 게임 서버 전용 (X-Server-Key)
                        .requestMatchers("/api/server/**").hasRole("SERVER")
                        // 관리자
                        .requestMatchers("/api/admin/**").hasRole("ADMIN")
                        // 그 외 API 는 로그인 필요
                        .requestMatchers("/api/**").authenticated()
                        // 웹 UI 정적 파일, Unity WebGL(/game), H2 콘솔, actuator
                        .anyRequest().permitAll()
                )
                .addFilterBefore(new ServerKeyAuthenticationFilter(props.serverApiKey()), UsernamePasswordAuthenticationFilter.class)
                .addFilterBefore(new JwtAuthenticationFilter(tokenProvider, userRepository), UsernamePasswordAuthenticationFilter.class);

        return http.build();
    }

    @Bean
    public PasswordEncoder passwordEncoder() {
        return new BCryptPasswordEncoder();
    }

    private void writeError(HttpServletRequest req, HttpServletResponse res, HttpStatus status, String message) throws IOException {
        res.setStatus(status.value());
        res.setContentType(MediaType.APPLICATION_JSON_VALUE);
        res.setCharacterEncoding(StandardCharsets.UTF_8.name());
        objectMapper.writeValue(res.getOutputStream(), ApiError.of(status, message, req.getRequestURI()));
    }
}
