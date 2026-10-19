package com.wow.config;

import org.springframework.boot.diagnostics.AbstractFailureAnalyzer;
import org.springframework.boot.diagnostics.FailureAnalysis;

import javax.sql.DataSource;
import java.sql.Connection;
import java.sql.SQLException;

/**
 * DB 에 연결하지 못해 서버가 시작되지 않을 때, 긴 스택트레이스 대신 원인과 해결 방법을 한글로 보여준다.
 * (META-INF/spring.factories 에 등록)
 *
 * 콘솔 예)
 *   Description: ② 모션 DB(WOW_MOTION) 에 연결하지 못했습니다: 계정 또는 비밀번호가 틀렸습니다 (ORA-01017)
 *   Action:      Database/oracle/setup-oracle.bat 을 실행해 계정을 만드세요 …
 */
public class DatabaseConnectionFailureAnalyzer extends AbstractFailureAnalyzer<SQLException> {

    /** 서버 시작 때 한 번 접속해 본다 (MainDbConfig / MotionDbConfig 에서 호출). 실패하면 아래 analyze() 가 안내 */
    public static void verify(DataSource dataSource) throws SQLException {
        try (Connection ignored = dataSource.getConnection()) {
            // 접속 성공
        }
    }

    @Override
    protected FailureAnalysis analyze(Throwable rootFailure, SQLException cause) {
        String all = messages(rootFailure);
        boolean motion = all.contains("motion");
        String which = motion ? "② 모션 DB(WOW_MOTION)" : "① 통합 DB(WOW)";
        String yml = motion ? "wow.motion.datasource" : "spring.datasource";
        String msg = String.valueOf(cause.getMessage());

        String reason;
        String action;
        if (msg.contains("ORA-01017")) {
            reason = "계정 또는 비밀번호가 틀렸습니다 (ORA-01017)";
            action = "1) Database/oracle/setup-oracle.bat 을 실행해 WOW / WOW_MOTION 계정을 만드세요.\n"
                    + "2) 비밀번호를 바꿨다면 application-oracle.yml 의 " + yml + ".password 도 같이 바꾸세요.";
        } else if (msg.contains("ORA-28000")) {
            reason = "계정이 잠겨 있습니다 (ORA-28000)";
            action = "Database/oracle/setup-oracle.bat 을 다시 실행하면 잠금이 풀립니다.";
        } else if (msg.contains("ORA-12514") || msg.contains("ORA-12505")) {
            reason = "서비스 이름(XEPDB1)을 찾을 수 없습니다 (" + (msg.contains("ORA-12514") ? "ORA-12514" : "ORA-12505") + ")";
            action = "Oracle XE 가 완전히 시작될 때까지 1~2분 기다린 뒤 다시 실행하세요. "
                    + "다른 서비스 이름을 쓰면 " + yml + ".url 을 수정하세요.";
        } else if (msg.contains("ORA-12541") || msg.contains("Network Adapter") || msg.contains("Connection refused")) {
            reason = "Oracle 서버(리스너 1521 포트)에 접속할 수 없습니다";
            action = "Windows 서비스(services.msc)에서 OracleServiceXE 와 OracleOraDB21Home1TNSListener 를 시작하세요.\n"
                    + "Oracle 없이 실행하려면: run-server.bat h2";
        } else {
            reason = msg;
            action = "application-oracle.yml 의 " + yml + " 설정(url, username, password)을 확인하세요.\n"
                    + "Oracle 없이 실행하려면: run-server.bat h2";
        }
        return new FailureAnalysis(which + " 에 연결하지 못했습니다: " + reason, action, cause);
    }

    private static String messages(Throwable t) {
        StringBuilder sb = new StringBuilder();
        for (Throwable x = t; x != null && sb.length() < 20_000; x = x.getCause()) {
            sb.append(x.getMessage()).append('\n');
            if (x.getCause() == x) {
                break;
            }
        }
        return sb.toString().toLowerCase();
    }
}
