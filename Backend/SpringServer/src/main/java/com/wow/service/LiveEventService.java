package com.wow.service;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.wow.dto.live.LiveDtos.LiveEvent;
import com.wow.dto.live.LiveDtos.LiveStatus;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;
import org.springframework.transaction.support.TransactionSynchronization;
import org.springframework.transaction.support.TransactionSynchronizationManager;
import org.springframework.web.servlet.mvc.method.annotation.SseEmitter;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * 서버 → 클라이언트(Unity / 웹) 실시간 전송. Server-Sent Events(SSE) 사용.
 *
 *  클라이언트: GET /api/live/events?access_token=JWT  (연결을 계속 열어 두고 이벤트를 받는다)
 *  서버 코드: liveEventService.sendToPlayer(playerId, "mail", "새 우편", "...", payloadObject)
 *             liveEventService.broadcast("config", ...)
 *
 * DB 에 저장한 뒤 보내야 클라이언트가 바로 다시 조회해도 새 데이터가 보이므로,
 * 트랜잭션 안에서 부르면 커밋이 끝난 뒤에 전송한다.
 * 서버를 여러 대로 늘리면 Redis Pub/Sub 등으로 서버끼리 이벤트를 나눠야 한다.
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class LiveEventService {

    /** 연결 유지 시간. 끊기면 클라이언트가 자동으로 다시 연결한다 */
    private static final long TIMEOUT_MS = 30 * 60 * 1000L;

    private final ObjectMapper objectMapper;
    private final Map<Long, List<SseEmitter>> emitters = new ConcurrentHashMap<>();

    /** 새 연결. 연결 직후 hello 이벤트를 보낸다 */
    public SseEmitter connect(Long playerId) {
        SseEmitter emitter = new SseEmitter(TIMEOUT_MS);
        List<SseEmitter> list = emitters.computeIfAbsent(playerId, id -> new CopyOnWriteArrayList<>());
        list.add(emitter);
        Runnable remove = () -> {
            list.remove(emitter);
            emitters.computeIfPresent(playerId, (id, l) -> l.isEmpty() ? null : l);
        };
        emitter.onCompletion(remove);
        emitter.onTimeout(() -> {
            remove.run();
            emitter.complete();
        });
        emitter.onError(e -> remove.run());
        send(emitter, event("hello", "연결됨", "실시간 데이터 연결에 성공했습니다.", null));
        return emitter;
    }

    public void sendToPlayer(Long playerId, String type, String title, String message, Object payload) {
        LiveEvent e = event(type, title, message, payload);
        afterCommit(() -> emitters.getOrDefault(playerId, List.of()).forEach(em -> send(em, e)));
    }

    public void broadcast(String type, String title, String message, Object payload) {
        LiveEvent e = event(type, title, message, payload);
        afterCommit(() -> emitters.values().forEach(list -> list.forEach(em -> send(em, e))));
    }

    public LiveStatus status() {
        List<Long> ids = new ArrayList<>(emitters.keySet());
        int connections = emitters.values().stream().mapToInt(List::size).sum();
        return new LiveStatus(ids.size(), connections, ids);
    }

    /** 25초마다 빈 주석을 보내 프록시/브라우저가 연결을 끊지 않게 하고, 끊긴 연결을 정리한다 */
    @Scheduled(fixedDelay = 25_000)
    public void keepAlive() {
        emitters.values().forEach(list -> list.forEach(em -> {
            try {
                em.send(SseEmitter.event().comment("ping"));
            } catch (Exception ex) {
                list.remove(em);
            }
        }));
    }

    // ── 내부 ─────────────────────────────────────────────────────────────

    private LiveEvent event(String type, String title, String message, Object payload) {
        String json = null;
        if (payload != null) {
            try {
                json = payload instanceof String s ? s : objectMapper.writeValueAsString(payload);
            } catch (JsonProcessingException ex) {
                log.warn("실시간 이벤트 payload 변환 실패: {}", ex.getMessage());
            }
        }
        return new LiveEvent(type, title, message, json, System.currentTimeMillis());
    }

    private void send(SseEmitter emitter, LiveEvent e) {
        try {
            // 이벤트 이름을 붙이지 않는다 → 브라우저 EventSource.onmessage / Unity 가 모든 종류를 한 곳에서 받고 data.type 으로 구분
            emitter.send(SseEmitter.event().data(objectMapper.writeValueAsString(e)));
        } catch (IOException | IllegalStateException ex) {
            emitter.completeWithError(ex);
        }
    }

    private static void afterCommit(Runnable task) {
        if (TransactionSynchronizationManager.isSynchronizationActive()) {
            TransactionSynchronizationManager.registerSynchronization(new TransactionSynchronization() {
                @Override
                public void afterCommit() {
                    task.run();
                }
            });
        } else {
            task.run();
        }
    }
}
