# -*- coding: utf-8 -*-
"""
War of Wizards — MediaPipe 손 랜드마크(21점) → 손 자세 · 게임 입력 이벤트

MediaPipe 와 카메라에 의존하지 않는 순수 로직이라 가짜 랜드마크로 시험할 수 있다.
마스터 프롬프트 §6 입력 체계 / 계약서 IPlayerInput 이벤트와 1:1 로 맞춘다.

    오른손  핀치 시작            → pinch_start   (IPlayerInput.OnPinchStarted)
            핀치 떼기 + 궤적      → pinch_end     (IPlayerInput.OnPinchEnded(Vector2[]))
            주먹                 → fist          (IPlayerInput.OnFistDetected)
            검지로 가리키기        → point         (IPlayerInput.OnPointDetected)
            손바닥 펴기           → palm_open     (IPlayerInput.OnPalmOpenDetected)
    왼손    손바닥 편 채 좌/우 휘두르기 → move (-1 / +1)  (IPlayerInput.OnMoveGesture)
    양손    추적 시작/끊김        → tracking      (IsRightHandTracked / IsLeftHandTracked)

웹캠 기본 입력은 룬 그리기 대신 손 모양이다 (ShapeHand):
    오른손  손 모양을 유지         → shape         (SKILL_SHAPES 의 스킬을 고른다, 손 위치 = 조준 커서)

좌표: 랜드마크는 MediaPipe 그대로(x 오른쪽, y 아래, 0~1, 거울 모드 화면 기준).
      밖으로 내보내는 좌표(궤적 · 조준점)는 y 를 뒤집어 "y 위쪽 +" 로 보낸다 (Unity Vector2 와 같은 방향).
"""

import math
from collections import deque

WRIST = 0
THUMB_TIP = 4
FINGERS = {             # (MCP, PIP, DIP, TIP)
    "index": (5, 6, 7, 8),
    "middle": (9, 10, 11, 12),
    "ring": (13, 14, 15, 16),
    "pinky": (17, 18, 19, 20),
}
PALM_POINTS = (0, 5, 9, 13, 17)

# 손 모양 → 스킬. 패턴은 (엄지, 검지, 중지, 약지, 새끼) 순서로 1 = 폄, 0 = 접음
SKILL_SHAPES = {
    "00000": "S01",  # 연막
    "01000": "S02",  # 독 장판
    "01110": "S03",  # 파이어볼
    "01111": "S04",  # 화살 세례
    "11111": "S05",  # 토네이도
    "10000": "S06",  # 번개
    "11100": "S07",  # 총난사
    "11001": "S08",  # 방어 쉴드
    "00100": "S09",  # 타워 버프
    "10100": "S10",  # 집중
    "00111": "S11",  # 도끼 전사
    "00110": "S12",  # 드래곤
    "11110": "S13",  # 터렛
    "01100": "H01",  # 프리즈
    "10001": "H02",  # 레이저 빔
    "11000": "H03",  # 흡혈
    "01001": "H04",  # 반사 결계
    "00001": "H05",  # 분노
    "00011": "H06",  # 소환탑
}


class Settings:
    """판정 기준값. 카메라 · 조명 · 손 크기에 따라 미리보기 창 숫자를 보며 조정한다."""
    pinch_on = 0.22          # 엄지끝-검지끝 거리 / 손바닥 크기 가 이보다 작으면 핀치 시작 (손끝을 붙이면 약 0.1)
    pinch_off = 0.35         # 이보다 커지면 핀치 끝 (사이 구간은 이전 상태 유지 = 떨림 방지)
    #                          손가락을 붙여 편 손바닥은 약 0.38 (MediaPipe 공식 예제 사진 측정)
    extended = 1.15          # 손목-손끝 거리가 손목-PIP 거리의 이 배수보다 크면 "편 손가락"
    curled = 1.00            # 이 배수보다 작으면 "접은 손가락"
    deep_curl = 0.80         # 검지가 이보다 더 접혀 있으면 핀치가 아니라 주먹 (주먹 쥘 때 엄지·검지가 닿는 오인 방지)
    stable_frames = 3        # 자세가 이 프레임 수만큼 이어져야 인정 (핀치 · 주먹 · 가리키기)
    palm_stable_frames = 8   # 손바닥(취소)은 더 오래 유지해야 인정 — 핀치를 떼며 손을 펴는 실수 방지
    palm_grace_sec = 0.6     # 핀치를 뗀 직후 이 시간 동안은 손바닥 취소를 무시
    lost_sec = 0.25          # 손이 이 시간 이상 안 보이면 "추적 끊김"
    min_trail_len = 0.06     # 궤적 길이(화면 폭 기준)가 이보다 짧으면 룬으로 보지 않음
    max_trail_points = 400
    smooth = 0.5             # 궤적 지수 평활 (0 = 평활 없음, 1 = 안 움직임)
    swipe_dist = 0.10        # 왼손 휘두르기: 이 거리(화면 폭 기준) 이상
    swipe_speed = 0.55       #               이 속도(화면 폭/초) 이상
    swipe_window = 0.30      #               이 시간(초) 안에서 측정
    swipe_cooldown = 0.70    # 연타 방지
    aim_mode = "position"    # "position" = 검지 끝이 화면 좌/중/우 어디에 있나, "angle" = 검지가 기운 방향
    aim_angle = 18.0         # angle 모드에서 좌/우로 판정하는 기울기(도)
    shape_hold_frames = 15   # 손 모양을 이 프레임 수만큼 유지해야 스킬을 고른다 (환경설정 "유지 시간")
    shape_cooldown = 0.8     # 같은 스킬을 다시 고를 수 있는 최소 간격(초)
    cursor_margin = 0.15     # 조준 커서: 화면 가장자리의 이 비율은 쓰지 않는다 (손이 화면 밖으로 나가지 않고 끝까지 닿게)


def _xyz(lm, i, aspect):
    p = lm[i]
    z = p[2] if len(p) > 2 else 0.0
    return (p[0] * aspect, p[1], z * aspect)


def _dist(a, b):
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def hand_features(lm, aspect=4 / 3, s=Settings):
    """21개 랜드마크 → 판정용 수치. aspect = 화면 가로/세로 (x, y 축척을 맞추기 위함)."""
    w = _xyz(lm, WRIST, aspect)
    palm = _dist(w, _xyz(lm, 9, aspect)) or 1e-6
    fingers = {}
    for name, (mcp, pip, dip, tip) in FINGERS.items():
        ratio = _dist(w, _xyz(lm, tip, aspect)) / (_dist(w, _xyz(lm, pip, aspect)) or 1e-6)
        fingers[name] = ratio
    pinch = _dist(_xyz(lm, THUMB_TIP, aspect), _xyz(lm, 8, aspect)) / palm
    return {"palm": palm, "fingers": fingers, "pinch": pinch}


def classify_pose(lm, aspect=4 / 3, pinching=False, s=Settings):
    """
    한 프레임의 손 자세: "pinch" | "fist" | "point" | "open" | "other"
    pinching=True 이면 핀치 해제 기준(pinch_off)을 써서 그리는 도중 끊기지 않게 한다.
    """
    f = hand_features(lm, aspect, s)
    r = f["fingers"]
    ext = {k: v > s.extended for k, v in r.items()}
    cur = {k: v < s.curled for k, v in r.items()}

    limit = s.pinch_off if pinching else s.pinch_on
    if f["pinch"] < limit and r["index"] > s.deep_curl:
        return "pinch", f
    if all(cur.values()):
        return "fist", f
    if ext["index"] and not ext["middle"] and not ext["ring"] and not ext["pinky"]:
        return "point", f
    if all(ext.values()):          # 핀치는 위에서 걸렀으므로 엄지 위치는 보지 않는다
        return "open", f
    return "other", f


def finger_pattern(lm, aspect=4 / 3):
    """손가락 5개의 폄/접음을 '01100' 같은 문자열로 (손 방향과 상관없음)."""
    def d(a, b):
        return math.hypot((lm[a][0] - lm[b][0]) * aspect, lm[a][1] - lm[b][1])
    # 엄지: 새끼 뿌리에서 엄지 끝이 엄지 관절보다 멀면 폄 / 나머지: 손목에서 손끝이 둘째 마디보다 멀면 폄
    bits = ["1" if d(4, 17) > d(3, 17) * 1.05 else "0"]
    for tip, pip in ((8, 6), (12, 10), (16, 14), (20, 18)):
        bits.append("1" if d(tip, WRIST) > d(pip, WRIST) else "0")
    return "".join(bits)


def palm_center(lm):
    return (sum(lm[i][0] for i in PALM_POINTS) / len(PALM_POINTS),
            sum(lm[i][1] for i in PALM_POINTS) / len(PALM_POINTS))


def to_up(p):
    """MediaPipe 화면 좌표(y 아래) → 내보내는 좌표(y 위)."""
    return (round(p[0], 4), round(1.0 - p[1], 4))


def aim_from(lm, s=Settings):
    """검지 끝으로 열(0=L,1=C,2=R)과 깊이(0 가까움 ~ 1 멂)를 정한다 (§6.3)."""
    tip = lm[8]
    if s.aim_mode == "angle":
        base = lm[5]
        ang = math.degrees(math.atan2(tip[0] - base[0], base[1] - tip[1]))
        column = 0 if ang < -s.aim_angle else 2 if ang > s.aim_angle else 1
    else:
        column = 0 if tip[0] < 1 / 3 else 2 if tip[0] > 2 / 3 else 1
    depth = min(1.0, max(0.0, (0.85 - tip[1]) / 0.7))
    return column, round(depth, 3)


class _Debounce:
    """같은 값이 n 프레임 이어져야 확정."""

    def __init__(self):
        self.value = None
        self.candidate = None
        self.count = 0

    def push(self, v, need):
        if v == self.candidate:
            self.count += 1
        else:
            self.candidate = v
            self.count = 1
        if self.count >= need and v != self.value:
            self.value = v
            return True
        return False


class RightHand:
    """오른손: 핀치 그리기 · 주먹 · 가리키기 · 손바닥 취소 (§6.2, §6.4)."""

    def __init__(self, s=Settings):
        self.s = s
        self.tracked = False
        self.last_seen = -1e9
        self.pose = "none"
        self._deb = _Debounce()
        self.drawing = False
        self.trail = []
        self._smooth = None
        self._pinch_end_t = -1e9
        self.aim = (1, 0.5)
        self.cursor = None          # 핀치 지점 또는 검지 끝 (y 위 좌표)
        self.features = None

    def update(self, lm, t, aspect=4 / 3):
        s = self.s
        events = []
        if lm is None:
            if self.tracked and t - self.last_seen > s.lost_sec:
                self.tracked = False
                if self.drawing:
                    self.drawing = False
                    self.trail = []
                    events.append({"name": "pinch_cancel", "reason": "tracking_lost"})
                self.pose = "none"
                self._deb = _Debounce()
                events.append({"name": "tracking", "tracked": False})
            return events

        self.last_seen = t
        if not self.tracked:
            self.tracked = True
            events.append({"name": "tracking", "tracked": True})

        raw, self.features = classify_pose(lm, aspect, pinching=self.drawing, s=s)
        if raw == "pinch":
            px = (lm[4][0] + lm[8][0]) / 2
            py = (lm[4][1] + lm[8][1]) / 2
            if self._smooth is None or not self.drawing:
                self._smooth = (px, py)
            else:
                a = s.smooth
                self._smooth = (self._smooth[0] * a + px * (1 - a), self._smooth[1] * a + py * (1 - a))
            self.cursor = to_up(self._smooth)
        else:
            self.cursor = to_up(lm[8])
        if raw == "point":
            self.aim = aim_from(lm, s)

        need = s.palm_stable_frames if raw == "open" else s.stable_frames
        # 그리는 중 잠깐 핀치가 풀려 보이는 프레임은 무시 (핀치 종료는 확정 프레임 수만큼 필요)
        changed = self._deb.push(raw, need)

        if self.drawing and raw == "pinch" and len(self.trail) < s.max_trail_points:
            if not self.trail or math.dist(self.trail[-1], self.cursor) > 0.002:
                self.trail.append(self.cursor)

        if not changed:
            return events
        prev, self.pose = self.pose, self._deb.value

        if self.drawing and self.pose != "pinch":
            self.drawing = False
            self._pinch_end_t = t
            trail, self.trail = self.trail, []
            length = sum(math.dist(trail[i - 1], trail[i]) for i in range(1, len(trail)))
            events.append({"name": "pinch_end", "points": [list(p) for p in trail],
                           "length": round(length, 4), "tooShort": length < s.min_trail_len})

        if self.pose == "pinch":
            self.drawing = True
            self.trail = [self.cursor]
            events.append({"name": "pinch_start"})
        elif self.pose == "fist":
            events.append({"name": "fist"})
        elif self.pose == "point":
            events.append({"name": "point", "column": self.aim[0], "depth": self.aim[1]})
        elif self.pose == "open":
            if t - self._pinch_end_t >= s.palm_grace_sec:
                events.append({"name": "palm_open"})
        return events


class ShapeHand:
    """오른손: 손 모양을 유지하면 그 스킬을 고른다. 손 위치가 조준 커서다. RightHand 와 같은 속성을 쓴다."""

    def __init__(self, s=Settings, hold_frames=0):
        self.s = s
        self.hold_frames = hold_frames or s.shape_hold_frames
        self.tracked = False
        self.last_seen = -1e9
        self.pose = "none"
        self.drawing = False       # 이 입력은 그리지 않는다
        self.trail = []
        self.aim = (1, 0.5)
        self.cursor = None
        self.features = None
        self.pattern = ""          # 지금 보이는 손 모양
        self._count = 0            # 그 모양이 연속으로 나온 프레임 수
        self._current = ""         # 마지막으로 확정한 모양 — 손을 바꾸기 전에는 다시 고르지 않는다
        self._picked = {}          # 스킬별 마지막으로 고른 시각
        self._smooth = None

    @property
    def spell(self):
        return SKILL_SHAPES.get(self.pattern)

    @property
    def progress(self):
        """고르기까지 진행률 0~1 (이미 고른 모양 · 표에 없는 모양은 0)."""
        if self.spell is None or self.pattern == self._current:
            return 0.0
        return round(min(1.0, self._count / self.hold_frames), 3)

    def _track_cursor(self, lm):
        s = self.s
        x, y = palm_center(lm)
        if self._smooth is None:
            self._smooth = (x, y)
        else:
            a = s.smooth
            self._smooth = (self._smooth[0] * a + x * (1 - a), self._smooth[1] * a + y * (1 - a))
        span = 1 - 2 * s.cursor_margin
        self.cursor = to_up(tuple(min(1.0, max(0.0, (v - s.cursor_margin) / span)) for v in self._smooth))

    def update(self, lm, t, aspect=4 / 3):
        s = self.s
        events = []
        if lm is None:
            if self.tracked and t - self.last_seen > s.lost_sec:
                self.tracked = False
                self.pose = "none"
                self.pattern, self._current, self._count = "", "", 0   # 손을 다시 들면 같은 모양도 또 고른다
                self._smooth = None
                events.append({"name": "tracking", "tracked": False})
            return events

        self.last_seen = t
        if not self.tracked:
            self.tracked = True
            events.append({"name": "tracking", "tracked": True})

        self.pose, self.features = classify_pose(lm, aspect, s=s)
        self._track_cursor(lm)

        pattern = finger_pattern(lm, aspect)
        if pattern == self.pattern:
            self._count += 1
        else:
            self.pattern, self._count = pattern, 1

        if self._count == self.hold_frames and pattern != self._current:
            self._current = pattern
            spell = SKILL_SHAPES.get(pattern)
            if spell and t - self._picked.get(spell, -1e9) > s.shape_cooldown:
                self._picked[spell] = t
                events.append({"name": "shape", "spell": spell, "pattern": pattern})
        return events


class LeftHand:
    """왼손: 손바닥 편 채 좌/우로 휘두르면 한 칸 이동 (§6.5)."""

    def __init__(self, s=Settings):
        self.s = s
        self.tracked = False
        self.last_seen = -1e9
        self.pose = "none"
        self.history = deque()
        self._last_move = -1e9

    def update(self, lm, t, aspect=4 / 3):
        s = self.s
        events = []
        if lm is None:
            if self.tracked and t - self.last_seen > s.lost_sec:
                self.tracked = False
                self.pose = "none"
                self.history.clear()
                events.append({"name": "tracking", "tracked": False})
            return events

        self.last_seen = t
        if not self.tracked:
            self.tracked = True
            events.append({"name": "tracking", "tracked": True})

        self.pose, _ = classify_pose(lm, aspect, s=s)
        if self.pose != "open":
            self.history.clear()
            return events

        cx, _ = palm_center(lm)
        self.history.append((t, cx))
        while self.history and t - self.history[0][0] > s.swipe_window:
            self.history.popleft()
        if t - self._last_move < s.swipe_cooldown or len(self.history) < 3:
            return events

        # 창 안의 어느 시점부터 지금까지든 "충분히 멀리 + 충분히 빠르게" 움직였으면 이동
        for t0, x0 in self.history:
            dx = cx - x0
            dt = max(t - t0, 1e-3)
            if abs(dx) >= s.swipe_dist and abs(dx) / dt >= s.swipe_speed:
                # 거울 모드 화면이므로 x 가 커지는 쪽 = 사용자의 오른쪽
                events.append({"name": "move", "direction": 1 if dx > 0 else -1})
                self._last_move = t
                self.history.clear()
                break
        return events
