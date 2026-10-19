package com.wow.dto.deck;

import jakarta.validation.constraints.NotNull;

import java.util.List;

/** 마법서 저장: 스킬 코드 8개 (예: ["S03","S02","S07","S08","S11","S13","H01","H02"]) */
public record DeckRequest(@NotNull(message = "덱 정보가 없습니다.") List<String> skills) {
}
