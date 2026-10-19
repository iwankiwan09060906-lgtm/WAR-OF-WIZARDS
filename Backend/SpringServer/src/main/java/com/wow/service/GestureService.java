package com.wow.service;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.wow.common.ApiException;
import com.wow.config.MotionDbConfig;
import com.wow.config.WowProperties;
import com.wow.dto.gesture.GestureLabelStat;
import com.wow.dto.gesture.GestureSampleRequest;
import com.wow.dto.gesture.GestureSampleResponse;
import com.wow.dto.gesture.ModelInfoResponse;
import com.wow.entity.Skill;
import com.wow.motion.entity.GestureSample;
import com.wow.motion.entity.GestureModel;
import com.wow.motion.repository.GestureModelRepository;
import com.wow.motion.repository.GestureSampleRepository;
import com.wow.repository.SkillRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.data.domain.PageRequest;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.multipart.MultipartFile;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardCopyOption;
import java.security.DigestInputStream;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.stream.Collectors;

/**
 * 룬 제스처 AI 파이프라인 (Unity → Spring → Python → Spring → Unity)
 * 1) Unity 가 스킬별 룬 궤적을 업로드 (POST /api/gestures/samples)
 * 2) Python 이 서버 키로 내려받아 학습 (GET /api/server/gestures/samples)
 * 3) Python 이 ONNX 모델을 업로드 (POST /api/server/gestures/model)
 * 4) Unity 가 최신 모델을 내려받아 Sentis 로 추론 (GET /api/gestures/model)
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class GestureService {

    public static final int MODEL_POINTS = 64;
    private static final int MIN_POINTS = 4;
    private static final int MAX_POINTS = 1024;
    private static final long MAX_MODEL_BYTES = 50L * 1024 * 1024;

    private final GestureSampleRepository sampleRepository;
    private final SkillRepository skillRepository;
    private final GestureModelRepository modelRepository;
    private final ObjectMapper objectMapper;
    private final WowProperties props;

    // ── 샘플 수집 ────────────────────────────────────────────────────────

    @Transactional(transactionManager = MotionDbConfig.TX)
    public GestureSampleResponse addSample(Long playerId, GestureSampleRequest request, String source) {
        String label = request.label().trim().toUpperCase(Locale.ROOT);
        if (skillRepository.findByCode(label).isEmpty()) {
            throw ApiException.badRequest("label 은 스킬 코드(S01~S13, H01~H06)여야 합니다: " + request.label());
        }
        List<Float> points = request.points();
        if (points.size() % 3 != 0) {
            throw ApiException.badRequest("points 는 x,y,z 3개씩이어야 합니다. (현재 " + points.size() + "개)");
        }
        int count = points.size() / 3;
        if (count < MIN_POINTS || count > MAX_POINTS) {
            throw ApiException.badRequest("점 개수는 " + MIN_POINTS + "~" + MAX_POINTS + "개여야 합니다. (현재 " + count + "개)");
        }
        for (Float value : points) {
            if (value == null || value.isNaN() || value.isInfinite()) {
                throw ApiException.badRequest("points 에 숫자가 아닌 값이 있습니다.");
            }
        }

        GestureSample sample = new GestureSample();
        sample.setPlayerId(playerId);
        sample.setLabel(label);
        sample.setPointCount(count);
        sample.setPointsJson(writeJson(points));
        sample.setDurationMs(request.durationMs() == null ? 0 : Math.max(0, Math.min(request.durationMs(), 60_000)));
        sample.setHand(request.hand() == null || request.hand().isBlank() ? null : truncate(request.hand().trim().toLowerCase(Locale.ROOT), 10));
        sample.setSource(truncate(source, 20));
        sampleRepository.save(sample);
        return toResponse(sample, playerId, false);
    }

    /** 19개 스킬 전부에 대해 샘플 수 (0 포함). classIndex = 모델 출력 순서 */
    @Transactional(transactionManager = MotionDbConfig.TX, readOnly = true)
    public List<GestureLabelStat> stats() {
        Map<String, Long> counts = sampleRepository.countByLabel().stream()
                .collect(Collectors.toMap(row -> (String) row[0], row -> ((Number) row[1]).longValue()));
        List<Skill> skills = skillRepository.findAllByOrderBySortOrderAsc();
        List<GestureLabelStat> result = new ArrayList<>();
        for (int i = 0; i < skills.size(); i++) {
            Skill s = skills.get(i);
            result.add(new GestureLabelStat(i, s.getCode(), s.getName(), counts.getOrDefault(s.getCode(), 0L)));
        }
        return result;
    }

    /** Python 학습용 내보내기 — afterId 이후 샘플을 id 순서로 (페이지 단위로 계속 받아가면 된다) */
    @Transactional(transactionManager = MotionDbConfig.TX, readOnly = true)
    public List<GestureSampleResponse> export(String label, long afterId, int limit) {
        int safeLimit = Math.min(Math.max(limit, 1), 1000);
        List<GestureSample> samples = label == null || label.isBlank()
                ? sampleRepository.findByIdGreaterThanOrderByIdAsc(afterId, PageRequest.of(0, safeLimit))
                : sampleRepository.findByLabelAndIdGreaterThanOrderByIdAsc(label.trim().toUpperCase(Locale.ROOT), afterId, PageRequest.of(0, safeLimit));
        return samples.stream()
                .map(s -> toResponse(s, s.getPlayerId(), true))
                .toList();
    }

    // ── 모델 배포 ────────────────────────────────────────────────────────

    public List<String> classLabels() {
        return skillRepository.findAllByOrderBySortOrderAsc().stream().map(Skill::getCode).toList();
    }

    public ModelInfoResponse modelInfo() {
        Path model = modelPath();
        Path weights = weightsPath();
        List<Integer> shape = List.of(1, MODEL_POINTS, 3);
        try {
            boolean modelExists = Files.isRegularFile(model);
            boolean weightsExist = Files.isRegularFile(weights);
            return new ModelInfoResponse(
                    modelExists,
                    model.getFileName().toString(),
                    modelExists ? Files.size(model) : 0,
                    modelExists ? sha256(model) : null,
                    modelExists ? Files.getLastModifiedTime(model).toInstant() : null,
                    "/api/gestures/model",
                    weightsExist,
                    weightsExist ? sha256(weights) : null,
                    "/api/gestures/model/weights",
                    shape,
                    classLabels());
        } catch (IOException ex) {
            throw new ApiException(org.springframework.http.HttpStatus.INTERNAL_SERVER_ERROR, "모델 파일을 읽을 수 없습니다.");
        }
    }

    public Path modelFileOrThrow() {
        return existingOrThrow(modelPath());
    }

    public Path weightsFileOrThrow() {
        return existingOrThrow(weightsPath());
    }

    /** .onnx → ONNX 모델, .json → 가중치(train.py export_weights_json) */
    public ModelInfoResponse uploadModel(MultipartFile file) {
        if (file == null || file.isEmpty()) {
            throw ApiException.badRequest("모델 파일(file)이 비어 있습니다.");
        }
        String name = file.getOriginalFilename() == null ? "" : file.getOriginalFilename().toLowerCase(Locale.ROOT);
        Path target;
        if (name.endsWith(".onnx")) {
            target = modelPath();
        } else if (name.endsWith(".json")) {
            target = weightsPath();
            validateWeightsJson(file);
        } else {
            throw ApiException.badRequest(".onnx 또는 가중치 .json 파일만 올릴 수 있습니다.");
        }
        if (file.getSize() > MAX_MODEL_BYTES) {
            throw ApiException.badRequest("모델 파일은 50MB 이하여야 합니다.");
        }
        Path temp = null;
        try {
            Files.createDirectories(target.getParent());
            temp = Files.createTempFile(target.getParent(), "upload-", ".tmp");
            try (InputStream in = file.getInputStream()) {
                Files.copy(in, temp, StandardCopyOption.REPLACE_EXISTING);
            }
            Files.move(temp, target, StandardCopyOption.REPLACE_EXISTING);
        } catch (IOException ex) {
            deleteQuietly(temp);
            log.error("모델 저장 실패: {}", target, ex);
            throw new ApiException(org.springframework.http.HttpStatus.INTERNAL_SERVER_ERROR, "모델 파일을 저장하지 못했습니다.");
        }
        log.info("제스처 모델 업로드: {} ({} bytes)", target, file.getSize());
        recordModelUpload(target, file.getSize());
        return modelInfo();
    }

    /** 모델 업로드 이력을 모션 DB(gesture_models)에 남긴다 — 관리자 페이지에서 조회 */
    private void recordModelUpload(Path target, long size) {
        try {
            GestureModel m = new GestureModel();
            m.setFileName(target.getFileName().toString());
            m.setKind(target.getFileName().toString().endsWith(".onnx") ? "onnx" : "weights");
            m.setSizeBytes(size);
            m.setSha256(sha256(target));
            m.setSampleCount(sampleRepository.count());
            modelRepository.save(m);
        } catch (Exception ex) {
            log.warn("모델 업로드 이력 저장 실패 (모델 파일은 정상 저장됨): {}", ex.getMessage());
        }
    }

    private void validateWeightsJson(MultipartFile file) {
        try (InputStream in = file.getInputStream()) {
            Map<?, ?> json = objectMapper.readValue(in, Map.class);
            if (!(json.get("classes") instanceof List<?>) || !(json.get("weightIh") instanceof List<?>)) {
                throw ApiException.badRequest("가중치 JSON 형식이 아닙니다. (classes, weightIh 필요)");
            }
        } catch (IOException ex) {
            throw ApiException.badRequest("가중치 JSON 을 읽을 수 없습니다.");
        }
    }

    private Path existingOrThrow(Path path) {
        if (!Files.isRegularFile(path)) {
            throw ApiException.notFound("아직 업로드된 제스처 모델이 없습니다.");
        }
        return path;
    }

    private Path modelPath() {
        return Paths.get(props.ai().modelPath()).toAbsolutePath().normalize();
    }

    /** 가중치 JSON 은 ONNX 와 같은 폴더의 gesture_model_weights.json */
    private Path weightsPath() {
        Path model = modelPath();
        String base = model.getFileName().toString().replaceFirst("\\.onnx$", "");
        return model.resolveSibling(base + "_weights.json");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private GestureSampleResponse toResponse(GestureSample s, Long playerId, boolean withPoints) {
        return new GestureSampleResponse(s.getId(), s.getLabel(), s.getPointCount(), s.getDurationMs(), s.getHand(),
                playerId, s.getCreatedAt(), withPoints ? readJson(s.getPointsJson()) : null);
    }

    private String writeJson(List<Float> points) {
        try {
            return objectMapper.writeValueAsString(points);
        } catch (JsonProcessingException ex) {
            throw ApiException.badRequest("points 를 저장할 수 없습니다.");
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

    private static String sha256(Path path) throws IOException {
        try (InputStream in = new DigestInputStream(Files.newInputStream(path), MessageDigest.getInstance("SHA-256"))) {
            DigestInputStream digestStream = (DigestInputStream) in;
            byte[] buffer = new byte[8192];
            while (digestStream.read(buffer) != -1) {
                // 끝까지 읽으며 해시 계산
            }
            return HexFormat.of().formatHex(digestStream.getMessageDigest().digest());
        } catch (NoSuchAlgorithmException ex) {
            throw new IllegalStateException(ex);
        }
    }

    private static void deleteQuietly(Path path) {
        if (path == null) {
            return;
        }
        try {
            Files.deleteIfExists(path);
        } catch (IOException ignored) {
            // 임시 파일 정리 실패는 무시
        }
    }

    private static String truncate(String value, int max) {
        return value.length() > max ? value.substring(0, max) : value;
    }
}
