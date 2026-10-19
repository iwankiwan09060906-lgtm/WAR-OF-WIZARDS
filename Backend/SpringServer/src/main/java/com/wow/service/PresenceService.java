package com.wow.service;

import org.springframework.stereotype.Service;

import java.time.Duration;
import java.time.Instant;
import java.util.Collection;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 접속 상태(친구 목록의 초록 점). 메모리에만 저장한다.
 * - 웹 UI / Unity 는 20초마다 POST /api/presence/heartbeat 를 보낸다.
 * - Photon Webhook(GameJoin/GameLeave)이나 전용 서버가 in_game 상태를 알려준다.
 * 서버를 여러 대로 늘리면 Redis 같은 공유 저장소로 옮겨야 한다.
 */
@Service
public class PresenceService {

    public static final String ONLINE = "online";
    public static final String IN_GAME = "in_game";
    public static final String OFFLINE = "offline";

    private static final Duration ONLINE_TIMEOUT = Duration.ofSeconds(60);
    private static final Duration IN_GAME_TIMEOUT = Duration.ofHours(2);

    private final Map<Long, Entry> entries = new ConcurrentHashMap<>();
    private final Set<Long> pinned = ConcurrentHashMap.newKeySet();

    public void heartbeat(Long playerId) {
        entries.compute(playerId, (id, old) -> {
            // 게임 중에 로비 화면(웹)에서 하트비트가 와도 in_game 상태를 유지
            if (old != null && IN_GAME.equals(old.status()) && isAlive(old)) {
                return new Entry(IN_GAME, Instant.now(), old.roomCode());
            }
            return new Entry(ONLINE, Instant.now(), null);
        });
    }

    public void update(Long playerId, String status, String roomCode) {
        String normalized = status == null ? ONLINE : status.trim().toLowerCase(Locale.ROOT);
        switch (normalized) {
            case OFFLINE -> entries.remove(playerId);
            case IN_GAME -> entries.put(playerId, new Entry(IN_GAME, Instant.now(), roomCode));
            default -> entries.put(playerId, new Entry(ONLINE, Instant.now(), null));
        }
    }

    /** 데모 봇처럼 항상 접속 중으로 보여줄 플레이어 */
    public void pin(Long playerId) {
        pinned.add(playerId);
    }

    public String statusOf(Long playerId) {
        if (pinned.contains(playerId)) {
            return ONLINE;
        }
        Entry entry = entries.get(playerId);
        if (entry == null || !isAlive(entry)) {
            return OFFLINE;
        }
        return entry.status();
    }

    public boolean isOnline(Long playerId) {
        return !OFFLINE.equals(statusOf(playerId));
    }

    public List<Long> onlineAmong(Collection<Long> playerIds) {
        return playerIds.stream().filter(this::isOnline).toList();
    }

    /** 관리자 대시보드: 지금 접속 중(온라인 + 게임 중)인 플레이어 수 */
    public long onlineCount() {
        return entries.entrySet().stream()
                .filter(e -> !OFFLINE.equals(e.getValue().status()) && isAlive(e.getValue()))
                .count();
    }

    private boolean isAlive(Entry entry) {
        Duration timeout = IN_GAME.equals(entry.status()) ? IN_GAME_TIMEOUT : ONLINE_TIMEOUT;
        return entry.lastSeen().plus(timeout).isAfter(Instant.now());
    }

    private record Entry(String status, Instant lastSeen, String roomCode) {
    }
}
