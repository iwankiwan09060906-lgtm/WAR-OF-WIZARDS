package com.wow.controller;

import com.wow.dto.player.NicknameUpdateRequest;
import com.wow.dto.player.ProfileResponse;
import com.wow.dto.player.RankEntryResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.PlayerService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/players")
@RequiredArgsConstructor
public class PlayerController {

    private final PlayerService playerService;

    @GetMapping("/me")
    public ResponseEntity<ProfileResponse> getMyProfile(@AuthenticationPrincipal UserPrincipal me) {
        return ResponseEntity.ok(playerService.getProfile(me.playerId()));
    }

    @PutMapping("/me")
    public ResponseEntity<ProfileResponse> updateMyProfile(
            @AuthenticationPrincipal UserPrincipal me,
            @Valid @RequestBody NicknameUpdateRequest request
    ) {
        return ResponseEntity.ok(playerService.updateNickname(me.playerId(), request.nickname()));
    }

    @GetMapping("/ranking")
    public ResponseEntity<List<RankEntryResponse>> getRanking(@RequestParam(defaultValue = "20") int limit) {
        return ResponseEntity.ok(playerService.getRanking(limit));
    }

    @GetMapping("/{id}")
    public ResponseEntity<ProfileResponse> getPlayer(@PathVariable Long id) {
        return ResponseEntity.ok(playerService.getProfile(id));
    }
}
