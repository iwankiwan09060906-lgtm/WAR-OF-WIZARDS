package com.wow.controller;

import com.wow.dto.match.MatchRecordRequest;
import com.wow.dto.match.MatchRecordResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.MatchService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

import java.util.List;

/** 전적 (캔버스 3·13). 서버 판정 결과는 /api/server/matches, Photon 은 /api/photon/webhooks 사용. */
@RestController
@RequestMapping("/api/matches")
@RequiredArgsConstructor
public class MatchController {

    private final MatchService matchService;

    @PostMapping
    public ResponseEntity<MatchRecordResponse> recordMatch(
            @AuthenticationPrincipal UserPrincipal me,
            @Valid @RequestBody MatchRecordRequest request
    ) {
        return ResponseEntity.ok(matchService.recordClientMatch(me.playerId(), request));
    }

    @GetMapping("/me")
    public ResponseEntity<List<MatchRecordResponse>> getMyMatches(
            @AuthenticationPrincipal UserPrincipal me,
            @RequestParam(defaultValue = "10") int limit
    ) {
        return ResponseEntity.ok(matchService.getHistory(me.playerId(), limit));
    }

    @GetMapping("/player/{playerId}")
    public ResponseEntity<List<MatchRecordResponse>> getPlayerMatches(
            @PathVariable Long playerId,
            @RequestParam(defaultValue = "10") int limit
    ) {
        return ResponseEntity.ok(matchService.getHistory(playerId, limit));
    }
}
