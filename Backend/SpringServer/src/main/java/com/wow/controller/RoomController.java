package com.wow.controller;

import com.wow.dto.room.RoomCreateRequest;
import com.wow.dto.room.RoomResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.RoomService;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

/** 친선전 룸 코드 (Photon 룸 이름으로 사용) */
@RestController
@RequestMapping("/api/rooms")
@RequiredArgsConstructor
public class RoomController {

    private final RoomService roomService;

    @PostMapping
    public ResponseEntity<RoomResponse> create(
            @AuthenticationPrincipal UserPrincipal me,
            @RequestBody(required = false) RoomCreateRequest request
    ) {
        return ResponseEntity.ok(roomService.create(me.playerId(), request == null ? null : request.mode()));
    }

    @GetMapping("/{code}")
    public ResponseEntity<RoomResponse> get(@PathVariable String code) {
        return ResponseEntity.ok(roomService.get(code));
    }

    @PostMapping("/{code}/close")
    public ResponseEntity<RoomResponse> close(@AuthenticationPrincipal UserPrincipal me, @PathVariable String code) {
        return ResponseEntity.ok(roomService.close(me.playerId(), code));
    }

    // ── 웹 대기실 ──

    @PostMapping("/{code}/join")
    public ResponseEntity<RoomResponse> join(@AuthenticationPrincipal UserPrincipal me, @PathVariable String code) {
        return ResponseEntity.ok(roomService.join(me.playerId(), code));
    }

    @PostMapping("/{code}/leave")
    public ResponseEntity<RoomResponse> leave(@AuthenticationPrincipal UserPrincipal me, @PathVariable String code) {
        return ResponseEntity.ok(roomService.leave(me.playerId(), code));
    }

    @PostMapping("/{code}/ready")
    public ResponseEntity<RoomResponse> ready(
            @AuthenticationPrincipal UserPrincipal me,
            @PathVariable String code,
            @RequestParam(defaultValue = "true") boolean ready
    ) {
        return ResponseEntity.ok(roomService.ready(me.playerId(), code, ready));
    }

    @PostMapping("/{code}/kick")
    public ResponseEntity<RoomResponse> kick(@AuthenticationPrincipal UserPrincipal me, @PathVariable String code) {
        return ResponseEntity.ok(roomService.kick(me.playerId(), code));
    }

    @PostMapping("/{code}/start")
    public ResponseEntity<RoomResponse> start(@AuthenticationPrincipal UserPrincipal me, @PathVariable String code) {
        return ResponseEntity.ok(roomService.start(me.playerId(), code));
    }
}
