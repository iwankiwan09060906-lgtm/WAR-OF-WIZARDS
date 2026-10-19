package com.wow.controller;

import com.wow.dto.common.ActionResponse;
import com.wow.dto.friend.FriendRequest;
import com.wow.dto.friend.FriendResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.FriendService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

import java.util.List;

/** 친구 (캔버스 5). {id} 는 상대 playerId. */
@RestController
@RequestMapping("/api/friends")
@RequiredArgsConstructor
public class FriendController {

    private final FriendService friendService;

    @GetMapping
    public ResponseEntity<List<FriendResponse>> list(@AuthenticationPrincipal UserPrincipal me) {
        return ResponseEntity.ok(friendService.list(me.playerId()));
    }

    @PostMapping("/requests")
    public ResponseEntity<ActionResponse> request(
            @AuthenticationPrincipal UserPrincipal me,
            @Valid @RequestBody FriendRequest request
    ) {
        return ResponseEntity.ok(friendService.request(me.playerId(), request.nickname()));
    }

    @PostMapping("/{id}/accept")
    public ResponseEntity<ActionResponse> accept(@AuthenticationPrincipal UserPrincipal me, @PathVariable Long id) {
        return ResponseEntity.ok(friendService.accept(me.playerId(), id));
    }

    @PostMapping("/{id}/reject")
    public ResponseEntity<ActionResponse> reject(@AuthenticationPrincipal UserPrincipal me, @PathVariable Long id) {
        return ResponseEntity.ok(friendService.reject(me.playerId(), id));
    }

    /** 친구 삭제 또는 보낸 요청 취소 */
    @DeleteMapping("/{id}")
    public ResponseEntity<ActionResponse> remove(@AuthenticationPrincipal UserPrincipal me, @PathVariable Long id) {
        return ResponseEntity.ok(friendService.remove(me.playerId(), id));
    }
}
