package com.wow.dto.friend;

/** relation: friend(친구) / incoming(받은 요청) / outgoing(보낸 요청), status: online / in_game / offline */
public record FriendResponse(
        Long id,
        String nickname,
        int rating,
        String relation,
        boolean online,
        String status
) {
}
