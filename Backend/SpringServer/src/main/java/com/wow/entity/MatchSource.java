package com.wow.entity;

/** 누가 결과를 기록했는가 — CLIENT(웹/Unity 자가 신고), SERVER(전용 서버), PHOTON(Webhook/WebRPC) */
public enum MatchSource {
    CLIENT,
    SERVER,
    PHOTON
}
