package com.wow.service;

import com.wow.dto.matchmaking.MatchmakingStatus;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;

import static org.assertj.core.api.Assertions.assertThat;

class MatchmakingServiceTest {

    private MatchmakingService service;
    private Instant now;

    @BeforeEach
    void setUp() {
        service = new MatchmakingService(30);
        now = Instant.parse("2026-01-01T00:00:00Z");
        service.setClock(() -> now);
    }

    private void wait(int seconds) {
        now = now.plusSeconds(seconds);
    }

    @Test
    void similarRatingsMatchImmediately() {
        assertThat(service.join(1L, "A", 1000).state()).isEqualTo("searching");
        MatchmakingStatus b = service.join(2L, "B", 1040);
        assertThat(b.state()).isEqualTo("matched");
        assertThat(b.opponentNickname()).isEqualTo("A");
        assertThat(b.bot()).isFalse();

        MatchmakingStatus a = service.poll(1L);
        assertThat(a.state()).isEqualTo("matched");
        assertThat(a.opponentPlayerId()).isEqualTo(2L);
        assertThat(a.matchId()).isEqualTo(b.matchId());
    }

    @Test
    void rangeWidensOverTime() {
        service.join(1L, "A", 1000);
        service.join(2L, "B", 1200);                  // 차이 200 > 50
        assertThat(service.poll(1L).state()).isEqualTo("searching");
        wait(9);                                      // 50 + 9*15 = 185 → 아직
        service.poll(2L);
        assertThat(service.poll(1L).state()).isEqualTo("searching");
        wait(1);                                      // 50 + 10*15 = 200 → 매칭
        assertThat(service.poll(1L).opponentNickname()).isEqualTo("B");
    }

    @Test
    void botAfterTimeoutAndCancel() {
        service.join(1L, "A", 1000);
        for (int i = 0; i < 15; i++) { wait(2); service.poll(1L); }
        MatchmakingStatus s = service.poll(1L);
        assertThat(s.state()).isEqualTo("matched");
        assertThat(s.bot()).isTrue();
        assertThat(s.opponentPlayerId()).isNull();

        service.join(3L, "C", 1000);
        service.cancel(3L);
        assertThat(service.poll(3L).state()).isEqualTo("none");
    }

    @Test
    void closedWindowIsRemoved() {
        service.join(1L, "A", 1000);
        wait(11);                                     // 10초 넘게 상태 확인 없음
        service.join(2L, "B", 1000);
        assertThat(service.poll(2L).state()).isEqualTo("searching");
        assertThat(service.poll(1L).state()).isEqualTo("none");
    }
}
