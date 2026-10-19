package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

/**
 * 한 판의 경기. 참가자별 기록은 MatchPlayer.
 * match_token 은 경기마다 유일 — Photon 룸/서버가 같은 결과를 두 번 보내도 한 번만 기록된다.
 */
@Entity
@Table(name = "matches")
@Getter
@Setter
@NoArgsConstructor
public class Match {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "match_token", nullable = false, unique = true, length = 64)
    private String matchToken;

    @Enumerated(EnumType.STRING)
    @Column(name = "game_mode", nullable = false, length = 20)
    private MatchMode mode;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private MatchSource source;

    @Column(name = "room_code", length = 20)
    private String roomCode;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "winner_id")
    private Player winner;

    @Column(name = "started_at")
    private Instant startedAt;

    @Column(name = "ended_at", nullable = false)
    private Instant endedAt;

    @Column(name = "duration_sec", nullable = false)
    private int durationSec;

    @OneToMany(mappedBy = "match", cascade = CascadeType.ALL, orphanRemoval = true)
    private List<MatchPlayer> players = new ArrayList<>();

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;

    public Match(String matchToken, MatchMode mode, MatchSource source, String roomCode,
                 Instant startedAt, Instant endedAt, int durationSec) {
        this.matchToken = matchToken;
        this.mode = mode;
        this.source = source;
        this.roomCode = roomCode;
        this.startedAt = startedAt;
        this.endedAt = endedAt;
        this.durationSec = durationSec;
    }

    public void addPlayer(MatchPlayer matchPlayer) {
        matchPlayer.setMatch(this);
        players.add(matchPlayer);
    }
}
