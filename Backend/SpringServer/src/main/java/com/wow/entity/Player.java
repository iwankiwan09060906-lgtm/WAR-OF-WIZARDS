package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;
import org.hibernate.annotations.UpdateTimestamp;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

/**
 * 게임 안의 마법사 (닉네임, 레이팅, 전적, 마법서 덱).
 * id 가 웹 UI / Unity / Photon(UserId) 에서 공통으로 쓰는 플레이어 ID 이다.
 * 봇(랭킹·친구 데모용)은 로그인 계정(user)이 없다.
 */
@Entity
@Table(name = "players")
@Getter
@Setter
@NoArgsConstructor
public class Player {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @OneToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "user_id", unique = true)
    private User user;

    @Column(nullable = false, unique = true, length = 20)
    private String nickname;

    @Column(nullable = false)
    private int rating = 1000;

    @Column(nullable = false)
    private int wins;

    @Column(nullable = false)
    private int losses;

    @Column(nullable = false)
    private int draws;

    @Column(name = "player_level", nullable = false)
    private int level = 1;

    @Column(nullable = false)
    private int experience;

    @Column(name = "is_bot", nullable = false)
    private boolean bot;

    /** 마법서 덱: 스킬 코드 8개 (슬롯 순서 = 전투 HUD 단축키 1~8) */
    @ElementCollection(fetch = FetchType.LAZY)
    @CollectionTable(name = "player_decks", joinColumns = @JoinColumn(name = "player_id"))
    @OrderColumn(name = "slot_index")
    @Column(name = "skill_code", nullable = false, length = 10)
    private List<String> deck = new ArrayList<>();

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;

    @UpdateTimestamp
    @Column(name = "updated_at", nullable = false)
    private Instant updatedAt;

    public Player(User user, String nickname) {
        this.user = user;
        this.nickname = nickname;
    }

    public static Player bot(String nickname, int rating, int wins, int losses) {
        Player p = new Player(null, nickname);
        p.bot = true;
        p.rating = rating;
        p.wins = wins;
        p.losses = losses;
        return p;
    }

    public int getTotalGames() {
        return wins + losses + draws;
    }
}
