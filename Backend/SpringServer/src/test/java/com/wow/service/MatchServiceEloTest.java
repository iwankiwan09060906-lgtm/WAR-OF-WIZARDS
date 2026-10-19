package com.wow.service;

import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

class MatchServiceEloTest {

    @Test
    void equalRatingsMoveByHalfK() {
        assertThat(MatchService.eloDelta(1000, 1000, 1.0, 32)).isEqualTo(16);
        assertThat(MatchService.eloDelta(1000, 1000, 0.0, 32)).isEqualTo(-16);
        assertThat(MatchService.eloDelta(1000, 1000, 0.5, 32)).isZero();
    }

    @Test
    void upsetGivesMorePoints() {
        int underdogWin = MatchService.eloDelta(1000, 1400, 1.0, 32);
        int favoriteWin = MatchService.eloDelta(1400, 1000, 1.0, 32);
        assertThat(underdogWin).isGreaterThan(favoriteWin);
        assertThat(underdogWin).isEqualTo(29);
        assertThat(favoriteWin).isEqualTo(3);
    }
}
