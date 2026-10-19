package com.wow.controller;

import com.wow.dto.friend.FriendResponse;
import com.wow.dto.live.LiveDtos.Bootstrap;
import com.wow.dto.live.LiveDtos.ConfigEntry;
import com.wow.security.UserPrincipal;
import com.wow.service.*;
import lombok.RequiredArgsConstructor;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.mvc.method.annotation.SseEmitter;

import java.util.List;

/**
 * DB → Unity / 웹 데이터 전달
 *
 *  GET /api/live/bootstrap   게임 시작 시 한 번: 프로필·덱·스킬·설정·안 읽은 우편·친구를 한 번에
 *  GET /api/live/config      게임 설정만 (game_configs)
 *  GET /api/live/events      실시간 이벤트 스트림 (SSE). 헤더를 못 붙이는 곳(브라우저 EventSource)은 ?access_token=JWT
 *
 * Unity 사용법: UnityXR/Assets/_Project/API/LiveApi.cs, Network/LiveEventStream.cs
 */
@RestController
@RequestMapping("/api/live")
@RequiredArgsConstructor
public class LiveController {

    private final PlayerService playerService;
    private final DeckService deckService;
    private final SkillService skillService;
    private final GameConfigService gameConfigService;
    private final MailService mailService;
    private final FriendService friendService;
    private final PresenceService presenceService;
    private final LiveEventService liveEventService;

    @GetMapping("/bootstrap")
    public ResponseEntity<Bootstrap> bootstrap(@AuthenticationPrincipal UserPrincipal me) {
        Long id = me.playerId();
        List<FriendResponse> friends = friendService.list(id);
        return ResponseEntity.ok(new Bootstrap(
                System.currentTimeMillis(),
                playerService.getProfile(id),
                deckService.getDeck(id).skills(),
                skillService.getAll(),
                gameConfigService.list(),
                mailService.unreadCount(id),
                friends,
                presenceService.onlineAmong(friendService.friendIds(id)),
                "/api/live/events"));
    }

    @GetMapping("/config")
    public ResponseEntity<List<ConfigEntry>> config() {
        return ResponseEntity.ok(gameConfigService.list());
    }

    @GetMapping(value = "/events", produces = MediaType.TEXT_EVENT_STREAM_VALUE)
    public SseEmitter events(@AuthenticationPrincipal UserPrincipal me) {
        presenceService.heartbeat(me.playerId());
        return liveEventService.connect(me.playerId());
    }
}
