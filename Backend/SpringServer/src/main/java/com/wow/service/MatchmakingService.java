package com.wow.service;

import com.wow.dto.matchmaking.MatchmakingStatus;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

import java.time.Duration;
import java.time.Instant;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.UUID;
import java.util.function.Supplier;

/**
 * 랭킹전 매칭 대기열 (메모리). 웹 UI / Unity 는
 *   POST /api/matchmaking/queue  → 대기열 참가
 *   GET  /api/matchmaking/status → 2초마다 확인 (확인할 때마다 매칭 시도)
 *   DELETE /api/matchmaking/queue → 취소
 * 순서로 쓴다.
 *
 * 규칙
 *  - 허용 레이팅 차이 = 50 + 대기초 × 15 (오래 기다릴수록 넓어짐). 두 사람 중 넓은 쪽 기준.
 *  - botTimeoutSeconds 동안 상대가 없으면 비슷한 레이팅의 봇과 매칭.
 *  - 10초 넘게 상태 확인이 없으면 창을 닫은 것으로 보고 대기열에서 뺀다.
 * 서버를 여러 대로 늘리면 Redis 같은 공유 저장소로 옮겨야 한다.
 */
@Service
public class MatchmakingService {

    static final int BASE_RANGE = 50;
    static final int RANGE_PER_SECOND = 15;
    private static final Duration STALE_AFTER = Duration.ofSeconds(10);
    private static final Duration KEEP_RESULT = Duration.ofSeconds(60);

    private final int botTimeoutSeconds;
    private Supplier<Instant> clock = Instant::now;

    /** 대기 중 (참가 순서 유지) */
    private final Map<Long, Ticket> waiting = new LinkedHashMap<>();
    /** 매칭 완료 결과 (양쪽 모두 상태 확인으로 가져간다) */
    private final Map<Long, Result> matched = new LinkedHashMap<>();

    public MatchmakingService(@Value("${wow.matchmaking.bot-timeout-seconds:30}") int botTimeoutSeconds) {
        this.botTimeoutSeconds = botTimeoutSeconds;
    }

    /** 테스트용: 시간 바꾸기 */
    void setClock(Supplier<Instant> clock) {
        this.clock = clock;
    }

    public synchronized MatchmakingStatus join(Long playerId, String nickname, int rating) {
        cleanUp();
        matched.remove(playerId);
        waiting.computeIfAbsent(playerId, id -> new Ticket(id, nickname, rating, now(), now()));
        tryMatch();
        return status(playerId);
    }

    public synchronized void cancel(Long playerId) {
        waiting.remove(playerId);
        matched.remove(playerId);
    }

    public synchronized MatchmakingStatus poll(Long playerId) {
        Ticket t = waiting.get(playerId);
        if (t != null) {
            t.lastSeen = now();
        }
        cleanUp();
        tryMatch();
        return status(playerId);
    }

    // ── 내부 ─────────────────────────────────────────────────────────

    private MatchmakingStatus status(Long playerId) {
        Result r = matched.get(playerId);
        if (r != null) {
            return new MatchmakingStatus("matched", r.waitedSec, 0, waiting.size(), botTimeoutSeconds,
                    r.opponentId, r.opponentNickname, r.opponentRating, r.bot, r.matchId);
        }
        Ticket t = waiting.get(playerId);
        if (t == null) {
            return new MatchmakingStatus("none", 0, 0, waiting.size(), botTimeoutSeconds, null, null, null, false, null);
        }
        int waited = waitedSec(t);
        return new MatchmakingStatus("searching", waited, range(t), waiting.size(), botTimeoutSeconds,
                null, null, null, false, null);
    }

    private void tryMatch() {
        // 1) 사람끼리: 먼저 온 사람부터, 레이팅이 가장 가까운 상대와
        boolean found = true;
        while (found) {
            found = false;
            for (Ticket a : waiting.values()) {
                Ticket b = waiting.values().stream()
                        .filter(x -> !x.playerId.equals(a.playerId))
                        .filter(x -> Math.abs(x.rating - a.rating) <= Math.max(range(a), range(x)))
                        .min(Comparator.comparingInt(x -> Math.abs(x.rating - a.rating)))
                        .orElse(null);
                if (b != null) {
                    String matchId = UUID.randomUUID().toString();
                    matched.put(a.playerId, new Result(b.playerId, b.nickname, b.rating, false, matchId, waitedSec(a), now()));
                    matched.put(b.playerId, new Result(a.playerId, a.nickname, a.rating, false, matchId, waitedSec(b), now()));
                    waiting.remove(a.playerId);
                    waiting.remove(b.playerId);
                    found = true;
                    break;
                }
            }
        }
        // 2) 오래 기다린 사람은 봇과
        waiting.values().removeIf(t -> {
            if (waitedSec(t) < botTimeoutSeconds) {
                return false;
            }
            int botRating = Math.max(0, t.rating + (int) Math.round((Math.random() - 0.5) * 100));
            matched.put(t.playerId, new Result(null, "Bot (" + botRating + ")", botRating, true,
                    UUID.randomUUID().toString(), waitedSec(t), now()));
            return true;
        });
    }

    private void cleanUp() {
        Instant now = now();
        waiting.values().removeIf(t -> t.lastSeen.plus(STALE_AFTER).isBefore(now));
        matched.values().removeIf(r -> r.at.plus(KEEP_RESULT).isBefore(now));
    }

    private int waitedSec(Ticket t) {
        return (int) Duration.between(t.joinedAt, now()).getSeconds();
    }

    private int range(Ticket t) {
        return BASE_RANGE + waitedSec(t) * RANGE_PER_SECOND;
    }

    private Instant now() {
        return clock.get();
    }

    private static final class Ticket {
        final Long playerId;
        final String nickname;
        final int rating;
        final Instant joinedAt;
        Instant lastSeen;

        Ticket(Long playerId, String nickname, int rating, Instant joinedAt, Instant lastSeen) {
            this.playerId = playerId;
            this.nickname = nickname;
            this.rating = rating;
            this.joinedAt = joinedAt;
            this.lastSeen = lastSeen;
        }
    }

    private record Result(Long opponentId, String opponentNickname, int opponentRating, boolean bot,
                          String matchId, int waitedSec, Instant at) {
    }
}
