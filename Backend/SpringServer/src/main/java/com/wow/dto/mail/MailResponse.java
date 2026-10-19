package com.wow.dto.mail;

import java.time.Instant;

/**
 * 우편함 항목. id 는 "mail-12" / "notice-3" 형태 (우편과 공지를 한 목록에 섞어 보여주기 때문).
 * kind: mail / invite / notice
 */
public record MailResponse(
        String id,
        String kind,
        String title,
        String body,
        String from,
        String roomCode,
        String reward,
        boolean read,
        Instant createdAt
) {
}
