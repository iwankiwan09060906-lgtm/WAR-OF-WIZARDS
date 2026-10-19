package com.wow.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.util.List;

/**
 * application.yml 의 wow.* 설정 묶음.
 */
@ConfigurationProperties(prefix = "wow")
public record WowProperties(
        Jwt jwt,
        Cors cors,
        String serverApiKey,
        Photon photon,
        Match match,
        Rating rating,
        Webgl webgl,
        Ai ai,
        Seed seed
) {

    public record Ai(String modelPath) {
    }

    public record Jwt(String secret, long expirationHours) {
    }

    public record Cors(List<String> allowedOrigins) {
    }

    public record Photon(String authSecret, String webhookSecret, int matchResultEventCode) {
    }

    public record Match(boolean allowClientRanked) {
    }

    public record Rating(int clientRankedStep, int eloK) {
    }

    public record Webgl(String path) {
    }

    public record Seed(boolean demoData, String adminPassword) {
    }
}
