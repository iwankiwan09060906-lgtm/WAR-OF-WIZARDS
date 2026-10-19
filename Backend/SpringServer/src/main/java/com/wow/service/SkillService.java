package com.wow.service;

import com.wow.common.ApiException;
import com.wow.dto.skill.SkillResponse;
import com.wow.entity.Skill;
import com.wow.repository.SkillRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.Locale;

@Service
@RequiredArgsConstructor
@Transactional(readOnly = true)
public class SkillService {

    private final SkillRepository skillRepository;

    public List<SkillResponse> getAll() {
        return skillRepository.findAllByOrderBySortOrderAsc().stream().map(SkillService::toResponse).toList();
    }

    public SkillResponse get(String code) {
        return skillRepository.findByCode(code.trim().toUpperCase(Locale.ROOT))
                .map(SkillService::toResponse)
                .orElseThrow(() -> ApiException.notFound("존재하지 않는 스킬입니다: " + code));
    }

    public static SkillResponse toResponse(Skill s) {
        return new SkillResponse(s.getCode(), s.getName(), s.getTier().name().toLowerCase(Locale.ROOT),
                s.getKind().name().toLowerCase(Locale.ROOT), s.getEffect(), s.getColor(),
                s.getCooldownSeconds(), s.getUsesPerGame(), s.getBaseDamage());
    }
}
