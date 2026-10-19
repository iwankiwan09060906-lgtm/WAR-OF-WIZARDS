package com.wow.controller;

import com.wow.dto.presence.PresenceResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.FriendService;
import com.wow.service.PresenceService;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

/**
 * 접속 상태. 클라이언트는 20초마다 heartbeat 를 보내고, 응답으로 접속 중인 친구 목록을 받는다.
 * (WebSocket 없이 동작하므로 Unity WebGL / Quest / 웹 모두 같은 방식으로 쓸 수 있다)
 */
@RestController
@RequestMapping("/api/presence")
@RequiredArgsConstructor
public class PresenceController {

    private final PresenceService presenceService;
    private final FriendService friendService;

    @PostMapping("/heartbeat")
    public ResponseEntity<PresenceResponse> heartbeat(@AuthenticationPrincipal UserPrincipal me) {
        presenceService.heartbeat(me.playerId());
        return ResponseEntity.ok(current(me.playerId()));
    }

    @GetMapping("/friends")
    public ResponseEntity<PresenceResponse> friends(@AuthenticationPrincipal UserPrincipal me) {
        return ResponseEntity.ok(current(me.playerId()));
    }

    private PresenceResponse current(Long playerId) {
        return new PresenceResponse(presenceService.statusOf(playerId),
                presenceService.onlineAmong(friendService.friendIds(playerId)));
    }
}
