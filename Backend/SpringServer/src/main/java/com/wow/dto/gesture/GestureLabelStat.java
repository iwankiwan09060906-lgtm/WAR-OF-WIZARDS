package com.wow.dto.gesture;

/** 스킬(클래스)별 수집된 샘플 수 */
public record GestureLabelStat(int classIndex, String label, String name, long count) {
}
