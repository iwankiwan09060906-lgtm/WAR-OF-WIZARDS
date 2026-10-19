package com.wow.service;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.wow.common.ApiException;
import com.wow.config.WowProperties;
import com.wow.dto.match.MatchReportResponse;
import com.wow.dto.match.ServerMatchReport;
import com.wow.entity.MatchMode;
import com.wow.entity.MatchSource;
import com.wow.entity.Player;
import com.wow.entity.User;
import com.wow.repository.PlayerRepository;
import com.wow.repository.UserRepository;
import com.wow.security.JwtTokenProvider;
import jakarta.validation.ConstraintViolation;
import jakarta.validation.Validator;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.Set;

/**
 * Photon(PUN2 / Realtime / Fusion) 연동.
 *
 * 1) Custom Authentication — Photon 이 클라이언트 접속 시 /api/photon/auth?token=JWT 를 호출
 *    성공: {"ResultCode":1,"UserId":"<playerId>","Nickname":"..."} → Photon UserId = 우리 playerId
 *    실패: {"ResultCode":2,"Message":"..."}
 *
 * 2) Webhooks (PUN2/Realtime) — /api/photon/webhooks/{GameCreate|GameJoin|GameLeave|GameClose|GameEvent}
 *    룸 상태·접속 상태(in_game) 갱신, HttpForward 이벤트(코드 101)로 온 경기 결과 기록.
 *
 * 3) WebRPC — PhotonNetwork.WebRpc("MatchResult", json) → /api/photon/webhooks/MatchResult
 *
 * Webhook/WebRPC 응답 규칙: {"ResultCode":0} 이 성공 (Custom Auth 는 1 이 성공 — 서로 다름에 주의)
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class PhotonService {

    private final JwtTokenProvider tokenProvider;
    private final UserRepository userRepository;
    private final PlayerRepository playerRepository;
    private final PresenceService presenceService;
    private final RoomService roomService;
    private final MatchService matchService;
    private final WowProperties props;
    private final ObjectMapper objectMapper;
    private final Validator validator;

    // ── 1. Custom Authentication ──────────────────────────────────────────

    @Transactional(readOnly = true)
    public Map<String, Object> authenticate(Map<String, String> params) {
        String secret = props.photon().authSecret();
        if (hasText(secret) && !secureEquals(secret, params.get("secret"))) {
            return authFail(2, "잘못된 인증 요청입니다.");
        }
        String token = params.get("token");
        if (!hasText(token)) {
            return authFail(3, "token 파라미터가 없습니다.");
        }
        Optional<JwtTokenProvider.TokenClaims> claims = tokenProvider.parse(token);
        if (claims.isEmpty() || claims.get().playerId() == null) {
            return authFail(2, "토큰이 유효하지 않거나 만료되었습니다. 다시 로그인해주세요.");
        }
        boolean activeUser = userRepository.findById(claims.get().userId()).map(User::isActive).orElse(false);
        Optional<Player> player = playerRepository.findById(claims.get().playerId());
        if (!activeUser || player.isEmpty()) {
            return authFail(2, "사용할 수 없는 계정입니다.");
        }

        Player p = player.get();
        Map<String, Object> data = new LinkedHashMap<>();
        data.put("playerId", p.getId());
        data.put("nickname", p.getNickname());
        data.put("rating", p.getRating());

        Map<String, Object> result = new LinkedHashMap<>();
        result.put("ResultCode", 1);
        result.put("UserId", String.valueOf(p.getId()));
        result.put("Nickname", p.getNickname());
        result.put("Data", data);
        return result;
    }

    // ── 2. Webhooks / 3. WebRPC ───────────────────────────────────────────

    public Map<String, Object> handleWebhook(String path, Map<String, Object> body, String headerSecret, String querySecret) {
        if (!verifyWebhookSecret(headerSecret, querySecret)) {
            log.warn("Photon webhook 비밀키 불일치: path={}", path);
            return webhookResult(1, "Unauthorized", null);
        }
        Map<String, Object> payload = body == null ? Map.of() : body;
        Long userPlayerId = parseLong(payload.get("UserId"));
        String gameId = str(payload.get("GameId"));

        try {
            if (payload.containsKey("RpcParams")) {
                return handleWebRpc(path, payload.get("RpcParams"), userPlayerId);
            }
            String type = str(payload.get("Type"));
            log.debug("Photon webhook {} type={} game={} user={}", path, type, gameId, userPlayerId);
            switch (type == null ? "" : type) {
                case "Create", "Load" -> {
                    roomService.markInGame(gameId, userPlayerId);
                    setInGame(userPlayerId, gameId);
                }
                case "Join" -> {
                    roomService.markInGame(gameId, null);
                    setInGame(userPlayerId, gameId);
                }
                case "Leave" -> {
                    if (userPlayerId != null) {
                        presenceService.update(userPlayerId, PresenceService.ONLINE, null);
                    }
                }
                case "Close" -> roomService.markClosed(gameId);
                case "Event" -> {
                    Integer evCode = parseInt(payload.get("EvCode"));
                    if (evCode != null && evCode == props.photon().matchResultEventCode()) {
                        MatchReportResponse report = recordFromPayload(payload.get("Data"), userPlayerId, gameId);
                        return webhookResult(0, "OK", report);
                    }
                }
                default -> {
                    // GameProperties 등은 기록만
                }
            }
            return webhookResult(0, "OK", null);
        } catch (ApiException ex) {
            return webhookResult(2, ex.getMessage(), null);
        } catch (RuntimeException ex) {
            log.error("Photon webhook 처리 실패: path={}", path, ex);
            return webhookResult(2, "서버 오류가 발생했습니다.", null);
        }
    }

    private Map<String, Object> handleWebRpc(String path, Object rpcParams, Long userPlayerId) {
        String name = path.toLowerCase(Locale.ROOT).replace("-", "");
        if (!name.equals("matchresult")) {
            return webhookResult(2, "알 수 없는 WebRPC 입니다: " + path, null);
        }
        MatchReportResponse report = recordFromPayload(rpcParams, userPlayerId, null);
        return webhookResult(0, "OK", report);
    }

    /**
     * 클라이언트(방장)가 Photon 을 거쳐 보낸 결과. UserId 는 Custom Auth 로 확인된 값이라 위조할 수 없지만,
     * 결과 내용 자체는 클라이언트가 만든 것이므로 allow-client-ranked=false 면 랭크전은 받지 않는다.
     */
    private MatchReportResponse recordFromPayload(Object data, Long reporterId, String gameId) {
        if (reporterId == null) {
            throw ApiException.unauthorized("Photon Custom Authentication 으로 로그인한 사용자만 결과를 보낼 수 있습니다.");
        }
        Object source = data;
        if (data instanceof String json) {
            try {
                source = objectMapper.readValue(json, Map.class);
            } catch (JsonProcessingException ex) {
                throw ApiException.badRequest("경기 결과 JSON 형식이 올바르지 않습니다.");
            }
        }
        ServerMatchReport report;
        try {
            report = objectMapper.convertValue(source, ServerMatchReport.class);
        } catch (IllegalArgumentException ex) {
            throw ApiException.badRequest("경기 결과 형식이 올바르지 않습니다.");
        }
        if (report == null) {
            throw ApiException.badRequest("경기 결과가 비어 있습니다.");
        }
        if (report.roomCode() == null && gameId != null) {
            report = new ServerMatchReport(report.matchToken(), report.mode(), gameId, report.winnerPlayerId(),
                    report.durationSec(), report.startedAt(), report.endedAt(), report.players());
        }
        Set<ConstraintViolation<ServerMatchReport>> violations = validator.validate(report);
        if (!violations.isEmpty()) {
            throw ApiException.badRequest(violations.iterator().next().getMessage());
        }
        boolean reporterIsPlayer = report.players().stream().anyMatch(p -> reporterId.equals(p.playerId()));
        if (!reporterIsPlayer) {
            throw ApiException.forbidden("경기 참가자만 결과를 보낼 수 있습니다.");
        }
        if (MatchMode.from(report.mode(), MatchMode.RANKED) == MatchMode.RANKED && !props.match().allowClientRanked()) {
            throw ApiException.forbidden("랭크전 결과는 게임 서버만 기록할 수 있습니다.");
        }
        return matchService.reportServerMatch(report, MatchSource.PHOTON);
    }

    private void setInGame(Long playerId, String roomCode) {
        if (playerId != null) {
            presenceService.update(playerId, PresenceService.IN_GAME, roomCode);
        }
    }

    private boolean verifyWebhookSecret(String headerSecret, String querySecret) {
        String expected = props.photon().webhookSecret();
        if (!hasText(expected)) {
            return true;
        }
        return secureEquals(expected, headerSecret) || secureEquals(expected, querySecret);
    }

    private static Map<String, Object> authFail(int code, String message) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("ResultCode", code);
        result.put("Message", message);
        return result;
    }

    private static Map<String, Object> webhookResult(int code, String message, Object data) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("ResultCode", code);
        result.put("Message", message);
        if (data != null) {
            result.put("Data", data);
        }
        return result;
    }

    private static boolean secureEquals(String expected, String provided) {
        if (provided == null) {
            return false;
        }
        return MessageDigest.isEqual(expected.getBytes(StandardCharsets.UTF_8), provided.getBytes(StandardCharsets.UTF_8));
    }

    private static String str(Object value) {
        return value == null ? null : String.valueOf(value);
    }

    private static Long parseLong(Object value) {
        if (value instanceof Number n) {
            return n.longValue();
        }
        try {
            return value == null ? null : Long.parseLong(String.valueOf(value).trim());
        } catch (NumberFormatException ex) {
            return null;
        }
    }

    private static Integer parseInt(Object value) {
        Long parsed = parseLong(value);
        return parsed == null ? null : parsed.intValue();
    }

    private static boolean hasText(String s) {
        return s != null && !s.isBlank();
    }
}
