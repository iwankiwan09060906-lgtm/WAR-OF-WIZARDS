package com.wow.init;

import com.wow.entity.*;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.core.Ordered;
import org.springframework.core.annotation.Order;
import org.springframework.core.io.ClassPathResource;
import org.springframework.jdbc.datasource.init.ResourceDatabasePopulator;
import org.springframework.stereotype.Component;

import javax.sql.DataSource;
import java.sql.Connection;
import java.sql.DatabaseMetaData;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.stream.Collectors;

/**
 * 서버 시작 시 두 DB 를 점검한다. (DataInitializer 보다 먼저 실행)
 *  1) 통합 DB / 모션 DB 연결 정보와 테이블 목록을 로그에 출력
 *  2) Oracle 이면 테이블·컬럼 한글 설명(COMMENT)과 모션 DB 읽기 권한을 적용
 *     → src/main/resources/db/oracle/main-comments.sql, motion-comments.sql
 *  3) Oracle 이면 enum 컬럼의 CHECK 제약을 Java enum 과 맞춘다
 *     (JPA ddl-auto: update 는 enum 값이 늘어나도 기존 CHECK 를 고치지 않아서 새 값 저장이 ORA-02290 으로 실패함)
 *
 * 테이블 자체는 JPA(ddl-auto: update)가 엔티티를 보고 만든다.
 */
@Slf4j
@Component
@Order(Ordered.HIGHEST_PRECEDENCE)
@RequiredArgsConstructor
public class DatabaseInitializer implements ApplicationRunner {

    @Qualifier("dataSource")
    private final DataSource mainDataSource;
    @Qualifier("motionDataSource")
    private final DataSource motionDataSource;

    /** 통합 DB 의 enum 컬럼 (테이블.컬럼 → enum). 새 enum 컬럼을 만들면 여기에 한 줄 추가 */
    private static final Map<String, Class<? extends Enum<?>>> ENUM_COLUMNS = new LinkedHashMap<>();

    static {
        ENUM_COLUMNS.put("users.role", UserRole.class);
        ENUM_COLUMNS.put("login_history.event_type", LoginHistory.EventType.class);
        ENUM_COLUMNS.put("friendships.status", FriendshipStatus.class);
        ENUM_COLUMNS.put("mails.kind", MailKind.class);
        ENUM_COLUMNS.put("matches.game_mode", MatchMode.class);
        ENUM_COLUMNS.put("matches.source", MatchSource.class);
        ENUM_COLUMNS.put("match_players.outcome", MatchOutcome.class);
        ENUM_COLUMNS.put("game_rooms.game_mode", MatchMode.class);
        ENUM_COLUMNS.put("game_rooms.status", RoomStatus.class);
        ENUM_COLUMNS.put("skills.kind", SkillKind.class);
        ENUM_COLUMNS.put("skills.tier", SkillTier.class);
    }

    @Override
    public void run(ApplicationArguments args) {
        log.info("════════════════ DB 연결 점검 ════════════════");
        check("① 통합 DB", mainDataSource, "db/oracle/main-comments.sql");
        check("② 모션 DB", motionDataSource, "db/oracle/motion-comments.sql");
        log.info("══════════════════════════════════════════════");
    }

    private void check(String name, DataSource ds, String oracleScript) {
        boolean oracle;
        try (Connection c = ds.getConnection()) {
            DatabaseMetaData md = c.getMetaData();
            oracle = md.getDatabaseProductName().toLowerCase().contains("oracle");
            List<String> tables = tables(c, oracle ? md.getUserName() : null);
            log.info("{} 연결 OK — {} · 계정 {} · {}", name, md.getDatabaseProductName(), md.getUserName(), md.getURL());
            log.info("{} 테이블 {}개: {}", name, tables.size(), String.join(", ", tables));
        } catch (Exception ex) {
            // 연결 실패는 서버 시작 단계(DatabaseConnectionFailureAnalyzer)에서 이미 안내되므로 여기서는 경고만
            log.warn("{} 점검 실패: {}", name, ex.getMessage());
            return;
        }
        if (oracle) {
            if (ds == mainDataSource) {
                syncEnumChecks(name, ds);
            }
            applyScript(name, ds, oracleScript);
        }
    }

    /** enum 컬럼 CHECK 제약이 Java enum 값과 다르면 다시 만든다 (Oracle 전용) */
    private void syncEnumChecks(String name, DataSource ds) {
        try (Connection c = ds.getConnection()) {
            for (Map.Entry<String, Class<? extends Enum<?>>> e : ENUM_COLUMNS.entrySet()) {
                String[] parts = e.getKey().split("\\.");
                String table = parts[0].toUpperCase(Locale.ROOT);
                String column = parts[1];
                List<String> values = Arrays.stream(e.getValue().getEnumConstants()).map(Enum::name).toList();
                String wanted = column + " in (" + values.stream().map(v -> "'" + v + "'").collect(Collectors.joining(",")) + ")";

                List<String[]> existing = new ArrayList<>();   // {이름, 조건}
                try (PreparedStatement ps = c.prepareStatement(
                        "SELECT constraint_name, search_condition_vc FROM user_constraints "
                                + "WHERE table_name = ? AND constraint_type = 'C' AND LOWER(search_condition_vc) LIKE ?")) {
                    ps.setString(1, table);
                    ps.setString(2, column + " in (%");
                    try (ResultSet rs = ps.executeQuery()) {
                        while (rs.next()) {
                            existing.add(new String[]{rs.getString(1), rs.getString(2)});
                        }
                    }
                }
                if (existing.size() == 1 && sameValues(existing.get(0)[1], values)) {
                    continue;   // 이미 맞음
                }
                try (Statement st = c.createStatement()) {
                    for (String[] old : existing) {
                        st.execute("ALTER TABLE " + table + " DROP CONSTRAINT \"" + old[0] + "\"");
                    }
                    String ck = ("CK_" + table + "_" + column).toUpperCase(Locale.ROOT);
                    st.execute("ALTER TABLE " + table + " ADD CONSTRAINT " + (ck.length() > 128 ? ck.substring(0, 128) : ck) + " CHECK (" + wanted + ")");
                }
                log.info("{} {}.{} 허용 값 갱신: {}", name, table.toLowerCase(Locale.ROOT), column, values);
            }
        } catch (Exception ex) {
            log.warn("{} enum CHECK 제약 점검 실패 (새 enum 값 저장이 실패할 수 있음): {}", name, ex.getMessage());
        }
    }

    private static boolean sameValues(String condition, List<String> values) {
        String cond = condition == null ? "" : condition.toUpperCase(Locale.ROOT);
        long quoted = cond.chars().filter(ch -> ch == '\'').count() / 2;
        return quoted == values.size() && values.stream().allMatch(v -> cond.contains("'" + v + "'"));
    }

    private static List<String> tables(Connection c, String schema) throws Exception {
        List<String> names = new ArrayList<>();
        try (ResultSet rs = c.getMetaData().getTables(null, schema, "%", new String[]{"TABLE"})) {
            while (rs.next()) {
                String t = rs.getString("TABLE_NAME");
                if (schema != null || "PUBLIC".equalsIgnoreCase(rs.getString("TABLE_SCHEM"))) {
                    names.add(t.toLowerCase());
                }
            }
        }
        return names;
    }

    /** COMMENT/GRANT 는 여러 번 실행해도 안전. 실패한 줄은 건너뛰고 경고만 남긴다. */
    private static void applyScript(String name, DataSource ds, String path) {
        try {
            ResourceDatabasePopulator populator = new ResourceDatabasePopulator(new ClassPathResource(path));
            populator.setContinueOnError(true);
            populator.setSqlScriptEncoding("UTF-8");
            populator.execute(ds);
            log.info("{} 테이블 설명(COMMENT) 적용: {}", name, path);
        } catch (Exception ex) {
            log.warn("{} 테이블 설명 적용 실패 (서버 동작에는 영향 없음): {}", name, ex.getMessage());
        }
    }
}
