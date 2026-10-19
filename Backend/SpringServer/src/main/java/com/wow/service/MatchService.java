package com.wow.service;

import com.wow.common.ApiException;
import com.wow.config.WowProperties;
import com.wow.dto.match.MatchRecordRequest;
import com.wow.dto.match.MatchRecordResponse;
import com.wow.dto.match.MatchReportResponse;
import com.wow.dto.match.ServerMatchReport;
import com.wow.entity.Match;
import com.wow.entity.MatchMode;
import com.wow.entity.MatchOutcome;
import com.wow.entity.MatchPlayer;
import com.wow.entity.MatchSource;
import com.wow.entity.Player;
import com.wow.repository.MatchPlayerRepository;
import com.wow.repository.MatchRepository;
import com.wow.repository.PlayerRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.data.domain.PageRequest;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.Duration;
import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.UUID;
import java.util.stream.Collectors;

/**
 * 전적 기록 + 레이팅/경험치 반영.
 * - recordClientMatch : 웹 UI·Unity 가 자기 결과를 신고 (봇전/친선전, 프로토타입 랭크전) — 랭크전 ±16
 * - reportServerMatch : 전용 서버·Photon 이 양쪽 결과를 한 번에 보고 — 1:1 랭크전은 Elo
 */
@Service
@RequiredArgsConstructor
public class MatchService {

    private static final int MAX_DURATION_SEC = 7200;

    private final MatchRepository matchRepository;
    private final MatchPlayerRepository matchPlayerRepository;
    private final PlayerRepository playerRepository;
    private final PresenceService presenceService;
    private final RoomService roomService;
    private final WowProperties props;

    @Transactional
    public MatchRecordResponse recordClientMatch(Long playerId, MatchRecordRequest request) {
        MatchMode mode = MatchMode.from(request.mode(), MatchMode.BOT);
        MatchOutcome outcome = MatchOutcome.from(request.result());
        if (mode == MatchMode.RANKED && !props.match().allowClientRanked()) {
            throw ApiException.forbidden("랭크전 결과는 게임 서버만 기록할 수 있습니다.");
        }

        String token = hasText(request.matchToken()) ? request.matchToken().trim() : UUID.randomUUID().toString();
        Optional<Match> existing = matchRepository.findByMatchToken(token);
        if (existing.isPresent()) {
            // 같은 결과를 다시 보낸 경우(네트워크 재시도) → 처음 기록을 그대로 돌려준다
            return matchPlayerRepository.findByMatchIdAndPlayerId(existing.get().getId(), playerId)
                    .map(MatchService::toRecord)
                    .orElseThrow(() -> ApiException.conflict("이미 다른 경기에서 사용된 matchToken 입니다."));
        }

        Player me = lockPlayer(playerId);
        int step = props.rating().clientRankedStep();
        int delta = mode != MatchMode.RANKED ? 0 : switch (outcome) {
            case WIN -> step;
            case LOSE -> -step;
            case DRAW -> 0;
        };

        int ratingBefore = me.getRating();
        applyResult(me, outcome, delta);

        int duration = clamp(request.durationSec(), 0, MAX_DURATION_SEC);
        Instant endedAt = Instant.now().truncatedTo(ChronoUnit.MILLIS);
        Match match = new Match(token, mode, MatchSource.CLIENT, null, endedAt.minusSeconds(duration), endedAt, duration);
        if (outcome == MatchOutcome.WIN) {
            match.setWinner(me);
        }
        MatchPlayer record = new MatchPlayer(me, opponentName(request.opponent()), outcome,
                clamp(request.casts(), 0, 100_000), clamp(request.damageDealt(), 0, 1_000_000),
                clamp(request.damageTaken(), 0, 1_000_000), ratingBefore, me.getRating());
        match.addPlayer(record);
        matchRepository.save(match);
        return toRecord(record);
    }

    @Transactional
    public MatchReportResponse reportServerMatch(ServerMatchReport report, MatchSource source) {
        String token = report.matchToken().trim();
        Optional<Match> existing = matchRepository.findByMatchToken(token);
        if (existing.isPresent()) {
            return toReport(existing.get(), true);
        }

        MatchMode mode = MatchMode.from(report.mode(), MatchMode.RANKED);
        List<ServerMatchReport.PlayerStat> stats = report.players();
        Set<Long> ids = new LinkedHashSet<>();
        for (ServerMatchReport.PlayerStat stat : stats) {
            if (!ids.add(stat.playerId())) {
                throw ApiException.badRequest("같은 플레이어가 두 번 포함되어 있습니다: " + stat.playerId());
            }
        }
        // Unity JsonUtility 는 null 을 못 보내므로 0 이하도 무승부로 본다
        Long winnerId = report.winnerPlayerId() != null && report.winnerPlayerId() > 0 ? report.winnerPlayerId() : null;
        if (winnerId != null && !ids.contains(winnerId)) {
            throw ApiException.badRequest("승자(winnerPlayerId)가 참가자 목록에 없습니다.");
        }

        // 두 요청이 서로 반대 순서로 잠그다 멈추지 않도록(교착) ID 순서대로 잠근다
        Map<Long, Player> players = new HashMap<>();
        ids.stream().sorted().forEach(id -> players.put(id, lockPlayer(id)));

        Map<Long, Integer> deltas = computeDeltas(mode, stats, players, winnerId);

        Instant endedAt = report.endedAt() != null ? report.endedAt() : Instant.now().truncatedTo(ChronoUnit.MILLIS);
        int duration;
        if (report.durationSec() != null) {
            duration = clamp(report.durationSec(), 0, MAX_DURATION_SEC);
        } else if (report.startedAt() != null) {
            duration = clamp((int) Duration.between(report.startedAt(), endedAt).getSeconds(), 0, MAX_DURATION_SEC);
        } else {
            duration = 0;
        }
        Instant startedAt = report.startedAt() != null ? report.startedAt() : endedAt.minusSeconds(duration);
        String roomCode = RoomService.normalize(report.roomCode());
        if (roomCode != null && roomCode.length() > 20) {
            roomCode = roomCode.substring(0, 20);
        }

        Match match = new Match(token, mode, source, roomCode, startedAt, endedAt, duration);
        if (winnerId != null) {
            match.setWinner(players.get(winnerId));
        }
        for (ServerMatchReport.PlayerStat stat : stats) {
            Player player = players.get(stat.playerId());
            MatchOutcome outcome = winnerId == null ? MatchOutcome.DRAW
                    : winnerId.equals(player.getId()) ? MatchOutcome.WIN : MatchOutcome.LOSE;
            String opponents = stats.stream()
                    .filter(other -> !other.playerId().equals(player.getId()))
                    .map(other -> players.get(other.playerId()).getNickname())
                    .collect(Collectors.joining(", "));

            int ratingBefore = player.getRating();
            applyResult(player, outcome, deltas.get(player.getId()));
            match.addPlayer(new MatchPlayer(player, opponentName(opponents), outcome,
                    clamp(stat.casts(), 0, 100_000), clamp(stat.damageDealt(), 0, 1_000_000),
                    clamp(stat.damageTaken(), 0, 1_000_000), ratingBefore, player.getRating()));
            presenceService.update(player.getId(), PresenceService.ONLINE, null);
        }
        matchRepository.save(match);
        if (roomCode != null) {
            roomService.markClosed(roomCode);
        }
        return toReport(match, false);
    }

    @Transactional(readOnly = true)
    public List<MatchRecordResponse> getHistory(Long playerId, int limit) {
        if (!playerRepository.existsById(playerId)) {
            throw ApiException.notFound("플레이어를 찾을 수 없습니다.");
        }
        int safeLimit = Math.min(Math.max(limit, 1), 100);
        return matchPlayerRepository.findRecentByPlayerId(playerId, PageRequest.of(0, safeLimit)).stream()
                .map(MatchService::toRecord)
                .toList();
    }

    /** 1:1 랭크전 Elo 변동량. score: 승 1.0 / 무 0.5 / 패 0.0 */
    public static int eloDelta(int rating, int opponentRating, double score, int k) {
        double expected = 1.0 / (1.0 + Math.pow(10.0, (opponentRating - rating) / 400.0));
        return (int) Math.round(k * (score - expected));
    }

    private Map<Long, Integer> computeDeltas(MatchMode mode, List<ServerMatchReport.PlayerStat> stats,
                                             Map<Long, Player> players, Long winnerId) {
        Map<Long, Integer> deltas = new HashMap<>();
        if (mode != MatchMode.RANKED) {
            stats.forEach(stat -> deltas.put(stat.playerId(), 0));
            return deltas;
        }
        if (stats.size() == 2) {
            Player a = players.get(stats.get(0).playerId());
            Player b = players.get(stats.get(1).playerId());
            double scoreA = winnerId == null ? 0.5 : winnerId.equals(a.getId()) ? 1.0 : 0.0;
            int k = props.rating().eloK();
            deltas.put(a.getId(), eloDelta(a.getRating(), b.getRating(), scoreA, k));
            deltas.put(b.getId(), eloDelta(b.getRating(), a.getRating(), 1.0 - scoreA, k));
            return deltas;
        }
        int step = props.rating().clientRankedStep();
        for (ServerMatchReport.PlayerStat stat : stats) {
            int delta = winnerId == null ? 0 : winnerId.equals(stat.playerId()) ? step : -step;
            deltas.put(stat.playerId(), delta);
        }
        return deltas;
    }

    /** 승 +150 / 무 +100 / 패 +50 경험치, 1000 마다 레벨업, 레이팅은 0 미만으로 내려가지 않음 */
    private static void applyResult(Player player, MatchOutcome outcome, int ratingDelta) {
        switch (outcome) {
            case WIN -> {
                player.setWins(player.getWins() + 1);
                player.setExperience(player.getExperience() + 150);
            }
            case LOSE -> {
                player.setLosses(player.getLosses() + 1);
                player.setExperience(player.getExperience() + 50);
            }
            case DRAW -> {
                player.setDraws(player.getDraws() + 1);
                player.setExperience(player.getExperience() + 100);
            }
        }
        player.setLevel(1 + player.getExperience() / 1000);
        player.setRating(Math.max(0, player.getRating() + ratingDelta));
    }

    private Player lockPlayer(Long playerId) {
        return playerRepository.findByIdForUpdate(playerId)
                .orElseThrow(() -> ApiException.notFound("플레이어를 찾을 수 없습니다: " + playerId));
    }

    public static MatchRecordResponse toRecord(MatchPlayer mp) {
        Match m = mp.getMatch();
        return new MatchRecordResponse(m.getId(), m.getMatchToken(), m.getMode().lower(), mp.getOutcome().lower(),
                mp.getOpponentName(), m.getDurationSec(), mp.getCasts(), mp.getDamageDealt(), mp.getDamageTaken(),
                mp.getRatingBefore(), mp.getRatingAfter(), mp.getRatingDelta(), m.getEndedAt());
    }

    private static MatchReportResponse toReport(Match match, boolean duplicate) {
        List<MatchReportResponse.PlayerResult> results = match.getPlayers().stream()
                .map(mp -> new MatchReportResponse.PlayerResult(mp.getPlayer().getId(), mp.getPlayer().getNickname(),
                        mp.getOutcome().lower(), mp.getRatingBefore(), mp.getRatingAfter(), mp.getRatingDelta()))
                .toList();
        return new MatchReportResponse(match.getId(), match.getMatchToken(), match.getMode().lower(), duplicate, results);
    }

    private static String opponentName(String name) {
        String trimmed = name == null || name.isBlank() ? "?" : name.trim();
        return trimmed.length() > 30 ? trimmed.substring(0, 30) : trimmed;
    }

    private static int clamp(Integer value, int min, int max) {
        if (value == null) {
            return min;
        }
        return Math.max(min, Math.min(max, value));
    }

    private static boolean hasText(String s) {
        return s != null && !s.isBlank();
    }
}
