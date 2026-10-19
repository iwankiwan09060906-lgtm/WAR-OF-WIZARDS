package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;

import java.time.Instant;

/**
 * 경기 참가자 1명의 기록 (마이페이지 전적 한 줄).
 * opponent_name 은 기록 시점의 상대 이름 — 봇전처럼 상대가 players 테이블에 없을 때도 표시하기 위해 저장.
 */
@Entity
@Table(name = "match_players", uniqueConstraints = {
        @UniqueConstraint(name = "uq_match_player", columnNames = {"match_id", "player_id"})
})
@Getter
@Setter
@NoArgsConstructor
public class MatchPlayer {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "match_id", nullable = false)
    private Match match;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "player_id", nullable = false)
    private Player player;

    @Column(name = "opponent_name", nullable = false, length = 30)
    private String opponentName;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 10)
    private MatchOutcome outcome;

    @Column(nullable = false)
    private int casts;

    @Column(name = "damage_dealt", nullable = false)
    private int damageDealt;

    @Column(name = "damage_taken", nullable = false)
    private int damageTaken;

    @Column(name = "rating_before", nullable = false)
    private int ratingBefore;

    @Column(name = "rating_after", nullable = false)
    private int ratingAfter;

    @Column(name = "rating_delta", nullable = false)
    private int ratingDelta;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;

    public MatchPlayer(Player player, String opponentName, MatchOutcome outcome,
                       int casts, int damageDealt, int damageTaken, int ratingBefore, int ratingAfter) {
        this.player = player;
        this.opponentName = opponentName;
        this.outcome = outcome;
        this.casts = casts;
        this.damageDealt = damageDealt;
        this.damageTaken = damageTaken;
        this.ratingBefore = ratingBefore;
        this.ratingAfter = ratingAfter;
        this.ratingDelta = ratingAfter - ratingBefore;
    }
}
