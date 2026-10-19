package com.wow.controller;

import com.wow.dto.common.ActionResponse;
import com.wow.dto.mail.InviteRequest;
import com.wow.dto.mail.MailResponse;
import com.wow.dto.mail.UnreadCountResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.MailService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

import java.util.List;

/** 우편함·공지·친선전 초대 (캔버스 6·7). {id} 는 "mail-12" / "notice-3" 형태. */
@RestController
@RequestMapping("/api/mails")
@RequiredArgsConstructor
public class MailController {

    private final MailService mailService;

    @GetMapping
    public ResponseEntity<List<MailResponse>> list(@AuthenticationPrincipal UserPrincipal me) {
        return ResponseEntity.ok(mailService.list(me.playerId()));
    }

    @GetMapping("/unread-count")
    public ResponseEntity<UnreadCountResponse> unreadCount(@AuthenticationPrincipal UserPrincipal me) {
        return ResponseEntity.ok(new UnreadCountResponse(mailService.unreadCount(me.playerId())));
    }

    @PostMapping("/{id}/read")
    public ResponseEntity<ActionResponse> markRead(@AuthenticationPrincipal UserPrincipal me, @PathVariable String id) {
        return ResponseEntity.ok(mailService.markRead(me.playerId(), id));
    }

    @DeleteMapping("/{id}")
    public ResponseEntity<ActionResponse> delete(@AuthenticationPrincipal UserPrincipal me, @PathVariable String id) {
        return ResponseEntity.ok(mailService.delete(me.playerId(), id));
    }

    @PostMapping("/invite")
    public ResponseEntity<ActionResponse> invite(
            @AuthenticationPrincipal UserPrincipal me,
            @Valid @RequestBody InviteRequest request
    ) {
        return ResponseEntity.ok(mailService.sendInvite(me.playerId(), request.friendId(), request.roomCode()));
    }
}
