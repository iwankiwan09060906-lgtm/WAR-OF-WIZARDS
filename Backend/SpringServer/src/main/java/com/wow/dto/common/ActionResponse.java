package com.wow.dto.common;

/**
 * 결과 데이터가 없는 요청(수락, 삭제, 읽음 처리 등)의 공통 응답.
 * Unity JsonUtility 는 빈 본문을 파싱하지 못하므로 항상 JSON 을 돌려준다.
 */
public record ActionResponse(boolean ok, String message) {

    public static ActionResponse ok(String message) {
        return new ActionResponse(true, message);
    }
}
