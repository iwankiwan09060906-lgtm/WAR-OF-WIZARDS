package com.wow.service;

import com.wow.common.ApiException;
import com.wow.dto.deck.DeckResponse;
import com.wow.entity.Player;
import com.wow.entity.Skill;
import com.wow.entity.SkillTier;
import com.wow.repository.SkillRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;

/**
 * 마법서(덱). 규칙은 웹 UI src/data/skills.ts validateDeck 과 동일:
 * 정확히 8장, 중복 불가, 존재하는 스킬만, 고위력 최대 2장.
 */
@Service
@RequiredArgsConstructor
public class DeckService {

    public static final int DECK_SIZE = 8;
    public static final int MAX_HIGH_IN_DECK = 2;

    private final PlayerService playerService;
    private final SkillRepository skillRepository;

    @Transactional(readOnly = true)
    public DeckResponse getDeck(Long playerId) {
        Player player = playerService.getPlayer(playerId);
        List<String> skills = List.copyOf(player.getDeck());
        return new DeckResponse(playerId, skills, skills.size() == DECK_SIZE);
    }

    @Transactional
    public DeckResponse saveDeck(Long playerId, List<String> skills) {
        List<String> codes = skills.stream()
                .map(code -> code == null ? "" : code.trim().toUpperCase(Locale.ROOT))
                .toList();
        validate(codes);

        Player player = playerService.getPlayer(playerId);
        player.getDeck().clear();
        player.getDeck().addAll(codes);
        return new DeckResponse(playerId, List.copyOf(codes), true);
    }

    private void validate(List<String> codes) {
        if (codes.size() != DECK_SIZE) {
            throw ApiException.badRequest("덱은 정확히 " + DECK_SIZE + "장이어야 합니다. (현재 " + codes.size() + "장)");
        }
        if (new HashSet<>(codes).size() != codes.size()) {
            throw ApiException.badRequest("같은 스킬을 중복해서 넣을 수 없습니다.");
        }
        Map<String, Skill> skillMap = skillRepository.findAll().stream()
                .collect(Collectors.toMap(Skill::getCode, Function.identity()));
        if (!skillMap.keySet().containsAll(codes)) {
            throw ApiException.badRequest("존재하지 않는 스킬이 포함되어 있습니다.");
        }
        long high = codes.stream().filter(code -> skillMap.get(code).getTier() == SkillTier.HIGH).count();
        if (high > MAX_HIGH_IN_DECK) {
            throw ApiException.badRequest("고위력 스킬은 덱에 최대 " + MAX_HIGH_IN_DECK + "장까지 넣을 수 있습니다.");
        }
    }
}
