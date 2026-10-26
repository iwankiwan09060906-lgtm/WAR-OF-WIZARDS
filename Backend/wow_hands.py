# -*- coding: utf-8 -*-
"""
War of Wizards — 서버 MediaPipe 손 추적 (wow_server.py 의 /api/mediapipe/*)

GestureTools/MediaPipe/wow_hand_tracker.py 는 PC 한 대의 웹캠을 직접 읽는다. 이 모듈은 같은 일을 서버에서 한다:
브라우저 · Quest · 다른 PC 가 보낸 카메라 프레임(JPEG)을 MediaPipe Hand Landmarker 로 읽고,
손 추적기와 똑같은 손 자세 로직(hand_gestures.py) · 룬 인식기(wow_runes.py)로 게임 입력 이벤트를 만든다.

    입력 ① image     : 카메라 프레임 (data:image/jpeg;base64,...) — 서버에 mediapipe 가 있어야 한다
    입력 ② hands     : 이미 뽑은 랜드마크 {"right": [x,y,z]*21, "left": ...} (MediaPipe JS 등) — 패키지 없이 동작
    출력            : 손 추적기 UDP 와 같은 frame 메시지 + 이번 프레임의 이벤트
                      (pinch_start · pinch_end(+룬) · fist · point(열 · 깊이) · palm_open · move · tracking)

플레이어(세션)마다 Landmarker · 손 상태를 따로 둔다 — 2:2 네 명이 동시에 보내도 섞이지 않는다.
mediapipe 는 numpy 2.x 를 끌어와 다른 도구와 부딪히므로 전역이 아니라 GestureTools/MediaPipe/.venv 에 설치하고
Backend/run_server_ai.bat 로 그 파이썬에서 서버를 띄운다.
"""

import base64
import json
import os
import socket
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
MP_DIR = os.path.join(os.path.dirname(HERE), "GestureTools", "MediaPipe")
if MP_DIR not in sys.path:
    sys.path.append(MP_DIR)
import hand_gestures as hg  # noqa: E402  (순수 파이썬)
import wow_runes  # noqa: E402

SESSION_IDLE_SEC = 60          # 이만큼 프레임이 안 오면 세션(Landmarker)을 닫는다
MAX_SESSIONS = 8               # 2:2 네 명 + 여유
MAX_FRAME_BYTES = 1_500_000

_SESSIONS = {}
_SESSIONS_LOCK = threading.Lock()
_DETECTOR = None               # (mp, cv2, np, wow_hand_tracker, 모델 바이트)
_DETECTOR_ERR = ""
_DETECTOR_LOCK = threading.Lock()
_UDP = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)


def _detector():
    """mediapipe · cv2 · 모델을 처음 쓸 때 한 번만 불러온다. 없으면 RuntimeError (서버는 계속 동작)."""
    global _DETECTOR, _DETECTOR_ERR
    with _DETECTOR_LOCK:
        if _DETECTOR is None:
            try:
                import cv2
                import numpy as np
                import mediapipe as mp
                import wow_hand_tracker as wt
            except ImportError as e:
                _DETECTOR_ERR = (f"이 파이썬({sys.executable})에 {e.name or 'mediapipe'} 가 없습니다 — "
                                 "Backend/run_server_ai.bat 로 서버를 실행하세요")
                raise RuntimeError(_DETECTOR_ERR)
            try:
                model = wt.load_model(wt.DEFAULT_MODEL)       # 없으면 공식 모델(7.8MB)을 한 번 받는다
            except Exception as e:
                _DETECTOR_ERR = f"손 모델(hand_landmarker.task)을 불러오지 못했습니다: {type(e).__name__}: {e}"
                raise RuntimeError(_DETECTOR_ERR)
            _DETECTOR = (mp, cv2, np, wt, model)
            _DETECTOR_ERR = ""
        return _DETECTOR


def status():
    """GET /api/mediapipe/status — 그림 입력을 쓸 수 있는지 (모델은 아직 안 불러온다)"""
    img_ok, why = True, ""
    if _DETECTOR is None:
        try:
            import importlib.util
            missing = [m for m in ("mediapipe", "cv2", "numpy") if importlib.util.find_spec(m) is None]
        except Exception:
            missing = ["mediapipe"]
        if missing:
            img_ok, why = False, (f"이 파이썬에 {', '.join(missing)} 가 없습니다 — Backend/run_server_ai.bat 로 서버를 실행하세요")
        elif _DETECTOR_ERR:
            img_ok, why = False, _DETECTOR_ERR
    model = os.path.join(MP_DIR, "models", "hand_landmarker.task")
    with _SESSIONS_LOCK:
        n = len(_SESSIONS)
    return {"image": {"available": img_ok, "reason": why, "model": os.path.exists(model)},
            "landmarks": {"available": True}, "python": sys.executable, "sessions": n,
            "runes": len(wow_runes.SPELLS)}


class HandSession:
    def __init__(self, key):
        self.key = key
        self.right = hg.RightHand()
        self.left = hg.LeftHand()
        self.landmarker = None
        self.last_ts = -1
        self.last_t = -1.0
        self.t0 = time.monotonic()
        self.used = time.monotonic()
        self.lock = threading.Lock()
        self.frames = 0
        self.fps = 0.0
        self._fps_t, self._fps_n = time.monotonic(), 0
        self.candidates = list(wow_runes.SPELLS)

    def close(self):
        if self.landmarker is not None:
            try:
                self.landmarker.close()
            except Exception:
                pass
            self.landmarker = None


def _session(key):
    now = time.monotonic()
    with _SESSIONS_LOCK:
        for k in [k for k, s in _SESSIONS.items() if now - s.used > SESSION_IDLE_SEC]:
            _SESSIONS.pop(k).close()
        s = _SESSIONS.get(key)
        if s is None:
            if len(_SESSIONS) >= MAX_SESSIONS:
                oldest = min(_SESSIONS, key=lambda k: _SESSIONS[k].used)
                _SESSIONS.pop(oldest).close()
            s = _SESSIONS[key] = HandSession(key)
        s.used = now
        return s


def reset(key):
    with _SESSIONS_LOCK:
        s = _SESSIONS.pop(key, None)
    if s:
        with s.lock:
            s.close()
    return s is not None


def _landmarks(raw):
    """[x,y,z]*21 · {x,y,z}*21 · 평평한 63개 → [(x,y,z)]*21 (MediaPipe 좌표: 0~1, y 아래), 아니면 None"""
    if not raw:
        return None
    try:
        if isinstance(raw[0], (int, float)):
            pts = [tuple(float(v) for v in raw[i:i + 3]) for i in range(0, len(raw), 3)]
        elif isinstance(raw[0], dict):
            pts = [(float(p["x"]), float(p["y"]), float(p.get("z", 0.0))) for p in raw]
        else:
            pts = [(float(p[0]), float(p[1]), float(p[2]) if len(p) > 2 else 0.0) for p in raw]
    except (KeyError, IndexError, TypeError, ValueError):
        return None
    return pts if len(pts) == 21 else None


def _hand_packet(tracker, lm):
    """손 추적기 UDP frame 메시지의 손 하나 (wow_hand_tracker.hand_packet 과 같은 모양 — y 위쪽 +)"""
    if lm is None or not tracker.tracked:
        return {"tracked": False, "pose": "none"}
    flat = []
    for p in lm:
        flat += [round(p[0], 4), round(1 - p[1], 4), round(p[2], 4)]
    pkt = {"tracked": True, "pose": tracker.pose, "landmarks": flat}
    if isinstance(tracker, hg.RightHand):
        pkt.update({"drawing": tracker.drawing, "cursor": [round(v, 4) for v in (tracker.cursor or (0, 0))],
                    "column": tracker.aim[0], "depth": round(tracker.aim[1], 3),
                    "pinch": round(tracker.features["pinch"], 3) if tracker.features else 0.0})
    else:
        pkt["palm"] = [round(v, 4) for v in hg.to_up(hg.palm_center(lm))]
    return pkt


def _split(result, swap):
    """MediaPipe 결과 → (오른손, 왼손). 거울 모드 화면이라 'Right' = 사용자의 오른손 (손 추적기와 같다)"""
    hands = []
    for i, lms in enumerate(result.hand_landmarks or []):
        label = result.handedness[i][0].category_name if result.handedness and result.handedness[i] else "Right"
        hands.append([label, [(p.x, p.y, p.z) for p in lms]])
    if len(hands) == 2 and hands[0][0] == hands[1][0]:
        hands.sort(key=lambda h: hg.palm_center(h[1])[0])
        hands[0][0], hands[1][0] = "Left", "Right"
    right = next((h[1] for h in hands if h[0] == "Right"), None)
    left = next((h[1] for h in hands if h[0] == "Left"), None)
    return (left, right) if swap else (right, left)


def _decode(image):
    if image.startswith("data:"):
        image = image.split(",", 1)[-1]
    try:
        raw = base64.b64decode(image, validate=False)
    except ValueError:
        raise ValueError("image 는 base64 JPEG/PNG 이어야 합니다.")
    if len(raw) > MAX_FRAME_BYTES:
        raise ValueError(f"프레임이 너무 큽니다 ({len(raw) // 1024} KB) — 640x480 JPEG 정도로 보내 주세요.")
    return raw


def process(key, image=None, hands=None, t_ms=None, mirror=True, swap=False, candidates=None, aspect=None, forward_to=None):
    """프레임 하나 처리 → frame 메시지 + events. forward_to=(host, port) 면 Unity MediaPipeHandInput 으로도 보낸다."""
    s = _session(key)
    with s.lock:
        t_start = time.perf_counter()
        # 브라우저가 준 시각(ms)을 쓰면 네트워크 지연이 들쭉날쭉해도 휘두르기 속도가 맞다. 시각이 되감기면 새 세션.
        now = (t_ms / 1000.0) if isinstance(t_ms, (int, float)) else time.monotonic() - s.t0
        if now < s.last_t - 1.0:
            s.close()
            s.right, s.left, s.last_ts = hg.RightHand(), hg.LeftHand(), -1
        s.last_t = now
        if candidates is not None:
            s.candidates = [str(c).upper() for c in candidates if str(c).upper() in wow_runes.SPELLS]

        size = None
        if image:
            mp, cv2, np, wt, model = _detector()
            frame = cv2.imdecode(np.frombuffer(_decode(image), np.uint8), cv2.IMREAD_COLOR)
            if frame is None:
                raise ValueError("프레임 그림을 읽지 못했습니다 (JPEG/PNG 만).")
            if mirror:
                frame = cv2.flip(frame, 1)
            h, w = frame.shape[:2]
            size, aspect = [w, h], w / h
            if s.landmarker is None:
                s.landmarker = wt.create_landmarker(model)
            ts = int(now * 1000)
            if ts <= s.last_ts:
                ts = s.last_ts + 1
            s.last_ts = ts
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = s.landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)
            lm_r, lm_l = _split(result, swap)
            source = "image"
        else:
            hands = hands or {}
            lm_r, lm_l = _landmarks(hands.get("right")), _landmarks(hands.get("left"))
            if swap:
                lm_r, lm_l = lm_l, lm_r
            aspect = float(aspect or 4 / 3)
            source = "landmarks"

        events = [("right", e) for e in s.right.update(lm_r, now, aspect)]
        events += [("left", e) for e in s.left.update(lm_l, now, aspect)]
        out_events = []
        for hand, ev in events:
            msg = {"type": "event", "t": round(now, 3), "hand": hand}
            msg.update(ev)
            if ev["name"] == "pinch_end":
                if ev["tooShort"]:
                    rune = {"recognized": False, "reason": "too_short", "spellId": None, "score": 0.0}
                else:
                    rune = wow_runes.recognize(ev["points"], s.candidates)
                rune = dict(rune)
                rune["ranking"] = [{"spellId": c, "score": sc} for c, sc in rune.get("ranking", [])]
                msg["rune"] = rune
                msg["points"] = [{"x": round(p[0], 4), "y": round(p[1], 4)} for p in ev["points"]]
            out_events.append(msg)

        s.frames += 1
        s._fps_n += 1
        wall = time.monotonic()
        if wall - s._fps_t >= 1.0:
            s.fps, s._fps_t, s._fps_n = s._fps_n / (wall - s._fps_t), wall, 0
        frame_msg = {"type": "frame", "t": round(now, 3), "fps": round(s.fps, 1),
                     "right": _hand_packet(s.right, lm_r), "left": _hand_packet(s.left, lm_l)}
        if forward_to:
            for m in out_events + [frame_msg]:
                try:
                    _UDP.sendto(json.dumps(m, ensure_ascii=False).encode("utf-8"), forward_to)
                except OSError:
                    break
        frame_msg.update({"events": out_events, "source": source, "size": size, "candidates": len(s.candidates),
                          "ms": round((time.perf_counter() - t_start) * 1000, 1), "frames": s.frames})
        return frame_msg
