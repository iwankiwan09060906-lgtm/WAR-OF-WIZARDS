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

import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

/** DB → Unity/웹 실시간 데이터 (bootstrap, game_configs, SSE 이벤트) */
@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
class LiveDataIntegrationTest {

    @Autowired
    private MockMvc mvc;
    @Autowired
    private ObjectMapper objectMapper;

    @Test
    void bootstrapReturnsEverythingUnityNeeds() throws Exception {
        String token = register("Boot");
        mvc.perform(get("/api/live/bootstrap")).andExpect(status().isUnauthorized());
        mvc.perform(bearer(get("/api/live/bootstrap"), token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.profile.nickname").exists())
                .andExpect(jsonPath("$.skills.length()").value(19))
                .andExpect(jsonPath("$.configs[?(@.key == 'cooldown.normal')].value").value("5"))
                .andExpect(jsonPath("$.unreadMail").isNumber())
                .andExpect(jsonPath("$.eventsUrl").value("/api/live/events"));
    }

    @Test
    void eventsArePushedToConnectedClients() throws Exception {
        String admin = login("admin@wow.dev", "admin1234");
        String alice = register("Ali");
        String bobNick = "Bob" + UUID.randomUUID().toString().substring(0, 5);
        String bob = registerWith(bobNick.toLowerCase() + "@test.dev", bobNick);

        // 토큰 없이는 연결 불가, ?access_token= 으로 연결 (브라우저 EventSource / Unity WebGL)
        mvc.perform(get("/api/live/events")).andExpect(status().isUnauthorized());
        MvcResult bobStream = mvc.perform(get("/api/live/events").param("access_token", bob))
                .andExpect(request().asyncStarted()).andReturn();
        MvcResult aliceStream = mvc.perform(get("/api/live/events").header("Authorization", "Bearer " + alice))
                .andExpect(request().asyncStarted()).andReturn();
        assertThat(text(bobStream)).contains("\"type\":\"hello\"");

        // 친구 요청 → bob 에게만
        mvc.perform(bearer(post("/api/friends/requests"), alice).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("nickname", bobNick)))).andExpect(status().isOk());
        assertThat(text(bobStream)).contains("\"type\":\"friend\"", "친구 요청");
        assertThat(text(aliceStream)).doesNotContain("\"type\":\"friend\"");

        // 관리자 설정 변경 → 모두에게 config 이벤트, DB 값도 바뀜
        mvc.perform(bearer(put("/api/admin/configs/cooldown.normal"), alice).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("value", "4")))).andExpect(status().isForbidden());
        mvc.perform(bearer(put("/api/admin/configs/cooldown.normal"), admin).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("value", "abc")))).andExpect(status().isBadRequest());
        mvc.perform(bearer(put("/api/admin/configs/cooldown.normal"), admin).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("value", "4"))))
                .andExpect(jsonPath("$.value").value("4"))
                .andExpect(jsonPath("$.version").value(2));
        assertThat(text(aliceStream)).contains("\"type\":\"config\"", "cooldown.normal");
        assertThat(text(bobStream)).contains("\"type\":\"config\"");
        mvc.perform(bearer(get("/api/live/config"), bob))
                .andExpect(jsonPath("$[?(@.key == 'cooldown.normal')].value").value("4"));

        // 관리자 → 특정 플레이어에게 임의 데이터 보내기
        long bobId = json(mvc.perform(bearer(get("/api/players/me"), bob)).andReturn()).get("id").asLong();
        mvc.perform(bearer(post("/api/admin/live/broadcast"), admin).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("toPlayerId", bobId, "type", "event_start", "title", "주말 이벤트",
                                "message", "경험치 2배", "payload", "{\"xp\":2}"))))
                .andExpect(jsonPath("$.ok").value(true));
        assertThat(text(bobStream)).contains("\"type\":\"event_start\"", "주말 이벤트", "\\\"xp\\\":2");
        assertThat(text(aliceStream)).doesNotContain("\"type\":\"event_start\"");

        mvc.perform(bearer(get("/api/admin/live/status"), admin)).andExpect(jsonPath("$.connectedPlayers").isNumber());

        // 원래 값으로 (다른 테스트에 영향 없도록)
        mvc.perform(bearer(put("/api/admin/configs/cooldown.normal"), admin).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("value", "5")))).andExpect(status().isOk());
    }

    // ── helpers ──

    private static String text(MvcResult r) throws Exception {
        return r.getResponse().getContentAsString(StandardCharsets.UTF_8);
    }

    private String register(String prefix) throws Exception {
        String nick = (prefix + UUID.randomUUID().toString().substring(0, 5)).replace("-", "");
        return registerWith(nick.toLowerCase() + "@test.dev", nick);
    }

    private String registerWith(String email, String nickname) throws Exception {
        return json(mvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("email", email, "password", "pass1234", "nickname", nickname))))
                .andExpect(status().isOk()).andReturn()).get("accessToken").asText();
    }

    private String login(String email, String password) throws Exception {
        return json(mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("email", email, "password", password))))
                .andExpect(status().isOk()).andReturn()).get("accessToken").asText();
    }

    private static MockHttpServletRequestBuilder bearer(MockHttpServletRequestBuilder b, String token) {
        return b.header("Authorization", "Bearer " + token);
    }

    private String body(Object v) throws Exception {
        return objectMapper.writeValueAsString(v);
    }

    private JsonNode json(MvcResult r) throws Exception {
        return objectMapper.readTree(r.getResponse().getContentAsString(StandardCharsets.UTF_8));
    }
}
