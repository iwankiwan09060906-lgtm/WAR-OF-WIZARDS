package com.wow.dto.live;

import com.wow.dto.friend.FriendResponse;
import com.wow.dto.player.ProfileResponse;
import com.wow.dto.skill.SkillResponse;

import java.util.List;

/**
 * DB → Unity / 웹 실시간 데이터 형식.
 * Unity JsonUtility 로 바로 읽을 수 있도록 Map 없이 배열·기본 타입만 쓴다.
 * (Unity 쪽 같은 모양: UnityXR/Assets/_Project/API/LiveApi.cs)
 */
public final class LiveDtos {

    private LiveDtos() {
    }

    /** 게임 설정 1개. value 는 항상 문자열 (type 에 따라 숫자/불리언으로 변환해서 쓴다) */
    public record ConfigEntry(String key, String value, String type, String category, String description, long version) {
    }

    /**
     * 실시간 이벤트 1개 (SSE 의 data).
     * type: hello / config / mail / invite / notice / friend / broadcast
     * payload: 추가 데이터(JSON 문자열). Unity 에서는 JsonUtility.FromJson 으로 한 번 더 읽는다.
     */
    public record LiveEvent(String type, String title, String message, String payload, long serverTime) {
    }

    /** 게임 시작 시 한 번에 받는 데이터 (GET /api/live/bootstrap) */
    public record Bootstrap(
            long serverTime,
            ProfileResponse profile,
            List<String> deck,
            List<SkillResponse> skills,
            List<ConfigEntry> configs,
            long unreadMail,
            List<FriendResponse> friends,
            List<Long> onlineFriendIds,
            String eventsUrl
    ) {
    }

    // ── 관리자 요청 ──
    public record ConfigUpdateRequest(String value) {
    }

    /** toPlayerId 가 null 이면 접속 중인 모든 클라이언트 */
    public record BroadcastRequest(Long toPlayerId, String type, String title, String message, String payload) {
    }

    public record LiveStatus(int connectedPlayers, int connections, List<Long> playerIds) {
    }
}
