package com.wow.controller;

import com.wow.dto.skill.SkillResponse;
import com.wow.service.SkillService;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

/** 스킬 19종. 예전 경로 /api/spells 도 같은 데이터를 돌려준다. */
@RestController
@RequestMapping({"/api/skills", "/api/spells"})
@RequiredArgsConstructor
public class SkillController {

    private final SkillService skillService;

    @GetMapping
    public ResponseEntity<List<SkillResponse>> getAll() {
        return ResponseEntity.ok(skillService.getAll());
    }

    @GetMapping("/{code}")
    public ResponseEntity<SkillResponse> get(@PathVariable String code) {
        return ResponseEntity.ok(skillService.get(code));
    }
}
