package com.wow.controller;

import com.wow.service.PhotonService;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.HashMap;
import java.util.Map;

/**
 * Photon Cloud 가 호출하는 주소 (로그인 불필요, 토큰·비밀키는 PhotonService 가 검사).
 *
 * Photon Dashboard 설정 예 (서버 주소가 https://my-server.com 일 때)
 *  - Authentication > Custom Server : https://my-server.com/api/photon/auth
 *  - Webhooks BaseUrl               : https://my-server.com/api/photon/webhooks
 *      PathCreate=GameCreate, PathJoin=GameJoin, PathLeave=GameLeave, PathClose=GameClose, PathEvent=GameEvent
 *      CustomHttpHeaders = {"X-Webhook-Secret":"<wow.photon.webhook-secret>"}
 * 항상 HTTP 200 + {"ResultCode":..} 로 응답해야 Photon 이 결과를 해석한다.
 */
@RestController
@RequestMapping("/api/photon")
@RequiredArgsConstructor
public class PhotonController {

    private final PhotonService photonService;

    @GetMapping("/auth")
    public ResponseEntity<Map<String, Object>> authGet(@RequestParam Map<String, String> params) {
        return ResponseEntity.ok(photonService.authenticate(params));
    }

    /** AuthPostData 로 JSON 을 보낸 경우 (query string 값과 합쳐서 처리) */
    @PostMapping("/auth")
    public ResponseEntity<Map<String, Object>> authPost(
            @RequestParam Map<String, String> params,
            @RequestBody(required = false) Map<String, Object> body
    ) {
        Map<String, String> merged = new HashMap<>(params);
        if (body != null) {
            body.forEach((key, value) -> {
                if (value != null) {
                    merged.putIfAbsent(key, String.valueOf(value));
                }
            });
        }
        return ResponseEntity.ok(photonService.authenticate(merged));
    }

    @PostMapping("/webhooks/{path}")
    public ResponseEntity<Map<String, Object>> webhook(
            @PathVariable String path,
            @RequestBody(required = false) Map<String, Object> body,
            @RequestHeader(value = "X-Webhook-Secret", required = false) String headerSecret,
            @RequestParam(value = "key", required = false) String querySecret
    ) {
        return ResponseEntity.ok(photonService.handleWebhook(path, body, headerSecret, querySecret));
    }
}
