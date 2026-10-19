package com.wow.dto.server;

import java.util.List;

/** 전용 서버가 경기 시작 시 불러가는 플레이어 정보 (덱 포함) */
public record ServerPlayerInfo(Long playerId, String nickname, int rating, int level, boolean bot, List<String> deck) {
}
