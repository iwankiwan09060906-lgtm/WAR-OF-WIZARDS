package com.wow.dto.deck;

import java.util.List;

/** complete: 8장이 모두 채워져 대전에 들어갈 수 있는 덱인지 */
public record DeckResponse(Long playerId, List<String> skills, boolean complete) {
}
