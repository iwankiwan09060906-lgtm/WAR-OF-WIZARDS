package com.wow.controller;

import com.wow.dto.motion.MotionRecordingRequest;
import com.wow.dto.motion.MotionRecordingResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.MotionService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

import java.util.List;

/**
 * VR 손 모션 녹화 (모션 DB).
 *  - Unity(로그인):  POST /api/motions
 *  - Python(서버 키): GET  /api/server/motions?label=&afterId=0&limit=100
 */
@RestController
@RequiredArgsConstructor
public class MotionController {

    private final MotionService motionService;

    @PostMapping("/api/motions")
    public ResponseEntity<MotionRecordingResponse> upload(
            @AuthenticationPrincipal UserPrincipal me,
            @Valid @RequestBody MotionRecordingRequest request,
            @RequestHeader(value = "X-Client", required = false) String client
    ) {
        String source = client == null || client.isBlank() ? "unity" : client.trim().toLowerCase();
        return ResponseEntity.ok(motionService.save(me.playerId(), request, source.length() > 20 ? source.substring(0, 20) : source));
    }

    @GetMapping("/api/server/motions")
    public ResponseEntity<List<MotionRecordingResponse>> export(
            @RequestParam(required = false) String label,
            @RequestParam(defaultValue = "0") long afterId,
            @RequestParam(defaultValue = "100") int limit
    ) {
        return ResponseEntity.ok(motionService.export(label, afterId, limit));
    }
}
