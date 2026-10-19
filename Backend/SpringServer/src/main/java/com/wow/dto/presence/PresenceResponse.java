package com.wow.dto.presence;

import java.util.List;

/** status: 내 상태 (online / in_game), onlineFriendIds: 접속 중인 친구 playerId 목록 */
public record PresenceResponse(String status, List<Long> onlineFriendIds) {
}
