package com.wow.dto.match;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;

import java.time.Instant;
import java.util.List;

/**
 * 게임 서버(Photon 전용 서버 / 방장 클라이언트의 WebRPC)가 보내는 경기 결과.
 * winnerPlayerId 가 null 이면 무승부. 1:1 랭크전이면 Elo 로 레이팅을 계산한다.
 */
public record ServerMatchReport(
        @NotBlank(message = "matchToken 이 필요합니다.")
        @Size(max = 64, message = "matchToken 은 64자 이하여야 합니다.")
        String matchToken,
        String mode,
        String roomCode,
        Long winnerPlayerId,
        Integer durationSec,
        Instant startedAt,
        Instant endedAt,
        @NotEmpty(message = "참가자 정보(players)가 필요합니다.")
        @Size(max = 8, message = "참가자는 최대 8명입니다.")
        List<@Valid PlayerStat> players
) {

    public record PlayerStat(
            @NotNull(message = "playerId 가 필요합니다.") Long playerId,
            Integer casts,
            Integer damageDealt,
            Integer damageTaken
    ) {
    }
}
