package com.wow.service;

import com.wow.common.ApiException;
import com.wow.dto.room.RoomResponse;
import com.wow.entity.GameRoom;
import com.wow.entity.MatchMode;
import com.wow.entity.Player;
import com.wow.entity.RoomStatus;
import com.wow.repository.GameRoomRepository;
import com.wow.repository.PlayerRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.security.SecureRandom;
import java.time.Instant;
import java.util.Locale;
import java.util.regex.Pattern;

/**
 * 친선전 룸 코드 발급/상태 관리. 코드 = Photon 룸 이름.
 */
@Service
@RequiredArgsConstructor
public class RoomService {

    /** 헷갈리는 글자(O/0, I/1) 제외 */
    private static final String ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static final Pattern CODE = Pattern.compile("^[A-Z0-9]{6}$");

    private final SecureRandom random = new SecureRandom();
    private final GameRoomRepository roomRepository;
    private final PlayerRepository playerRepository;

    @Transactional
    public RoomResponse create(Long hostPlayerId, String mode) {
        Player host = playerRepository.findById(hostPlayerId)
                .orElseThrow(() -> ApiException.notFound("플레이어를 찾을 수 없습니다."));
        GameRoom room = roomRepository.save(new GameRoom(generateUniqueCode(), host, MatchMode.from(mode, MatchMode.FRIENDLY)));
        return toResponse(room);
    }

    @Transactional(readOnly = true)
    public RoomResponse get(String code) {
        return toResponse(find(code));
    }

    @Transactional
    public RoomResponse close(Long requesterId, String code) {
        GameRoom room = find(code);
        requireHost(room, requesterId, "방장만 룸을 닫을 수 있습니다.");
        closeRoom(room);
        return toResponse(room);
    }

    // ── 웹 대기실: 입장 → 준비 → 방장이 시작 (클라이언트는 2초마다 GET /api/rooms/{code} 로 상태를 확인) ──

    @Transactional
    public RoomResponse join(Long playerId, String code) {
        GameRoom room = find(code);
        if (room.getStatus() != RoomStatus.OPEN) {
            throw ApiException.conflict("이미 시작했거나 닫힌 룸입니다.");
        }
        if (isHost(room, playerId)) {
            return toResponse(room);
        }
        if (room.getGuest() != null && !room.getGuest().getId().equals(playerId)) {
            throw ApiException.conflict("룸이 가득 찼습니다.");
        }
        Player me = playerRepository.findById(playerId)
                .orElseThrow(() -> ApiException.notFound("플레이어를 찾을 수 없습니다."));
        if (room.getGuest() == null) {
            room.setGuest(me);
            room.setGuestReady(false);
        }
        return toResponse(room);
    }

    @Transactional
    public RoomResponse leave(Long playerId, String code) {
        GameRoom room = find(code);
        if (isHost(room, playerId)) {
            closeRoom(room);
        } else if (isGuest(room, playerId)) {
            room.setGuest(null);
            room.setGuestReady(false);
        }
        return toResponse(room);
    }

    @Transactional
    public RoomResponse ready(Long playerId, String code, boolean ready) {
        GameRoom room = find(code);
        if (!isGuest(room, playerId)) {
            throw ApiException.forbidden("이 룸의 게스트가 아닙니다.");
        }
        if (room.getStatus() != RoomStatus.OPEN) {
            throw ApiException.conflict("이미 시작했거나 닫힌 룸입니다.");
        }
        room.setGuestReady(ready);
        return toResponse(room);
    }

    @Transactional
    public RoomResponse kick(Long playerId, String code) {
        GameRoom room = find(code);
        requireHost(room, playerId, "방장만 게스트를 내보낼 수 있습니다.");
        room.setGuest(null);
        room.setGuestReady(false);
        return toResponse(room);
    }

    @Transactional
    public RoomResponse start(Long playerId, String code) {
        GameRoom room = find(code);
        requireHost(room, playerId, "방장만 대전을 시작할 수 있습니다.");
        if (room.getStatus() != RoomStatus.OPEN) {
            throw ApiException.conflict("이미 시작했거나 닫힌 룸입니다.");
        }
        if (room.getGuest() == null) {
            throw ApiException.badRequest("게스트가 입장해야 시작할 수 있습니다.");
        }
        if (!room.isGuestReady()) {
            throw ApiException.badRequest("게스트가 아직 준비하지 않았습니다.");
        }
        room.setStatus(RoomStatus.IN_GAME);
        return toResponse(room);
    }

    private static boolean isHost(GameRoom room, Long playerId) {
        return room.getHost() != null && room.getHost().getId().equals(playerId);
    }

    private static boolean isGuest(GameRoom room, Long playerId) {
        return room.getGuest() != null && room.getGuest().getId().equals(playerId);
    }

    private static void requireHost(GameRoom room, Long playerId, String message) {
        if (!isHost(room, playerId)) {
            throw ApiException.forbidden(message);
        }
    }

    /** Photon GameCreate/GameJoin Webhook: 우리 서버가 발급하지 않은 코드여도 형식이 맞으면 기록 */
    @Transactional
    public void markInGame(String code, Long hostPlayerId) {
        String normalized = normalize(code);
        if (normalized == null || !CODE.matcher(normalized).matches()) {
            return;
        }
        GameRoom room = roomRepository.findByRoomCode(normalized).orElseGet(() -> {
            Player host = hostPlayerId == null ? null : playerRepository.findById(hostPlayerId).orElse(null);
            return roomRepository.save(new GameRoom(normalized, host, MatchMode.FRIENDLY));
        });
        if (room.getStatus() != RoomStatus.CLOSED) {
            room.setStatus(RoomStatus.IN_GAME);
        }
    }

    @Transactional
    public void markClosed(String code) {
        String normalized = normalize(code);
        if (normalized == null) {
            return;
        }
        roomRepository.findByRoomCode(normalized).ifPresent(this::closeRoom);
    }

    public static String normalize(String code) {
        if (code == null || code.isBlank()) {
            return null;
        }
        return code.trim().toUpperCase(Locale.ROOT);
    }

    public static boolean isValidCode(String code) {
        return code != null && CODE.matcher(code).matches();
    }

    private GameRoom find(String code) {
        String normalized = normalize(code);
        return roomRepository.findByRoomCode(normalized == null ? "" : normalized)
                .orElseThrow(() -> ApiException.notFound("룸을 찾을 수 없습니다."));
    }

    private void closeRoom(GameRoom room) {
        if (room.getStatus() != RoomStatus.CLOSED) {
            room.setStatus(RoomStatus.CLOSED);
            room.setClosedAt(Instant.now());
        }
    }

    private String generateUniqueCode() {
        for (int attempt = 0; attempt < 20; attempt++) {
            StringBuilder sb = new StringBuilder(6);
            for (int i = 0; i < 6; i++) {
                sb.append(ALPHABET.charAt(random.nextInt(ALPHABET.length())));
            }
            String code = sb.toString();
            if (!roomRepository.existsByRoomCode(code)) {
                return code;
            }
        }
        throw new ApiException(org.springframework.http.HttpStatus.SERVICE_UNAVAILABLE, "룸 코드를 만들지 못했습니다. 다시 시도해주세요.");
    }

    private RoomResponse toResponse(GameRoom room) {
        Player host = room.getHost();
        Player guest = room.getGuest();
        return new RoomResponse(room.getRoomCode(), room.getRoomCode(), room.getMode().lower(),
                room.getStatus().name().toLowerCase(Locale.ROOT),
                host != null ? host.getId() : null, host != null ? host.getNickname() : null,
                room.getMaxPlayers(), room.getCreatedAt(),
                guest != null ? guest.getId() : null, guest != null ? guest.getNickname() : null,
                guest != null ? guest.getRating() : null, room.isGuestReady());
    }
}
