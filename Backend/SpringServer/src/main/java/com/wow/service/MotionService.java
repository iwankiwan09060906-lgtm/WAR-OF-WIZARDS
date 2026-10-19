package com.wow.service;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.wow.common.ApiException;
import com.wow.config.MotionDbConfig;
import com.wow.dto.motion.MotionRecordingRequest;
import com.wow.dto.motion.MotionRecordingResponse;
import com.wow.motion.entity.MotionRecording;
import com.wow.motion.repository.MotionRecordingRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.data.domain.PageRequest;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * VR 손 모션 녹화 저장/조회 — 전부 모션 DB(WOW_MOTION) 를 쓴다.
 * Unity: POST /api/motions  →  Python: GET /api/server/motions (서버 키)
 */
@Service
@RequiredArgsConstructor
public class MotionService {

    private static final Set<String> HANDS = Set.of("left", "right", "both");
    private static final int MAX_JOINTS = 64;
    private static final int MAX_VALUES = 200_000;   // 26관절 × 30fps × 약 85초

    private final MotionRecordingRepository recordingRepository;
    private final ObjectMapper objectMapper;

    @Transactional(transactionManager = MotionDbConfig.TX)
    public MotionRecordingResponse save(Long playerId, MotionRecordingRequest req, String source) {
        int joints = req.jointCount();
        if (joints < 1 || joints > MAX_JOINTS) {
            throw ApiException.badRequest("jointCount 는 1~" + MAX_JOINTS + " 사이여야 합니다.");
        }
        List<Float> frames = req.frames();
        if (frames.isEmpty() || frames.size() > MAX_VALUES) {
            throw ApiException.badRequest("frames 는 1~" + MAX_VALUES + "개 숫자여야 합니다.");
        }
        int perFrame = joints * 3;
        if (frames.size() % perFrame != 0) {
            throw ApiException.badRequest("frames 길이가 jointCount × 3 의 배수가 아닙니다. (현재 " + frames.size() + "개)");
        }
        for (Float v : frames) {
            if (v == null || v.isNaN() || v.isInfinite()) {
                throw ApiException.badRequest("frames 에 숫자가 아닌 값이 있습니다.");
            }
        }
        String hand = req.hand() == null ? "right" : req.hand().trim().toLowerCase(Locale.ROOT);
        if (!HANDS.contains(hand)) {
            throw ApiException.badRequest("hand 는 left / right / both 중 하나여야 합니다.");
        }
        int fps = req.fps() == null ? 30 : Math.max(1, Math.min(req.fps(), 240));
        int frameCount = frames.size() / perFrame;

        MotionRecording m = new MotionRecording();
        m.setPlayerId(playerId);
        m.setLabel(req.label().trim().toUpperCase(Locale.ROOT));
        m.setHand(hand);
        m.setJointCount(joints);
        m.setFrameCount(frameCount);
        m.setFps(fps);
        m.setDurationMs(req.durationMs() != null ? Math.max(0, req.durationMs()) : frameCount * 1000 / fps);
        m.setFramesJson(writeJson(frames));
        m.setDevice(req.device());
        m.setMatchToken(req.matchToken());
        m.setSource(source);
        recordingRepository.save(m);
        return toResponse(m, false);
    }

    /** Python 학습용 내보내기 (afterId 이후, id 순) */
    @Transactional(transactionManager = MotionDbConfig.TX, readOnly = true)
    public List<MotionRecordingResponse> export(String label, long afterId, int limit) {
        int safe = Math.min(Math.max(limit, 1), 200);
        List<MotionRecording> list = label == null || label.isBlank()
                ? recordingRepository.findByIdGreaterThanOrderByIdAsc(afterId, PageRequest.of(0, safe))
                : recordingRepository.findByLabelAndIdGreaterThanOrderByIdAsc(label.trim().toUpperCase(Locale.ROOT), afterId, PageRequest.of(0, safe));
        return list.stream().map(m -> toResponse(m, true)).toList();
    }

    /** 관리자 목록 (좌표 제외) */
    @Transactional(transactionManager = MotionDbConfig.TX, readOnly = true)
    public List<MotionRecordingResponse> recent(int limit) {
        return recordingRepository.findAllByOrderByIdDesc(PageRequest.of(0, Math.min(Math.max(limit, 1), 200)))
                .stream().map(m -> toResponse(m, false)).toList();
    }

    private MotionRecordingResponse toResponse(MotionRecording m, boolean withFrames) {
        return new MotionRecordingResponse(m.getId(), m.getPlayerId(), m.getLabel(), m.getHand(), m.getFrameCount(),
                m.getJointCount(), m.getFps(), m.getDurationMs(), m.getDevice(), m.getSource(), m.getCreatedAt(),
                withFrames ? readJson(m.getFramesJson()) : null);
    }

    private String writeJson(List<Float> values) {
        try {
            return objectMapper.writeValueAsString(values);
        } catch (JsonProcessingException ex) {
            throw ApiException.badRequest("frames 를 저장할 수 없습니다.");
        }
    }

    private List<Float> readJson(String json) {
        try {
            return objectMapper.readValue(json, new TypeReference<List<Float>>() {
            });
        } catch (JsonProcessingException ex) {
            return List.of();
        }
    }
}
