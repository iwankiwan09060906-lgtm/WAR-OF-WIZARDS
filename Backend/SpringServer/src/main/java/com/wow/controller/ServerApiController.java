package com.wow.controller;

import com.wow.dto.common.ActionResponse;
import com.wow.dto.gesture.GestureSampleRequest;
import com.wow.dto.gesture.GestureSampleResponse;
import com.wow.dto.gesture.ModelInfoResponse;
import com.wow.dto.match.MatchReportResponse;
import com.wow.dto.match.ServerMatchReport;
import com.wow.dto.presence.PresenceUpdateRequest;
import com.wow.dto.server.ServerPlayerInfo;
import com.wow.dto.server.TokenVerifyRequest;
import com.wow.dto.server.TokenVerifyResponse;
import com.wow.entity.MatchSource;
import com.wow.service.GestureService;
import com.wow.service.MatchService;
import com.wow.service.PlayerService;
import com.wow.service.PresenceService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.multipart.MultipartFile;

import java.util.List;

/**
 * 게임 서버 전용 API — 헤더 X-Server-Key 필요 (application.yml wow.server-api-key).
 * Photon Fusion 전용 서버 / 헤드리스 Unity 서버가 "권위 판정" 결과를 기록할 때 사용한다.
 */
@RestController
@RequestMapping("/api/server")
@RequiredArgsConstructor
public class ServerApiController {

    private final PlayerService playerService;
    private final MatchService matchService;
    private final PresenceService presenceService;
    private final GestureService gestureService;

    /** 경기 시작 시 플레이어 정보 + 덱 조회 */
    @GetMapping("/players/{id}")
    public ResponseEntity<ServerPlayerInfo> getPlayer(@PathVariable Long id) {
        return ResponseEntity.ok(playerService.getServerInfo(id));
    }

    /** 경기 결과 기록 (1:1 랭크전은 Elo). 같은 matchToken 재전송은 duplicate=true 로 무시된다. */
    @PostMapping("/matches")
    public ResponseEntity<MatchReportResponse> reportMatch(@Valid @RequestBody ServerMatchReport report) {
        return ResponseEntity.ok(matchService.reportServerMatch(report, MatchSource.SERVER));
    }

    /** 접속한 클라이언트가 보낸 로그인 토큰이 진짜인지 확인 (Fusion ConnectionToken 등에 JWT 를 실어 보낼 때) */
    @PostMapping("/tokens/verify")
    public ResponseEntity<TokenVerifyResponse> verifyToken(@Valid @RequestBody TokenVerifyRequest request) {
        return ResponseEntity.ok(playerService.verifyToken(request.token()));
    }

    // ── Python 학습 스크립트용 (AI/Tools/wow_api.py) ─────────────────────

    /** 학습 데이터 내보내기: afterId 다음부터 limit 개씩 (다 받을 때까지 반복) */
    @GetMapping("/gestures/samples")
    public ResponseEntity<List<GestureSampleResponse>> exportGestures(
            @RequestParam(required = false) String label,
            @RequestParam(defaultValue = "0") long afterId,
            @RequestParam(defaultValue = "500") int limit
    ) {
        return ResponseEntity.ok(gestureService.export(label, afterId, limit));
    }

    /** 학습 결과 ONNX 업로드 → Unity 가 /api/gestures/model 로 받아간다 */
    @PostMapping(value = "/gestures/model", consumes = MediaType.MULTIPART_FORM_DATA_VALUE)
    public ResponseEntity<ModelInfoResponse> uploadModel(@RequestPart("file") MultipartFile file) {
        return ResponseEntity.ok(gestureService.uploadModel(file));
    }

    /** Python 도구가 서버 키로 샘플을 직접 올릴 때 (합성 데이터, 다른 기기에서 모은 데이터 등) */
    @PostMapping("/gestures/samples")
    public ResponseEntity<GestureSampleResponse> importGesture(@Valid @RequestBody GestureSampleRequest request) {
        return ResponseEntity.ok(gestureService.addSample(null, request, "python"));
    }

    @PutMapping("/presence/{playerId}")
    public ResponseEntity<ActionResponse> updatePresence(
            @PathVariable Long playerId,
            @Valid @RequestBody PresenceUpdateRequest request
    ) {
        presenceService.update(playerId, request.status(), request.roomCode());
        return ResponseEntity.ok(ActionResponse.ok("상태를 변경했습니다."));
    }
}
