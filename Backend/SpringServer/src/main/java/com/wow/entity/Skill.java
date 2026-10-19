package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;

/**
 * 스킬 19종 (일반 S01~S13, 고위력 H01~H06). DataInitializer 가 시작할 때 채우고 갱신한다.
 * 밸런스 수치를 DB 에서 바꾸면 웹 UI / Unity 가 /api/skills 로 받아 쓸 수 있다.
 */
@Entity
@Table(name = "skills")
@Getter
@Setter
@NoArgsConstructor
public class Skill {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, unique = true, length = 10)
    private String code;

    @Column(nullable = false, length = 50)
    private String name;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 10)
    private SkillTier tier;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private SkillKind kind;

    @Column(nullable = false, length = 300)
    private String effect;

    @Column(nullable = false, length = 10)
    private String color;

    @Column(name = "cooldown_seconds", nullable = false)
    private int cooldownSeconds;

    /** 게임당 사용 가능 횟수 (0 = 제한 없음) */
    @Column(name = "uses_per_game", nullable = false)
    private int usesPerGame;

    /** 플레이어 적중 시 기본 피해량 [TBD — 밸런스 확정 전 임시값] */
    @Column(name = "base_damage", nullable = false)
    private int baseDamage;

    @Column(name = "sort_order", nullable = false)
    private int sortOrder;
}
