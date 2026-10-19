package com.wow.controller;

import lombok.RequiredArgsConstructor;
import org.springframework.core.env.Environment;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

import java.time.Instant;
import java.util.List;
import java.util.Map;

/** Unity / 웹에서 서버 연결 확인용 (로그인 불필요) */
@RestController
@RequiredArgsConstructor
public class HealthController {

    private final Environment environment;

    @GetMapping("/api/health")
    public ResponseEntity<Map<String, Object>> health() {
        return ResponseEntity.ok(Map.of(
                "status", "UP",
                "service", "war-of-wizards-server",
                "db", List.of(environment.getActiveProfiles()),
                "time", Instant.now().toString()));
    }
}
