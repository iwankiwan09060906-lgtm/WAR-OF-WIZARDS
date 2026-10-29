# -*- coding: utf-8 -*-
"""
War of Wizards — 전장 지도 그림 (VLM 이 보는 그림)

상황(wow_ai.normalize_state 결과 + Unity 봇이 보낸 유닛 점)을 위에서 내려다본 그림 한 장으로 그린다.
게임 화면 사진이 없을 때(Unity 봇 · BotLab 봇끼리 대전) VLM 에 이 그림을 넣고, 미세조정 데이터셋도 같은 그림을 쓴다
(실제로 쓸 때와 배울 때 그림이 같아야 한다). 표준 라이브러리만 (zlib PNG).

    위 = 상대 진영(빨강) · 아래 = 내 진영(파랑) · 세로 세 줄 = 레인 (왼쪽 · 가운데 · 오른쪽, 내 시점)
    동그라미: 파랑 = 우리 유닛 · 빨강 = 적 유닛 (위치 = 레인 · 깊이)
    노란 마름모: 내 칸으로 날아오는 투사체 (아래로 갈수록 곧 닿는다)
    네모: 작은 것 = 타워 · 큰 것 = 넥서스 (무너지면 회색 + X) · 세모: 플레이어 (내 것은 아래, 상대는 위)
    맨 아래 8칸: 덱 슬롯 (초록 = 지금 쓸 수 있음 · 회색 = 쿨타임 · 금색 테두리 = 고위력) · 맨 위 · 맨 아래 막대: HP
"""

import base64
import struct
import zlib

W, H = 384, 512
LANE_W = W // 3
FIELD_TOP, FIELD_BOTTOM = 92, 400           # 깊이 1(상대 발판) ~ 0(내 발판)

BG = (18, 20, 28)
LANE_BG = ((28, 31, 42), (32, 35, 48))
LINE = (60, 64, 80)
ALLY = (70, 145, 255)
ENEMY = (240, 72, 60)
ALLY_DARK = (40, 80, 150)
ENEMY_DARK = (140, 45, 40)
DEAD = (90, 90, 96)
SHOT = (255, 214, 60)
READY = (70, 200, 110)
COOL = (70, 72, 82)
GOLD = (230, 180, 50)
WHITE = (235, 235, 240)
FOE_SHADES = ((240, 72, 60), (250, 140, 60), (200, 80, 200))


class Canvas:
    def __init__(self, w, h, color):
        self.w, self.h = w, h
        self.px = bytearray(bytes(color) * (w * h))

    def rect(self, x0, y0, x1, y1, color):
        x0, x1 = max(0, int(x0)), min(self.w, int(x1))
        if x1 <= x0:
            return
        row = bytes(color) * (x1 - x0)
        for y in range(max(0, int(y0)), min(self.h, int(y1))):
            i = (y * self.w + x0) * 3
            self.px[i:i + len(row)] = row

    def frame(self, x0, y0, x1, y1, color, t=2):
        self.rect(x0, y0, x1, y0 + t, color)
        self.rect(x0, y1 - t, x1, y1, color)
        self.rect(x0, y0, x0 + t, y1, color)
        self.rect(x1 - t, y0, x1, y1, color)

    def circle(self, cx, cy, r, color):
        for dy in range(-r, r + 1):
            half = int((r * r - dy * dy) ** 0.5)
            self.rect(cx - half, cy + dy, cx + half + 1, cy + dy + 1, color)

    def triangle(self, cx, y_tip, size, up, color):
        """밑변이 수평인 세모 (up = 꼭짓점이 위)"""
        for k in range(size):
            half = k * 0.6
            y = y_tip + k if up else y_tip - k
            self.rect(cx - half, y, cx + half + 1, y + 1, color)

    def diamond(self, cx, cy, r, color):
        for dy in range(-r, r + 1):
            half = r - abs(dy)
            self.rect(cx - half, cy + dy, cx + half + 1, cy + dy + 1, color)

    def cross(self, x0, y0, x1, y1, color, t=2):
        n = int(max(abs(x1 - x0), abs(y1 - y0)))
        for i in range(n + 1):
            f = i / max(1, n)
            for (ax, ay, bx, by) in ((x0, y0, x1, y1), (x0, y1, x1, y0)):
                x, y = ax + (bx - ax) * f, ay + (by - ay) * f
                self.rect(x - t / 2, y - t / 2, x + t / 2 + 1, y + t / 2 + 1, color)

    def png(self):
        raw = bytearray()
        stride = self.w * 3
        for y in range(self.h):
            raw.append(0)
            raw += self.px[y * stride:(y + 1) * stride]

        def chunk(tag, data):
            return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)
        return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b""))


def _lane_x(col):
    return LANE_W * col + LANE_W // 2


def _depth_y(depth):
    d = min(1.0, max(0.0, float(depth)))
    return int(FIELD_BOTTOM - d * (FIELD_BOTTOM - FIELD_TOP))


def _hp_bar(cv, y, frac, color, dark):
    cv.rect(8, y, W - 8, y + 6, dark)
    cv.rect(8, y, 8 + (W - 16) * min(1.0, max(0.0, frac)), y + 6, color)


def _base(cv, y_tower, y_nexus, tower_alive, nexus_alive, color):
    for (x0, y0, x1, y1, alive) in ((W / 2 - 34, y_tower, W / 2 + 34, y_tower + 18, tower_alive),
                                    (W / 2 - 60, y_nexus, W / 2 + 60, y_nexus + 20, nexus_alive)):
        cv.rect(x0, y0, x1, y1, color if alive else DEAD)
        if not alive:
            cv.cross(x0 + 4, y0 + 2, x1 - 4, y1 - 2, WHITE)


def _units_from_lanes(st):
    """유닛 점이 없을 때(웹 코치) 레인 요약으로 대신 그린다: 적은 몰린 깊이 둘레, 우리는 앞줄 둘레"""
    out = []
    for ln in st.get("lanes") or []:
        c = ln.get("column", 0)
        at = ln.get("clusterDepth")
        at = ln.get("enemyFront", 0.5) if at is None else at
        for i in range(min(12, int(ln.get("enemies") or 0))):
            out.append((c, min(1.0, at + 0.04 * (i // 3)), True))
        front = ln.get("allyFront") or 0.25
        for i in range(min(12, int(ln.get("allies") or 0))):
            out.append((c, max(0.0, front - 0.04 * (i // 3)), False))
    return out


def render(st):
    """normalize_state 결과 → PNG bytes"""
    cv = Canvas(W, H, BG)
    for c in range(3):
        cv.rect(LANE_W * c, FIELD_TOP, LANE_W * (c + 1), FIELD_BOTTOM, LANE_BG[c % 2])
    for c in (1, 2):
        cv.rect(LANE_W * c - 1, FIELD_TOP, LANE_W * c + 1, FIELD_BOTTOM, LINE)
    cv.rect(0, (FIELD_TOP + FIELD_BOTTOM) // 2, W, (FIELD_TOP + FIELD_BOTTOM) // 2 + 1, LINE)

    # 진영 · 구조물
    opps = st.get("opponents") or []
    _base(cv, 64, 38, st.get("opponentTowerAlive", True), st.get("opponentNexusAlive", True), ENEMY_DARK)
    _base(cv, 410, 432, st.get("selfTowerAlive", True), st.get("selfNexusAlive", True), ALLY_DARK)

    # 유닛 (같은 칸 · 같은 깊이에 겹치면 옆으로 벌린다)
    units = st.get("units") or _units_from_lanes(st)
    seen = {}
    for c, d, enemy in units:
        key = (c, round(d * 20), enemy)
        k = seen.get(key, 0)
        seen[key] = k + 1
        dx = ((k % 5) - 2) * 14 + (6 if enemy else -6)
        dy = (k // 5) * 7
        cv.circle(_lane_x(c) + dx, _depth_y(d) - dy, 5, ENEMY if enemy else ALLY)

    # 날아오는 투사체 (내 칸, 도착까지 남은 시간 → 깊이)
    inc = st.get("incoming") or {}
    if inc.get("count") and inc.get("column") is not None:
        eta = inc.get("eta") if inc.get("eta") is not None else 2.0
        cv.diamond(_lane_x(inc["column"]), _depth_y(min(0.95, max(0.02, eta / 2.5))), 9, SHOT)

    # 플레이어
    cv.triangle(_lane_x(st.get("selfColumn", 1)), 455, 17, True, ALLY)
    if opps:
        for i, o in enumerate(opps[:3]):
            if o.get("column") is not None and o.get("hp", 1) != 0:
                cv.triangle(_lane_x(o["column"]) + (i - (len(opps) - 1) / 2) * 20, 34, 18, False, FOE_SHADES[i % 3])
    elif st.get("opponentColumn") is not None:
        cv.triangle(_lane_x(st["opponentColumn"]), 34, 22, False, ENEMY)

    # HP
    if opps:
        for i, o in enumerate(opps[:3]):
            hp = o.get("hp", -1)
            frac = (hp / (st.get("opponentMaxHp") or 1000)) if hp >= 0 else 1.0
            cv.rect(8, 2 + i * 4, 8 + (W - 16) * min(1.0, max(0.0, frac)), 5 + i * 4, FOE_SHADES[i % 3])
    else:
        _hp_bar(cv, 2, st.get("opponentHp", 1000) / (st.get("opponentMaxHp") or 1000), ENEMY, ENEMY_DARK)
    _hp_bar(cv, H - 8, st.get("selfHp", 1000) / (st.get("selfMaxHp") or 1000), ALLY, ALLY_DARK)

    # 덱 슬롯
    slots = st.get("slots") or []
    for s in slots[:8]:
        x0 = 8 + s["slot"] * 46
        cv.rect(x0, 474, x0 + 40, 500, READY if s.get("ready") else COOL)
        if s.get("high"):
            cv.frame(x0, 474, x0 + 40, 500, GOLD, 3)
    return cv.png()


def data_url(st):
    return "data:image/png;base64," + base64.b64encode(render(st)).decode("ascii")
