package com.wow.service;

import com.wow.common.ApiException;
import com.wow.dto.live.LiveDtos.ConfigEntry;
import com.wow.entity.GameConfig;
import com.wow.repository.GameConfigRepository;
import com.wow.security.UserPrincipal;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.Locale;

/**
 * 게임 설정(game_configs) — DB 에 있는 값을 Unity / 웹이 받아서 쓴다. 관리자가 바꾸면 즉시 전달(config 이벤트).
 *
 * 새 설정 추가: 아래 DEFAULTS 에 한 줄 추가 → 서버 재시작하면 DB 에 없을 때만 들어간다 (기존 값은 유지).
 * Unity 에서 읽기: GameConfigStore.GetFloat("cooldown.normal", 5f)
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class GameConfigService implements ApplicationRunner {

    private static final String NUMBER = "NUMBER";
    private static final String TEXT = "TEXT";
    private static final String BOOL = "BOOL";

    /** {키, 기본값, 타입, 분류, 설명} — 키는 웹 WOW.RULES 경로와 같다 */
    private static final String[][] DEFAULTS = {
            {"cooldown.normal", "5", NUMBER, "전투", "일반 스킬 쿨타임 (초)"},
            {"cooldown.high", "20", NUMBER, "전투", "고위력 스킬 쿨타임 (초)"},
            {"highUsesPerGame", "3", NUMBER, "전투", "고위력 스킬 게임당 사용 횟수"},
            {"wave.interval", "10", NUMBER, "전투", "미니언 웨이브 간격 (초)"},
            {"wave.unitCap", "20", NUMBER, "전투", "진영당 최대 유닛 수"},
            {"player.maxHp", "600", NUMBER, "전투", "플레이어 최대 HP"},
            {"structure.tower", "300", NUMBER, "전투", "타워 HP"},
            {"structure.nexus", "500", NUMBER, "전투", "넥서스 HP"},
            {"defense.regenAmount", "20", NUMBER, "전투", "10초마다 회복하는 HP"},
            {"afk.loseAfter", "30", NUMBER, "전투", "무입력 기권패까지 시간 (초)"},
            {"aimTimeout", "5", NUMBER, "전투", "조준 자동 취소 시간 (초)"},
            {"countdown", "3", NUMBER, "전투", "대전 시작 카운트다운 (초)"},
            {"motd", "War of Wizards 에 오신 것을 환영합니다!", TEXT, "운영", "로비 한 줄 메시지 (Message of the Day)"},
            {"maintenance", "false", BOOL, "운영", "점검 모드 — true 면 클라이언트가 점검 안내를 띄운다"},
            {"client.minVersion", "0.1.0", TEXT, "운영", "최소 클라이언트 버전 — 낮으면 업데이트 안내"},
            {"event.xpMultiplier", "1.0", NUMBER, "이벤트", "경험치 배율 (이벤트 기간에 올리기)"},
    };

    private final GameConfigRepository repository;
    private final LiveEventService liveEventService;
    private final AdminService adminService;

    /** 서버 시작 시 없는 설정만 기본값으로 채운다 */
    @Override
    @Transactional
    public void run(ApplicationArguments args) {
        int added = 0;
        for (String[] d : DEFAULTS) {
            if (!repository.existsById(d[0])) {
                repository.save(new GameConfig(d[0], d[1], d[2], d[3], d[4]));
                added++;
            }
        }
        if (added > 0) {
            log.info("게임 설정 기본값 {}개를 game_configs 에 추가했습니다.", added);
        }
    }

    @Transactional(readOnly = true)
    public List<ConfigEntry> list() {
        return repository.findAllByOrderByCategoryAscKeyAsc().stream().map(GameConfigService::toEntry).toList();
    }

    /** 관리자 변경 → DB 저장 → 감사 로그 → 접속 중인 모든 클라이언트에 config 이벤트 */
    @Transactional
    public ConfigEntry update(UserPrincipal admin, String key, String rawValue) {
        GameConfig c = repository.findById(key).orElseThrow(() -> ApiException.notFound("없는 설정입니다: " + key));
        String value = validate(c, rawValue);
        String before = c.getValue();
        c.setValue(value);
        c.setVersion(c.getVersion() + 1);
        c.setUpdatedBy(admin.username());
        ConfigEntry entry = toEntry(c);
        adminService.audit(admin, "CONFIG_UPDATE", "game_config", key, before + " → " + value);
        liveEventService.broadcast("config", "게임 설정 변경", key + " = " + value, List.of(entry));
        return entry;
    }

    private static String validate(GameConfig c, String raw) {
        String v = raw == null ? "" : raw.trim();
        switch (c.getType()) {
            case NUMBER -> {
                try {
                    double d = Double.parseDouble(v);
                    if (Double.isNaN(d) || Double.isInfinite(d) || d < 0) {
                        throw new NumberFormatException();
                    }
                } catch (NumberFormatException ex) {
                    throw ApiException.badRequest(c.getKey() + " 은(는) 0 이상의 숫자여야 합니다.");
                }
            }
            case BOOL -> {
                v = v.toLowerCase(Locale.ROOT);
                if (!v.equals("true") && !v.equals("false")) {
                    throw ApiException.badRequest(c.getKey() + " 은(는) true 또는 false 여야 합니다.");
                }
            }
            default -> {
                if (v.length() > 1000) {
                    throw ApiException.badRequest("값은 1000자 이하여야 합니다.");
                }
            }
        }
        return v;
    }

    private static ConfigEntry toEntry(GameConfig c) {
        return new ConfigEntry(c.getKey(), c.getValue(), c.getType(), c.getCategory(), c.getDescription(), c.getVersion());
    }
}
