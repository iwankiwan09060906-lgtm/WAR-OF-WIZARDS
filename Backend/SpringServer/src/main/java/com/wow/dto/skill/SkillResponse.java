package com.wow.dto.skill;

public record SkillResponse(
        String code,
        String name,
        String tier,
        String kind,
        String effect,
        String color,
        int cooldownSeconds,
        int usesPerGame,
        int baseDamage
) {
}
