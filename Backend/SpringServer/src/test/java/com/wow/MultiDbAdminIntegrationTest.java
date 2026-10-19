package com.wow;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

import javax.sql.DataSource;
import java.nio.charset.StandardCharsets;
import java.sql.Connection;
import java.sql.ResultSet;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

/** 멀티 DB(통합 DB + 모션 DB) 분리와 관리자 회원 관리 기능 */
@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
class MultiDbAdminIntegrationTest {

    private static final String SERVER_KEY = "test-server-key";

    @Autowired
    private MockMvc mvc;
    @Autowired
    private ObjectMapper objectMapper;
    @Autowired
    @Qualifier("dataSource")
    private DataSource mainDataSource;
    @Autowired
    @Qualifier("motionDataSource")
    private DataSource motionDataSource;

    @Test
    void tablesAreSplitBetweenTheTwoDatabases() throws Exception {
        List<String> main = tables(mainDataSource);
        List<String> motion = tables(motionDataSource);

        assertThat(main).contains("USERS", "PLAYERS", "MATCHES", "LOGIN_HISTORY", "ADMIN_AUDIT_LOG")
                .doesNotContain("GESTURE_SAMPLES", "MOTION_RECORDINGS", "GESTURE_MODELS");
        assertThat(motion).contains("GESTURE_SAMPLES", "MOTION_RECORDINGS", "GESTURE_MODELS")
                .doesNotContain("USERS", "PLAYERS");
    }

    @Test
    void gestureAndMotionDataGoToMotionDb() throws Exception {
        String token = register("Motion");
        long before = rowCount(motionDataSource, "GESTURE_SAMPLES");

        mvc.perform(bearer(post("/api/gestures/samples"), token).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("label", "H06", "points", List.of(0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3)))))
                .andExpect(status().isOk());
        assertThat(rowCount(motionDataSource, "GESTURE_SAMPLES")).isEqualTo(before + 1);

        // 손 모션: 2관절 × 2프레임 × xyz = 12개
        List<Float> frames = new ArrayList<>();
        for (int i = 0; i < 12; i++) frames.add(i * 0.1f);
        JsonNode saved = json(mvc.perform(bearer(post("/api/motions"), token).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("label", "fist", "hand", "left", "jointCount", 2, "fps", 30, "frames", frames))))
                .andExpect(status().isOk()).andReturn());
        assertThat(saved.get("label").asText()).isEqualTo("FIST");
        assertThat(saved.get("frameCount").asInt()).isEqualTo(2);

        mvc.perform(bearer(post("/api/motions"), token).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("label", "fist", "jointCount", 2, "frames", List.of(1, 2, 3, 4)))))
                .andExpect(status().isBadRequest());

        mvc.perform(get("/api/server/motions").header("X-Server-Key", SERVER_KEY).param("label", "FIST"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[0].frames.length()").value(12));
        mvc.perform(get("/api/server/motions")).andExpect(status().isUnauthorized());
    }

    @Test
    void adminManagesMembersAndEverythingIsLogged() throws Exception {
        String admin = login("admin@wow.dev", "admin1234");
        String email = "ban" + UUID.randomUUID().toString().substring(0, 6) + "@test.dev";
        String userToken = registerWith(email, "BanMe" + UUID.randomUUID().toString().substring(0, 4));

        // 일반 회원은 관리자 API 불가
        mvc.perform(bearer(get("/api/admin/dashboard"), userToken)).andExpect(status().isForbidden());

        // 대시보드: 두 DB 모두 UP
        mvc.perform(bearer(get("/api/admin/dashboard"), admin))
                .andExpect(jsonPath("$.databases.length()").value(2))
                .andExpect(jsonPath("$.databases[0].up").value(true))
                .andExpect(jsonPath("$.databases[1].up").value(true))
                .andExpect(jsonPath("$.databases[1].tables.gesture_samples").exists());

        // 검색 → userId
        JsonNode page = json(mvc.perform(bearer(get("/api/admin/users"), admin).param("q", email)).andReturn());
        assertThat(page.get("total").asInt()).isEqualTo(1);
        long userId = page.get("items").get(0).get("userId").asLong();

        // 잘못된 비밀번호 → 실패 기록
        mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("email", email, "password", "wrong-pass")))).andExpect(status().isUnauthorized());

        // 정지 → 로그인 403
        mvc.perform(bearer(put("/api/admin/users/" + userId + "/active"), admin).contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("active", false, "reason", "테스트"))))
                .andExpect(jsonPath("$.active").value(false));
        mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("email", email, "password", "pass1234")))).andExpect(status().isForbidden());

        // 해제 + 임시 비밀번호로 로그인
        mvc.perform(bearer(put("/api/admin/users/" + userId + "/active"), admin).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("active", true)))).andExpect(jsonPath("$.active").value(true));
        String temp = json(mvc.perform(bearer(post("/api/admin/users/" + userId + "/reset-password"), admin)).andReturn())
                .get("tempPassword").asText();
        login(email, temp);

        // 권한 변경 / 자기 자신 정지 금지
        mvc.perform(bearer(put("/api/admin/users/" + userId + "/role"), admin).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("role", "ROLE_ADMIN")))).andExpect(jsonPath("$.role").value("ROLE_ADMIN"));
        long adminUserId = json(mvc.perform(bearer(get("/api/admin/users"), admin).param("q", "admin@wow.dev")).andReturn())
                .get("items").get(0).get("userId").asLong();
        mvc.perform(bearer(put("/api/admin/users/" + adminUserId + "/active"), admin).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("active", false)))).andExpect(status().isBadRequest());

        // 로그인 기록: 가입 · 실패(비밀번호/정지) · 성공
        JsonNode history = json(mvc.perform(bearer(get("/api/admin/login-history"), admin).param("userId", String.valueOf(userId))).andReturn());
        List<String> types = new ArrayList<>();
        history.forEach(h -> types.add(h.get("eventType").asText()));
        assertThat(types).contains("REGISTER", "LOGIN_FAIL", "LOGIN_SUCCESS");
        assertThat(history.toString()).contains("비밀번호 불일치", "정지된 계정");

        // 감사 로그
        JsonNode audit = json(mvc.perform(bearer(get("/api/admin/audit-logs"), admin)).andReturn());
        List<String> actions = new ArrayList<>();
        audit.forEach(a -> actions.add(a.get("action").asText()));
        assertThat(actions).contains("USER_DISABLE", "USER_ENABLE", "USER_PASSWORD_RESET", "USER_ROLE");

        // 회원 상세 + 모션 DB 개요
        mvc.perform(bearer(get("/api/admin/users/" + userId), admin))
                .andExpect(jsonPath("$.member.email").value(email))
                .andExpect(jsonPath("$.recentLogins").isArray());
        mvc.perform(bearer(get("/api/admin/motion"), admin)).andExpect(jsonPath("$.gestureByLabel").isArray());
    }

    @Test
    void passwordChangeAndLoginLock() throws Exception {
        String email = "lock" + UUID.randomUUID().toString().substring(0, 6) + "@test.dev";
        String token = registerWith(email, "Lock" + UUID.randomUUID().toString().substring(0, 5));

        // 비밀번호 변경: 현재 비밀번호 틀림 / 너무 짧음 / 성공
        mvc.perform(bearer(put("/api/auth/password"), token).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("currentPassword", "nope", "newPassword", "newpass99")))).andExpect(status().isBadRequest());
        mvc.perform(bearer(put("/api/auth/password"), token).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("currentPassword", "pass1234", "newPassword", "abc")))).andExpect(status().isBadRequest());
        mvc.perform(bearer(put("/api/auth/password"), token).contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("currentPassword", "pass1234", "newPassword", "newpass99")))).andExpect(jsonPath("$.ok").value(true));
        mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                .content(body(Map.of("email", email, "password", "pass1234")))).andExpect(status().isUnauthorized());
        login(email, "newpass99");

        // 5번 틀리면 올바른 비밀번호로도 잠시 로그인 불가 (429)
        for (int i = 0; i < 5; i++) {
            mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                    .content(body(Map.of("email", email, "password", "wrong" + i)))).andExpect(status().isUnauthorized());
        }
        mvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content(body(Map.of("email", email, "password", "newpass99"))))
                .andExpect(status().isTooManyRequests())
                .andExpect(jsonPath("$.message").value(org.hamcrest.Matchers.containsString("잠겼습니다")));

        // 관리자가 임시 비밀번호를 발급하면 잠금도 풀린다
        String admin = login("admin@wow.dev", "admin1234");
        long userId = json(mvc.perform(bearer(get("/api/admin/users"), admin).param("q", email)).andReturn())
                .get("items").get(0).get("userId").asLong();
        String temp = json(mvc.perform(bearer(post("/api/admin/users/" + userId + "/reset-password"), admin)).andReturn())
                .get("tempPassword").asText();
        login(email, temp);

        JsonNode history = json(mvc.perform(bearer(get("/api/admin/login-history"), admin).param("userId", String.valueOf(userId))).andReturn());
        assertThat(history.toString()).contains("PASSWORD_CHANGE", "로그인 잠금 중 시도");
    }

    // ── helpers ──

    private static List<String> tables(DataSource ds) throws Exception {
        List<String> names = new ArrayList<>();
        try (Connection c = ds.getConnection(); ResultSet rs = c.getMetaData().getTables(null, "PUBLIC", "%", new String[]{"TABLE"})) {
            while (rs.next()) names.add(rs.getString("TABLE_NAME").toUpperCase());
        }
        return names;
    }

    private static long rowCount(DataSource ds, String table) throws Exception {
        try (Connection c = ds.getConnection(); ResultSet rs = c.createStatement().executeQuery("select count(*) from " + table)) {
            rs.next();
            return rs.getLong(1);
        }
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
