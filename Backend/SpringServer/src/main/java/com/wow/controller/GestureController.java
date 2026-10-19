package com.wow.controller;

import com.wow.dto.gesture.GestureLabelStat;
import com.wow.dto.gesture.GestureSampleRequest;
import com.wow.dto.gesture.GestureSampleResponse;
import com.wow.dto.gesture.ModelInfoResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.GestureService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.core.io.FileSystemResource;
import org.springframework.core.io.Resource;
import org.springframework.http.ContentDisposition;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

import java.nio.file.Path;
import java.util.List;

/**
 * 룬 제스처 AI — 샘플 업로드(Unity), 수집 현황, 학습된 ONNX 모델 다운로드.
 * Python 학습 스크립트용 내보내기/모델 업로드는 ServerApiController(/api/server/gestures/**) 에 있다.
 */
@RestController
@RequestMapping("/api/gestures")
@RequiredArgsConstructor
public class GestureController {

    private final GestureService gestureService;

    @PostMapping("/samples")
    public ResponseEntity<GestureSampleResponse> upload(
            @AuthenticationPrincipal UserPrincipal me,
            @Valid @RequestBody GestureSampleRequest request,
            @RequestHeader(value = "X-Client", required = false) String client
    ) {
        String source = client == null || client.isBlank() ? "unity" : client.trim();
        return ResponseEntity.ok(gestureService.addSample(me.playerId(), request, source));
    }

    @GetMapping("/stats")
    public ResponseEntity<List<GestureLabelStat>> stats() {
        return ResponseEntity.ok(gestureService.stats());
    }

    @GetMapping("/model/info")
    public ResponseEntity<ModelInfoResponse> modelInfo() {
        return ResponseEntity.ok(gestureService.modelInfo());
    }

    /** ONNX (Unity Sentis / onnxruntime 용) */
    @GetMapping("/model")
    public ResponseEntity<Resource> downloadModel() {
        return file(gestureService.modelFileOrThrow(), MediaType.APPLICATION_OCTET_STREAM);
    }

    /** 가중치 JSON (Unity 순수 C# 추론 GestureLstmClassifier 용) */
    @GetMapping("/model/weights")
    public ResponseEntity<Resource> downloadWeights() {
        return file(gestureService.weightsFileOrThrow(), MediaType.APPLICATION_JSON);
    }

    private static ResponseEntity<Resource> file(Path path, MediaType type) {
        HttpHeaders headers = new HttpHeaders();
        headers.setContentType(type);
        headers.setContentDisposition(ContentDisposition.attachment().filename(path.getFileName().toString()).build());
        return ResponseEntity.ok().headers(headers).body(new FileSystemResource(path));
    }
}
