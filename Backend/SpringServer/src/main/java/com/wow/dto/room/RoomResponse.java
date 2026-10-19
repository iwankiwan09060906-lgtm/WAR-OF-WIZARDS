package com.wow.dto.room;

import java.time.Instant;

/**
 * guest* 는 웹 대기실(입장/준비/시작)용. 게스트가 없으면 null / false.
 * photonRoomName 으로 PhotonNetwork.JoinOrCreateRoom / Fusion SessionName 을 호출하면 된다.
 */
public record RoomResponse(
        String roomCode,
        String photonRoomName,
        String mode,
        String status,
        Long hostPlayerId,
        String hostNickname,
        int maxPlayers,
        Instant createdAt,
        Long guestPlayerId,
        String guestNickname,
        Integer guestRating,
        boolean guestReady
) {
}
