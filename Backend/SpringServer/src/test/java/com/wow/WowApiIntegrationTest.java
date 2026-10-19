package com.wow;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

/**
 * 웹 UI · Unity · Photon · 게임 서버가 쓰는 API 전체 흐름을 H2 메모리 DB 로 검증한다.
 */
@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
class WowApiIntegrationTest {

    private static final String SERVER_KEY = "test-server-key";
    private static final String WEBHOOK_SECRET = "test-webhook-secret";

    @Autowired
    private MockMvc mvc;

    @Autowired
    private ObjectMapper objectMapper;

    // ── 공개 API ─────────────────────────────────────────────────────────

    @Test
    void healthAndSkillsArePublic() throws Exception {
        mvc.perform(get("/api/health")).andExpect(status().isOk()).andExpect(jsonPath("$.status").value("UP"));

        JsonNode skills = json(mvc.perform(get("/api/skills")).andExpect(status().isOk()).andReturn());
        assertThat(skills.size()).isEqualTo(19);
        assertThat(skills.get(0).get("code").asText()).isEqualTo("S01");
        assertThat(skills.get(13).get("code").asText()).isEqualTo("H01");
        assertThat(skills.get(13).get("usesPerGame").asInt()).isEqualTo(3);
        assertThat(skills.get(13).get("cooldownSeconds").asInt()).isEqualTo(20);

        mvc.perform(get("/api/spells/s03")).andExpect(status().isOk()).andExpect(jsonPath("$.name").value("파이어볼"));
        mvc.perform(get("/api/players/ranking?limit=5")).andExpect(status().isOk()).andExpect(jsonPath("$.length()").value(5));
    }

    @Test
    void protectedApiRequiresLogin() throws Exception {
        mvc.perform(get("/api/players/me"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.message").value("로그인이 필요합니다."));
    }

    // ── 인증 ─────────────────────────────────────────────────────────────

    @Test
    void registerLoginAndSessionRestore() throws Exception {
        Account a = register("Mage");

        mvc.perform(get("/api/auth/nickname-available").param("nickname", a.nickname))
                .andExpect(jsonPath("$.available").value(false));
        mvc.perform(get("/api/auth/nickname-available").param("nickname", "x"))
                .andExpect(jsonPath("$.available").value(false));

        // 같은 닉네임(대소문자만 다름) 가입 불가
        mvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("email", "other-" + a.email, "password", "pass1234", "nickname", a.nickname.toUpperCase()))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.message").value("이미 사용 중인 닉네임입니다."));

        mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("email", a.email, "password", "wrongpass"))))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.message").value("이메일 또는 비밀번호가 올바르지 않습니다."));

        // 예전 Unity AuthApi 형식(usernameOrEmail)도 로그인 가능
        mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("usernameOrEmail", a.email.toUpperCase(), "password", "pass1234"))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.playerId").value(a.playerId));

        mvc.perform(auth(get("/api/auth/me"), a))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.nickname").value(a.nickname))
                .andExpect(jsonPath("$.accessToken").isNotEmpty());

        // 가입 환영 우편
        mvc.perform(auth(get("/api/mails"), a))
                .andExpect(jsonPath("$[?(@.title == '가입을 환영합니다!')].reward").value("골드 500"));
    }

    @Test
    void demoAccountIsSeeded() throws Exception {
        MvcResult result = mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("email", "demo@wow.dev", "password", "demo1234"))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.nickname").value("DemoWizard"))
                .andReturn();
        Account demo = Account.from(json(result), "demo@wow.dev");

        mvc.perform(auth(get("/api/decks/me"), demo)).andExpect(jsonPath("$.skills.length()").value(8));
        mvc.perform(auth(get("/api/matches/me?limit=30"), demo)).andExpect(jsonPath("$.length()").value(5));
        JsonNode friends = json(mvc.perform(auth(get("/api/friends"), demo)).andReturn());
        assertThat(friends.size()).isEqualTo(5);
        mvc.perform(auth(post("/api/presence/heartbeat"), demo))
                .andExpect(jsonPath("$.onlineFriendIds.length()").value(3)); // Aria, Kael, Mira
    }

    // ── 마법서 ───────────────────────────────────────────────────────────

    @Test
    void deckRulesAreEnforced() throws Exception {
        Account a = register("Decker");

        mvc.perform(auth(put("/api/decks/me"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("skills", new String[]{"S01", "S02", "S03"}))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("덱은 정확히 8장이어야 합니다. (현재 3장)"));

        mvc.perform(auth(put("/api/decks/me"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("skills", new String[]{"S01", "S02", "S03", "S04", "S05", "H01", "H02", "H03"}))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("고위력 스킬은 덱에 최대 2장까지 넣을 수 있습니다."));

        mvc.perform(auth(put("/api/decks/me"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("skills", new String[]{"S01", "S01", "S03", "S04", "S05", "S06", "H02", "H03"}))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("같은 스킬을 중복해서 넣을 수 없습니다."));

        mvc.perform(auth(put("/api/decks/me"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("skills", new String[]{"S01", "S02", "S03", "S04", "S05", "S99", "H02", "H03"}))))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("존재하지 않는 스킬이 포함되어 있습니다."));

        String[] deck = {"s03", "S02", "S07", "S08", "S11", "S13", "H01", "H02"};
        mvc.perform(auth(put("/api/decks/me"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("skills", deck))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.complete").value(true));
        mvc.perform(auth(get("/api/decks/me"), a))
                .andExpect(jsonPath("$.skills[0]").value("S03"))
                .andExpect(jsonPath("$.skills[7]").value("H02"));
    }

    // ── 전적 ─────────────────────────────────────────────────────────────

    @Test
    void clientMatchRecordUpdatesProfileAndIsIdempotent() throws Exception {
        Account a = register("Fighter");
        String token = UUID.randomUUID().toString();
        Map<String, Object> req = Map.of("mode", "ranked", "result", "win", "opponent", "Bot",
                "durationSec", 312, "casts", 40, "matchToken", token);

        mvc.perform(auth(post("/api/matches"), a).contentType(MediaType.APPLICATION_JSON).content(body(req)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.ratingDelta").value(16))
                .andExpect(jsonPath("$.result").value("win"));
        // 같은 matchToken 재전송 → 한 번만 반영
        mvc.perform(auth(post("/api/matches"), a).contentType(MediaType.APPLICATION_JSON).content(body(req)))
                .andExpect(status().isOk());

        mvc.perform(auth(post("/api/matches"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("mode", "bot", "result", "lose", "opponent", "Bot", "durationSec", 100, "casts", 5))))
                .andExpect(jsonPath("$.ratingDelta").value(0));

        mvc.perform(auth(get("/api/players/me"), a))
                .andExpect(jsonPath("$.rating").value(1016))
                .andExpect(jsonPath("$.wins").value(1))
                .andExpect(jsonPath("$.losses").value(1))
                .andExpect(jsonPath("$.experience").value(200));
        mvc.perform(auth(get("/api/matches/me"), a))
                .andExpect(jsonPath("$.length()").value(2))
                .andExpect(jsonPath("$[0].mode").value("bot"));

        mvc.perform(auth(post("/api/matches"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("mode", "space", "result", "win"))))
                .andExpect(status().isBadRequest());
    }

    @Test
    void serverReportUsesEloAndRequiresServerKey() throws Exception {
        Account a = register("EloA");
        Account b = register("EloB");
        String token = "srv-" + UUID.randomUUID();
        String report = body(Map.of(
                "matchToken", token, "mode", "ranked", "roomCode", "ZZ99ZZ", "winnerPlayerId", a.playerId,
                "durationSec", 420,
                "players", new Object[]{
                        Map.of("playerId", a.playerId, "casts", 30, "damageDealt", 600, "damageTaken", 200),
                        Map.of("playerId", b.playerId, "casts", 25, "damageDealt", 200, "damageTaken", 600)}));

        mvc.perform(post("/api/server/matches").contentType(MediaType.APPLICATION_JSON).content(report))
                .andExpect(status().isUnauthorized());
        mvc.perform(auth(post("/api/server/matches"), a).contentType(MediaType.APPLICATION_JSON).content(report))
                .andExpect(status().isForbidden());

        mvc.perform(post("/api/server/matches").header("X-Server-Key", SERVER_KEY)
                        .contentType(MediaType.APPLICATION_JSON).content(report))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.duplicate").value(false))
                .andExpect(jsonPath("$.players[?(@.playerId == " + a.playerId + ")].ratingDelta").value(16))
                .andExpect(jsonPath("$.players[?(@.playerId == " + b.playerId + ")].ratingDelta").value(-16));
        mvc.perform(post("/api/server/matches").header("X-Server-Key", SERVER_KEY)
                        .contentType(MediaType.APPLICATION_JSON).content(report))
                .andExpect(jsonPath("$.duplicate").value(true));

        mvc.perform(auth(get("/api/matches/me"), b))
                .andExpect(jsonPath("$[0].opponent").value(a.nickname))
                .andExpect(jsonPath("$[0].result").value("lose"));

        mvc.perform(get("/api/server/players/" + a.playerId).header("X-Server-Key", SERVER_KEY))
                .andExpect(jsonPath("$.rating").value(1016));
        mvc.perform(post("/api/server/tokens/verify").header("X-Server-Key", SERVER_KEY)
                        .contentType(MediaType.APPLICATION_JSON).content(body(Map.of("token", a.token))))
                .andExpect(jsonPath("$.valid").value(true))
                .andExpect(jsonPath("$.playerId").value(a.playerId));
    }

    // ── 친구 · 우편 · 접속 상태 ──────────────────────────────────────────

    @Test
    void friendFlowWithInviteMail() throws Exception {
        Account a = register("Alice");
        Account b = register("Bob");

        mvc.perform(auth(post("/api/friends/requests"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("nickname", b.nickname.toLowerCase()))))
                .andExpect(status().isOk());
        mvc.perform(auth(post("/api/friends/requests"), b).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("nickname", a.nickname))))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.message").value("이미 친구 요청이 진행 중입니다."));

        // 친구가 아니면 초대 불가
        mvc.perform(auth(post("/api/mails/invite"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("friendId", b.playerId, "roomCode", "QW12ER"))))
                .andExpect(status().isForbidden());

        mvc.perform(auth(get("/api/friends"), b)).andExpect(jsonPath("$[0].relation").value("incoming"));
        mvc.perform(auth(post("/api/friends/" + a.playerId + "/accept"), b)).andExpect(status().isOk());
        mvc.perform(auth(get("/api/friends"), a)).andExpect(jsonPath("$[0].relation").value("friend"));

        mvc.perform(auth(post("/api/presence/heartbeat"), b)).andExpect(status().isOk());
        mvc.perform(auth(post("/api/presence/heartbeat"), a))
                .andExpect(jsonPath("$.onlineFriendIds[0]").value(b.playerId));

        mvc.perform(auth(post("/api/mails/invite"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("friendId", b.playerId, "roomCode", "bad"))))
                .andExpect(status().isBadRequest());
        mvc.perform(auth(post("/api/mails/invite"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("friendId", b.playerId, "roomCode", "qw12er"))))
                .andExpect(status().isOk());

        JsonNode mails = json(mvc.perform(auth(get("/api/mails"), b)).andReturn());
        JsonNode invite = null;
        for (JsonNode m : mails) {
            if ("invite".equals(m.get("kind").asText())) {
                invite = m;
            }
        }
        assertThat(invite).isNotNull();
        assertThat(invite.get("roomCode").asText()).isEqualTo("QW12ER");
        assertThat(invite.get("from").asText()).isEqualTo(a.nickname);

        long unreadBefore = json(mvc.perform(auth(get("/api/mails/unread-count"), b)).andReturn()).get("count").asLong();
        mvc.perform(auth(post("/api/mails/" + invite.get("id").asText() + "/read"), b)).andExpect(status().isOk());
        mvc.perform(auth(post("/api/mails/notice-1/read"), b)).andExpect(status().isOk());
        long unreadAfter = json(mvc.perform(auth(get("/api/mails/unread-count"), b)).andReturn()).get("count").asLong();
        assertThat(unreadAfter).isEqualTo(unreadBefore - 2);

        // 남의 우편은 읽음 처리 불가
        mvc.perform(auth(post("/api/mails/" + invite.get("id").asText() + "/read"), a)).andExpect(status().isNotFound());

        mvc.perform(auth(delete("/api/friends/" + b.playerId), a)).andExpect(status().isOk());
        mvc.perform(auth(get("/api/friends"), b)).andExpect(jsonPath("$.length()").value(0));
    }

    // ── 룸 · Photon ─────────────────────────────────────────────────────

    @Test
    void roomAndPhotonCustomAuth() throws Exception {
        Account a = register("Host");

        JsonNode room = json(mvc.perform(auth(post("/api/rooms"), a)).andExpect(status().isOk()).andReturn());
        String code = room.get("roomCode").asText();
        assertThat(code).matches("^[A-Z0-9]{6}$");
        assertThat(room.get("photonRoomName").asText()).isEqualTo(code);
        mvc.perform(auth(get("/api/rooms/" + code.toLowerCase()), a)).andExpect(jsonPath("$.status").value("open"));

        mvc.perform(get("/api/photon/auth").param("token", a.token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.ResultCode").value(1))
                .andExpect(jsonPath("$.UserId").value(String.valueOf(a.playerId)))
                .andExpect(jsonPath("$.Nickname").value(a.nickname));
        mvc.perform(get("/api/photon/auth").param("token", "not-a-jwt"))
                .andExpect(jsonPath("$.ResultCode").value(2));
        mvc.perform(get("/api/photon/auth"))
                .andExpect(jsonPath("$.ResultCode").value(3));
        mvc.perform(post("/api/photon/auth").contentType(MediaType.APPLICATION_JSON).content(body(Map.of("token", a.token))))
                .andExpect(jsonPath("$.ResultCode").value(1));
    }

    @Test
    void webRoomLobbyJoinReadyStart() throws Exception {
        Account host = register("RHost");
        Account guest = register("RGuest");
        Account third = register("RThird");

        String code = json(mvc.perform(auth(post("/api/rooms"), host)).andReturn()).get("roomCode").asText();

        // 게스트 입장 전에는 시작 불가
        mvc.perform(auth(post("/api/rooms/" + code + "/start"), host)).andExpect(status().isBadRequest());

        mvc.perform(auth(post("/api/rooms/" + code.toLowerCase() + "/join"), guest))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.guestPlayerId").value(guest.playerId))
                .andExpect(jsonPath("$.guestNickname").value(guest.nickname))
                .andExpect(jsonPath("$.guestReady").value(false));
        mvc.perform(auth(post("/api/rooms/" + code + "/join"), third)).andExpect(status().isConflict());

        // 준비 전 시작 불가, 게스트만 준비 가능
        mvc.perform(auth(post("/api/rooms/" + code + "/start"), host)).andExpect(status().isBadRequest());
        mvc.perform(auth(post("/api/rooms/" + code + "/ready"), third)).andExpect(status().isForbidden());
        mvc.perform(auth(post("/api/rooms/" + code + "/ready"), guest)).andExpect(jsonPath("$.guestReady").value(true));

        // 방장만 시작 가능 → 상태 in_game (게스트는 폴링으로 감지)
        mvc.perform(auth(post("/api/rooms/" + code + "/start"), guest)).andExpect(status().isForbidden());
        mvc.perform(auth(post("/api/rooms/" + code + "/start"), host)).andExpect(jsonPath("$.status").value("in_game"));
        mvc.perform(auth(get("/api/rooms/" + code), guest)).andExpect(jsonPath("$.status").value("in_game"));
        mvc.perform(auth(post("/api/rooms/" + code + "/join"), third)).andExpect(status().isConflict());

        // 내보내기 / 나가기
        String code2 = json(mvc.perform(auth(post("/api/rooms"), host)).andReturn()).get("roomCode").asText();
        mvc.perform(auth(post("/api/rooms/" + code2 + "/join"), guest)).andExpect(status().isOk());
        mvc.perform(auth(post("/api/rooms/" + code2 + "/kick"), guest)).andExpect(status().isForbidden());
        mvc.perform(auth(post("/api/rooms/" + code2 + "/kick"), host)).andExpect(jsonPath("$.guestPlayerId").doesNotExist());
        mvc.perform(auth(post("/api/rooms/" + code2 + "/join"), third)).andExpect(jsonPath("$.guestPlayerId").value(third.playerId));
        mvc.perform(auth(post("/api/rooms/" + code2 + "/leave"), third)).andExpect(jsonPath("$.guestPlayerId").doesNotExist());
        mvc.perform(auth(post("/api/rooms/" + code2 + "/leave"), host)).andExpect(jsonPath("$.status").value("closed"));
    }

    @Test
    void missingPageShowsHtmlErrorPageButApiStaysJson() throws Exception {
        mvc.perform(get("/no-such-page.html").accept(MediaType.TEXT_HTML))
                .andExpect(status().isNotFound())
                .andExpect(content().contentTypeCompatibleWith(MediaType.TEXT_HTML))
                .andExpect(content().string(org.hamcrest.Matchers.containsString("error-page.js")));
        mvc.perform(get("/api/no-such-api").accept(MediaType.TEXT_HTML, MediaType.APPLICATION_JSON))
                .andExpect(status().is4xxClientError());
        mvc.perform(get("/lobby.html")).andExpect(status().isOk());
    }

    @Test
    void rankedMatchmakingPairsTwoPlayers() throws Exception {
        Account a = register("MmA");
        Account b = register("MmB");

        mvc.perform(get("/api/matchmaking/status")).andExpect(status().isUnauthorized());
        mvc.perform(auth(post("/api/matchmaking/queue"), a)).andExpect(jsonPath("$.state").value("searching"));
        mvc.perform(auth(post("/api/matchmaking/queue"), b))
                .andExpect(jsonPath("$.state").value("matched"))
                .andExpect(jsonPath("$.opponentPlayerId").value(a.playerId))
                .andExpect(jsonPath("$.bot").value(false));
        mvc.perform(auth(get("/api/matchmaking/status"), a))
                .andExpect(jsonPath("$.state").value("matched"))
                .andExpect(jsonPath("$.opponentNickname").value(b.nickname));

        mvc.perform(auth(delete("/api/matchmaking/queue"), a)).andExpect(jsonPath("$.state").value("none"));
    }

    @Test
    void photonWebhooksTrackRoomAndRecordMatch() throws Exception {
        Account a = register("PunA");
        Account b = register("PunB");

        mvc.perform(post("/api/photon/webhooks/GameCreate").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("Type", "Create", "GameId", "PN12AB", "UserId", String.valueOf(a.playerId)))))
                .andExpect(jsonPath("$.ResultCode").value(1)); // 비밀키 없음

        mvc.perform(post("/api/photon/webhooks/GameCreate").header("X-Webhook-Secret", WEBHOOK_SECRET)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("Type", "Create", "GameId", "PN12AB", "UserId", String.valueOf(a.playerId)))))
                .andExpect(jsonPath("$.ResultCode").value(0));
        mvc.perform(auth(get("/api/rooms/PN12AB"), a)).andExpect(jsonPath("$.status").value("in_game"));
        mvc.perform(auth(get("/api/players/" + a.playerId), b)).andExpect(jsonPath("$.online").value(true));

        String result = body(Map.of("matchToken", "pun-" + UUID.randomUUID(), "mode", "friendly",
                "winnerPlayerId", b.playerId, "durationSec", 200,
                "players", new Object[]{Map.of("playerId", a.playerId, "casts", 10), Map.of("playerId", b.playerId, "casts", 12)}));
        // RaiseEvent(HttpForward) → PathEvent, Data 는 JSON 문자열
        mvc.perform(post("/api/photon/webhooks/GameEvent").param("key", WEBHOOK_SECRET)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("Type", "Event", "GameId", "PN12AB", "UserId", String.valueOf(a.playerId),
                                "EvCode", 101, "Data", result))))
                .andExpect(jsonPath("$.ResultCode").value(0))
                .andExpect(jsonPath("$.Data.players.length()").value(2));

        mvc.perform(auth(get("/api/matches/me"), b))
                .andExpect(jsonPath("$[0].result").value("win"))
                .andExpect(jsonPath("$[0].mode").value("friendly"));
        mvc.perform(auth(get("/api/rooms/PN12AB"), a)).andExpect(jsonPath("$.status").value("closed"));

        // 참가자가 아닌 사람이 보낸 결과는 거부
        Account c = register("PunC");
        mvc.perform(post("/api/photon/webhooks/MatchResult").header("X-Webhook-Secret", WEBHOOK_SECRET)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("AppId", "x", "UserId", String.valueOf(c.playerId), "RpcParams",
                                objectMapper.readValue(result.replace("pun-", "rpc-"), Map.class)))))
                .andExpect(jsonPath("$.ResultCode").value(2))
                .andExpect(jsonPath("$.Message").value("경기 참가자만 결과를 보낼 수 있습니다."));
    }

    // ── 제스처 AI (Unity → Spring → Python → Unity) ─────────────────────

    @Test
    void gestureSamplesExportAndModelDistribution() throws Exception {
        Account a = register("Rune");
        float[] circle = new float[30];
        for (int i = 0; i < 10; i++) {
            circle[i * 3] = (float) Math.cos(i * 0.6);
            circle[i * 3 + 1] = (float) Math.sin(i * 0.6);
            circle[i * 3 + 2] = 0.1f * i;
        }

        mvc.perform(auth(post("/api/gestures/samples"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("label", "s03", "points", circle, "durationMs", 800, "hand", "Right"))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.label").value("S03"))
                .andExpect(jsonPath("$.pointCount").value(10));
        mvc.perform(auth(post("/api/gestures/samples"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("label", "FIRE", "points", circle))))
                .andExpect(status().isBadRequest());
        mvc.perform(auth(post("/api/gestures/samples"), a).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("label", "S03", "points", new float[]{1, 2, 3, 4}))))
                .andExpect(status().isBadRequest());

        JsonNode stats = json(mvc.perform(get("/api/gestures/stats")).andExpect(status().isOk()).andReturn());
        assertThat(stats.size()).isEqualTo(19);
        assertThat(stats.get(2).get("label").asText()).isEqualTo("S03");
        assertThat(stats.get(2).get("count").asLong()).isGreaterThanOrEqualTo(1);

        // Python 내보내기는 서버 키 필요
        mvc.perform(get("/api/server/gestures/samples")).andExpect(status().isUnauthorized());
        JsonNode exported = json(mvc.perform(get("/api/server/gestures/samples?label=S03").header("X-Server-Key", SERVER_KEY))
                .andExpect(status().isOk()).andReturn());
        assertThat(exported.get(0).get("points").size()).isEqualTo(30);

        // 모델 업로드 → 정보 → 다운로드
        mvc.perform(get("/api/gestures/model/info")).andExpect(jsonPath("$.classes.length()").value(19));
        byte[] fakeModel = {8, 1, 18, 4, 't', 'e', 's', 't'};
        mvc.perform(multipart("/api/server/gestures/model")
                        .file(new org.springframework.mock.web.MockMultipartFile("file", "gesture_model.onnx",
                                "application/octet-stream", fakeModel))
                        .header("X-Server-Key", SERVER_KEY))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.available").value(true))
                .andExpect(jsonPath("$.sizeBytes").value(fakeModel.length));
        mvc.perform(get("/api/gestures/model"))
                .andExpect(status().isOk())
                .andExpect(content().bytes(fakeModel));

        // 가중치 JSON (Unity 순수 C# 추론용)
        byte[] weights = body(Map.of("classes", new String[]{"S01"}, "weightIh", new float[]{0.1f})).getBytes();
        mvc.perform(multipart("/api/server/gestures/model")
                        .file(new org.springframework.mock.web.MockMultipartFile("file", "gesture_model_weights.json",
                                "application/json", weights))
                        .header("X-Server-Key", SERVER_KEY))
                .andExpect(jsonPath("$.weightsAvailable").value(true));
        mvc.perform(get("/api/gestures/model/weights")).andExpect(status().isOk()).andExpect(content().bytes(weights));
        mvc.perform(multipart("/api/server/gestures/model")
                        .file(new org.springframework.mock.web.MockMultipartFile("file", "bad.json", "application/json", "{}".getBytes()))
                        .header("X-Server-Key", SERVER_KEY))
                .andExpect(status().isBadRequest());
    }

    // ── 관리자 ───────────────────────────────────────────────────────────

    @Test
    void adminOnlyEndpoints() throws Exception {
        Account user = register("NotAdmin");
        mvc.perform(auth(post("/api/admin/notices"), user).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("title", "t", "body", "b"))))
                .andExpect(status().isForbidden());

        Account admin = Account.from(json(mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("email", "admin@wow.dev", "password", "admin1234")))).andReturn()), "admin@wow.dev");
        mvc.perform(auth(post("/api/admin/notices"), admin).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("title", "[공지] 점검", "body", "내일 점검합니다."))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.kind").value("notice"));
        mvc.perform(auth(post("/api/admin/mails"), admin).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("toPlayerId", user.playerId, "title", "보상", "reward", "골드 100"))))
                .andExpect(status().isOk());
        mvc.perform(auth(get("/api/mails"), user))
                .andExpect(jsonPath("$[?(@.title == '보상')].from").value("운영팀"));
    }

    // ── Unity WebGL 서비스 ──────────────────────────────────────────────

    @Test
    void servesUnityWebGLBuildWithCompressionHeaders() throws Exception {
        mvc.perform(get("/game")).andExpect(status().isFound());

        Path build = Path.of("build/test-webgl/Build");
        Files.createDirectories(build);
        Files.write(build.resolve("Game.wasm.br"), new byte[]{1, 2, 3});
        Files.writeString(build.resolve("Game.framework.js.gz"), "x");
        Files.writeString(Path.of("build/test-webgl/index.html"), "<html>unity</html>");

        mvc.perform(get("/game/")).andExpect(status().isOk()).andExpect(content().string("<html>unity</html>"));
        mvc.perform(get("/game/Build/Game.wasm.br"))
                .andExpect(status().isOk())
                .andExpect(header().string("Content-Encoding", "br"))
                .andExpect(header().string("Content-Type", "application/wasm"));
        mvc.perform(get("/game/Build/Game.framework.js.gz"))
                .andExpect(header().string("Content-Encoding", "gzip"))
                .andExpect(header().string("Content-Type", "application/javascript"));
        mvc.perform(get("/game/../build.gradle")).andExpect(status().is4xxClientError());
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private record Account(String token, long playerId, String nickname, String email) {
        static Account from(JsonNode node, String email) {
            return new Account(node.get("accessToken").asText(), node.get("playerId").asLong(),
                    node.get("nickname").asText(), email);
        }
    }

    private Account register(String prefix) throws Exception {
        String suffix = UUID.randomUUID().toString().substring(0, 5);
        String nickname = (prefix + suffix).replace("-", "");
        if (nickname.length() > 12) {
            nickname = nickname.substring(0, 12);
        }
        String email = nickname.toLowerCase() + "@test.dev";
        MvcResult result = mvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("email", email, "password", "pass1234", "nickname", nickname))))
                .andExpect(status().isOk())
                .andReturn();
        return Account.from(json(result), email);
    }

    private MockHttpServletRequestBuilder auth(MockHttpServletRequestBuilder builder, Account account) {
        return builder.header("Authorization", "Bearer " + account.token);
    }

    private String body(Object value) throws Exception {
        return objectMapper.writeValueAsString(value);
    }

    private JsonNode json(MvcResult result) throws Exception {
        return objectMapper.readTree(result.getResponse().getContentAsString(java.nio.charset.StandardCharsets.UTF_8));
    }
}
