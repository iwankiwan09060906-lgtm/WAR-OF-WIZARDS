package com.wow.controller;

import com.wow.dto.deck.DeckRequest;
import com.wow.dto.deck.DeckResponse;
import com.wow.security.UserPrincipal;
import com.wow.service.DeckService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.*;

/** 마법서 (캔버스 9) */
@RestController
@RequestMapping("/api/decks")
@RequiredArgsConstructor
public class DeckController {

    private final DeckService deckService;

    @GetMapping("/me")
    public ResponseEntity<DeckResponse> getMyDeck(@AuthenticationPrincipal UserPrincipal me) {
        return ResponseEntity.ok(deckService.getDeck(me.playerId()));
    }

    @PutMapping("/me")
    public ResponseEntity<DeckResponse> saveMyDeck(
            @AuthenticationPrincipal UserPrincipal me,
            @Valid @RequestBody DeckRequest request
    ) {
        return ResponseEntity.ok(deckService.saveDeck(me.playerId(), request.skills()));
    }
}
