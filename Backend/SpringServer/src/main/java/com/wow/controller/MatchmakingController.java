package com.wow.controller;

import com.wow.dto.matchmaking.MatchmakingStatus;
import com.wow.dto.player.ProfileResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.MatchmakingService;
import com.wow.service.PlayerService;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

/** 랭킹전 매칭 대기열 (캔버스 10). 클라이언트는 참가 후 2초마다 status 를 확인한다. */
@RestController
@RequestMapping("/api/matchmaking")
@RequiredArgsConstructor
public class MatchmakingController {

    private final MatchmakingService matchmakingService;
    private final PlayerService playerService;

    @PostMapping("/queue")
    public ResponseEntity<MatchmakingStatus> join(@AuthenticationPrincipal UserPrincipal me) {
        ProfileResponse p = playerService.getProfile(me.playerId());
        return ResponseEntity.ok(matchmakingService.join(p.id(), p.nickname(), p.rating()));
    }

    @GetMapping("/status")
    public ResponseEntity<MatchmakingStatus> status(@AuthenticationPrincipal UserPrincipal me) {
        return ResponseEntity.ok(matchmakingService.poll(me.playerId()));
    }

    @DeleteMapping("/queue")
    public ResponseEntity<MatchmakingStatus> cancel(@AuthenticationPrincipal UserPrincipal me) {
        matchmakingService.cancel(me.playerId());
        return ResponseEntity.ok(matchmakingService.poll(me.playerId()));
    }
}
