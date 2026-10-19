package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;

import java.time.Instant;

/**
 * 친선전 룸. room_code(6자리) 를 그대로 Photon 룸 이름(세션명)으로 사용한다.
 * Photon Webhook(GameCreate/GameClose)이 오면 상태가 갱신된다.
 */
@Entity
@Table(name = "game_rooms")
@Getter
@Setter
@NoArgsConstructor
public class GameRoom {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "room_code", nullable = false, unique = true, length = 6)
    private String roomCode;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "host_id")
    private Player host;

    @Enumerated(EnumType.STRING)
    @Column(name = "game_mode", nullable = false, length = 20)
    private MatchMode mode = MatchMode.FRIENDLY;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 10)
    private RoomStatus status = RoomStatus.OPEN;

    @Column(name = "max_players", nullable = false)
    private int maxPlayers = 2;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;

    @Column(name = "closed_at")
    private Instant closedAt;

    /** 웹 친선전 룸에 입장한 게스트 (Photon 없이 웹에서 대기실을 쓸 때) */
    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "guest_id")
    private Player guest;

    @Column(name = "guest_ready")
    private Boolean guestReady = false;

    public boolean isGuestReady() {
        return Boolean.TRUE.equals(guestReady);
    }

    public GameRoom(String roomCode, Player host, MatchMode mode) {
        this.roomCode = roomCode;
        this.host = host;
        this.mode = mode;
    }
}
