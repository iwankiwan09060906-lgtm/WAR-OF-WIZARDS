# -*- coding: utf-8 -*-
"""
War of Wizards — MediaPipe 손 추적기 (6단계: AI 손동작 인식)

웹캠 → MediaPipe Hand Landmarker(양손 21점) → 손 자세 · 룬 인식 → UDP(JSON) → Unity 클라이언트

    python wow_hand_tracker.py                     # 기본: 카메라 0번, 127.0.0.1:5065 로 송출
    python wow_hand_tracker.py --deck S03,S06,S07,S08,S11,S13,H01,H02
    python wow_hand_tracker.py --recognizer server --server http://localhost:8080
    python wow_hand_tracker.py --video 손영상.mp4   # 카메라 대신 영상 파일

미리보기 창 단축키: q/Esc 종료 · h 왼손/오른손 바꾸기 · a 조준 방식(위치/기울기) · r 룬 안내 켜기/끄기

메시지 형식은 Docs/Input/MediaPipeHandInput.md 참고.
"""

import argparse
import json
import math
import os
import socket
import sys
import threading
import time
import urllib.request
from collections import deque

import cv2
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hand_gestures as hg  # noqa: E402
import wow_runes  # noqa: E402

MODEL_URL = ("https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
             "hand_landmarker/float16/latest/hand_landmarker.task")
DEFAULT_MODEL = os.path.join(HERE, "models", "hand_landmarker.task")

HAND_LINES = [(0, 1), (1, 2), (2, 3), (3, 4), (0, 5), (5, 6), (6, 7), (7, 8), (5, 9), (9, 10), (10, 11),
              (11, 12), (9, 13), (13, 14), (14, 15), (15, 16), (13, 17), (17, 18), (18, 19), (19, 20), (0, 17)]
POSE_KO = {"pinch": "핀치(그리기)", "fist": "주먹", "point": "가리키기", "open": "손바닥",
           "other": "-", "none": "안 보임"}
COLUMN_KO = ["왼쪽", "가운데", "오른쪽"]
EVENT_KO = {"pinch_start": "핀치 시작 → 그리기", "pinch_end": "핀치 끝", "pinch_cancel": "그리기 취소",
            "fist": "주먹 → 스킬 준비", "point": "가리키기 → 조준", "palm_open": "손바닥 → 취소",
            "move": "이동", "tracking": "추적"}


# ─────────────────────────────────────────────────────────────────────────────
# 모델 · 카메라
# ─────────────────────────────────────────────────────────────────────────────
def load_model(path):
    if not os.path.exists(path):
        print(f"[모델] {path} 이(가) 없어 MediaPipe 공식 모델을 받습니다 (약 7.8MB)...")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        urllib.request.urlretrieve(MODEL_URL, path)
    # 한글 · 공백이 든 경로를 MediaPipe C++ 쪽이 못 여는 경우가 있어 바이트로 넘긴다.
    with open(path, "rb") as f:
        return f.read()


def create_landmarker(model_bytes, num_hands=2):
    import mediapipe as mp
    from mediapipe.tasks.python import vision
    options = vision.HandLandmarkerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_buffer=model_bytes),
        running_mode=vision.RunningMode.VIDEO,
        num_hands=num_hands,
        min_hand_detection_confidence=0.5,
        min_hand_presence_confidence=0.5,
        min_tracking_confidence=0.5,
    )
    return vision.HandLandmarker.create_from_options(options)


def open_capture(args):
    if args.video:
        return cv2.VideoCapture(args.video)
    cap = cv2.VideoCapture(args.camera, cv2.CAP_DSHOW) if os.name == "nt" else cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        cap = cv2.VideoCapture(args.camera)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
    return cap


def split_hands(result, swap=False):
    """MediaPipe 결과 → (오른손 랜드마크, 왼손 랜드마크). 거울 모드 화면이므로 'Right' = 사용자의 오른손."""
    hands = []
    for i, lms in enumerate(result.hand_landmarks or []):
        label = result.handedness[i][0].category_name if result.handedness and result.handedness[i] else "Right"
        pts = [(p.x, p.y, p.z) for p in lms]
        hands.append([label, pts])
    if len(hands) == 2 and hands[0][0] == hands[1][0]:
        # 두 손이 같은 쪽으로 분류되면 화면 위치로 정한다 (화면 오른쪽 = 오른손)
        hands.sort(key=lambda h: hg.palm_center(h[1])[0])
        hands[0][0], hands[1][0] = "Left", "Right"
    right = next((h[1] for h in hands if h[0] == "Right"), None)
    left = next((h[1] for h in hands if h[0] == "Left"), None)
    return (left, right) if swap else (right, left)


# ─────────────────────────────────────────────────────────────────────────────
# 룬 인식 (로컬 또는 백엔드 6단계 API)
# ─────────────────────────────────────────────────────────────────────────────
def recognize_rune(points, candidates, args):
    if args.recognizer == "server":
        try:
            body = json.dumps({"points": [{"x": x, "y": y} for x, y in points],
                               "candidates": candidates}).encode("utf-8")
            req = urllib.request.Request(args.server.rstrip("/") + "/api/stage6/ai/recognize", data=body,
                                         headers={"Content-Type": "application/json"}, method="POST")
            with urllib.request.urlopen(req, timeout=1.0) as resp:
                r = json.loads(resp.read().decode("utf-8"))["result"]
            r.setdefault("score", r.get("confidence", 0.0))   # 서버 응답 이름 → 로컬 결과 이름으로 맞춤
            r.setdefault("gap", r.get("scoreGap", 0.0))
            r["source"] = "server"
            return r
        except Exception as e:  # 서버가 꺼져 있어도 게임은 계속되어야 한다
            r = wow_runes.recognize(points, candidates)
            r["source"] = f"local (서버 실패: {type(e).__name__})"
            return r
    r = wow_runes.recognize(points, candidates)
    r["source"] = "local"
    return r


# ─────────────────────────────────────────────────────────────────────────────
# UDP 송수신
# ─────────────────────────────────────────────────────────────────────────────
class Link:
    """Unity 로 보내는 소켓 + Unity 에서 오는 제어 메시지(후보 룬 목록 등)를 받는 소켓."""

    def __init__(self, host, port, listen_port):
        self.target = (host, port)
        self.out = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.inp = None
        if listen_port:
            self.inp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            self.inp.bind(("0.0.0.0", listen_port))
            self.inp.setblocking(False)
        self.seq = 0
        self.last_peer = -1e9
        self.sent = 0

    def send(self, msg):
        self.seq += 1
        msg["seq"] = self.seq
        data = json.dumps(msg, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        try:
            self.out.sendto(data, self.target)
            self.sent += 1
        except OSError:
            pass  # 받는 쪽이 없으면 Windows 가 오류를 돌려줄 수 있다 — 무시

    def poll(self):
        msgs = []
        while self.inp:
            try:
                data, _ = self.inp.recvfrom(65535)
            except (BlockingIOError, ConnectionResetError, OSError):
                break
            try:
                msgs.append(json.loads(data.decode("utf-8")))
                self.last_peer = time.monotonic()
            except ValueError:
                pass
        return msgs

    def close(self):
        self.out.close()
        if self.inp:
            self.inp.close()


# Unity JsonUtility 는 중첩 배열([[x,y],...])을 못 읽으므로
# 좌표 목록은 {"x","y"} 객체 배열, 21점은 63개짜리 평평한 배열로 보낸다.
def points_json(points):
    return [{"x": round(p[0], 4), "y": round(p[1], 4)} for p in points]


def rune_json(r):
    r = dict(r)
    r["ranking"] = [{"spellId": c, "score": s} for c, s in r.get("ranking", [])]
    return r


def hand_packet(tracker, lm, aspect):
    if lm is None or not tracker.tracked:
        return {"tracked": False, "pose": "none"}
    flat = []
    for p in lm:
        flat += [round(p[0], 4), round(1 - p[1], 4), round(p[2], 4)]
    pkt = {"tracked": True, "pose": tracker.pose, "landmarks": flat}
    if isinstance(tracker, hg.RightHand):
        pkt.update({"drawing": tracker.drawing, "cursor": list(tracker.cursor or (0, 0)),
                    "column": tracker.aim[0], "depth": tracker.aim[1],
                    "pinch": round(tracker.features["pinch"], 3) if tracker.features else 0.0})
    else:
        pkt["palm"] = list(hg.to_up(hg.palm_center(lm)))
    return pkt


# ─────────────────────────────────────────────────────────────────────────────
# 미리보기 창
# ─────────────────────────────────────────────────────────────────────────────
class Overlay:
    """OpenCV 는 한글을 못 쓰므로 글자는 모아 두었다가 Pillow 로 한 번에 그린다."""

    def __init__(self):
        self.texts = []
        try:
            from PIL import ImageFont
            self.fonts = {s: ImageFont.truetype("C:/Windows/Fonts/malgun.ttf", s) for s in (14, 18, 26)}
        except Exception:
            self.fonts = None

    def text(self, pos, s, color=(255, 255, 255), size=18):
        self.texts.append((pos, s, color, size))

    def flush(self, img):
        if not self.texts:
            return img
        if self.fonts is None:
            for (x, y), s, c, size in self.texts:
                cv2.putText(img, s.encode("ascii", "ignore").decode(), (x, y + size), cv2.FONT_HERSHEY_SIMPLEX,
                            size / 30, c[::-1], 1, cv2.LINE_AA)
            self.texts = []
            return img
        from PIL import Image, ImageDraw
        pil = Image.fromarray(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
        draw = ImageDraw.Draw(pil)
        for (x, y), s, c, size in self.texts:
            draw.text((x + 1, y + 1), s, font=self.fonts[size], fill=(0, 0, 0))
            draw.text((x, y), s, font=self.fonts[size], fill=c)
        self.texts = []
        return cv2.cvtColor(np.array(pil), cv2.COLOR_RGB2BGR)


def px(p, w, h):
    return int(p[0] * w), int(p[1] * h)


def up_px(p, w, h):
    return int(p[0] * w), int((1 - p[1]) * h)


def draw_hand(img, lm, color, w, h):
    for a, b in HAND_LINES:
        cv2.line(img, px(lm[a], w, h), px(lm[b], w, h), color, 2, cv2.LINE_AA)
    for i, p in enumerate(lm):
        cv2.circle(img, px(p, w, h), 5 if i in (4, 8) else 3, (255, 255, 255), -1, cv2.LINE_AA)


def draw_rune_icon(img, code, x, y, size, color):
    pts = wow_runes.RUNE_SHAPES[code]
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    s = max(max(xs) - min(xs), max(ys) - min(ys)) or 1
    mx, my = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    k = size * 0.8 / s
    poly = np.array([(int(x + size / 2 + (a - mx) * k), int(y + size / 2 - (b - my) * k)) for a, b in pts], np.int32)
    cv2.polylines(img, [poly], False, color, 2, cv2.LINE_AA)
    cv2.circle(img, tuple(poly[0]), 3, (90, 230, 90), -1)


def render(frame, ov, st, args):
    h, w = frame.shape[:2]
    img = frame
    right, left = st["right"], st["left"]

    # 조준 3칸 (오른손 가리키기 중)
    if right.tracked and right.pose == "point" and args.aim == "position":
        layer = img.copy()
        cv2.rectangle(layer, (right.aim[0] * w // 3, 0), ((right.aim[0] + 1) * w // 3, h), (40, 160, 255), -1)
        img = cv2.addWeighted(layer, 0.18, img, 0.82, 0)
        for k in (1, 2):
            cv2.line(img, (k * w // 3, 0), (k * w // 3, h), (40, 160, 255), 1)

    if st["lm_right"] is not None:
        draw_hand(img, st["lm_right"], (40, 200, 255), w, h)
    if st["lm_left"] is not None:
        draw_hand(img, st["lm_left"], (255, 200, 60), w, h)

    # 그리는 중인 궤적 / 마지막 룬
    if right.drawing and len(right.trail) > 1:
        cv2.polylines(img, [np.array([up_px(p, w, h) for p in right.trail], np.int32)], False, (0, 140, 255), 5, cv2.LINE_AA)
    now = st.get("now", time.monotonic())
    last = st["last_rune"]
    if last and now - last["t"] < 2.5:
        col = (80, 220, 80) if last["rune"].get("recognized") else (80, 80, 230)
        if len(last["points"]) > 1:
            cv2.polylines(img, [np.array([up_px(p, w, h) for p in last["points"]], np.int32)], False, col, 3, cv2.LINE_AA)
        r = last["rune"]
        if r.get("recognized"):
            ov.text((w // 2 - 150, h // 2 - 20), f"{r['spellId']} {r['name']}  {r['score']:.2f}", (120, 255, 120), 26)
        else:
            why = {"too_short": "너무 짧음", "low_score": "모양이 달라요", "ambiguous": "두 룬과 비슷해요",
                   "no_candidates": "쓸 수 있는 슬롯 없음"}.get(r.get("reason"), r.get("reason", ""))
            best = f" (가장 비슷: {r['spellId']} {r['score']:.2f})" if r.get("spellId") else ""
            ov.text((w // 2 - 170, h // 2 - 20), f"인식 실패: {why}{best}", (255, 120, 120), 26)

    # 상단 상태줄
    cv2.rectangle(img, (0, 0), (w, 58), (20, 20, 20), -1)
    peer = time.monotonic() - st["link"].last_peer < 3
    ov.text((10, 6), f"UDP → {args.host}:{args.port}   Unity 응답: {'연결됨' if peer else '대기 (보내기는 계속)'}"
                     f"   FPS {st['fps']:.0f}   인식기: {args.recognizer}", (230, 230, 230), 18)
    rp = f"오른손: {POSE_KO.get(right.pose, right.pose)}" if right.tracked else "오른손: 안 보임"
    if right.tracked and right.features:
        fr = right.features["fingers"]
        rp += (f"  [핀치 {right.features['pinch']:.2f} (시작<{hg.Settings.pinch_on} 끝>{hg.Settings.pinch_off})"
               f"  손가락 {fr['index']:.2f} {fr['middle']:.2f} {fr['ring']:.2f} {fr['pinky']:.2f}"
               f" (편>{hg.Settings.extended} 접<{hg.Settings.curled})]")
    if right.tracked and right.pose == "point":
        rp += f"  조준 {COLUMN_KO[right.aim[0]]} 깊이 {right.aim[1]:.2f}"
    lp = f"왼손: {POSE_KO.get(left.pose, left.pose)}" if left.tracked else "왼손: 안 보임"
    ov.text((10, 32), rp + "     " + lp, (255, 210, 120), 18)

    # 최근 이벤트
    y = 70
    for t_ev, s in list(st["log"])[-6:]:
        a = max(0.35, 1 - (now - t_ev) / 6)
        ov.text((10, y), s, tuple(int(c * a) for c in (255, 255, 255)), 18)
        y += 24

    # 룬 안내 (후보만)
    if st["show_runes"]:
        cands = st["candidates"]
        size = 54
        x0 = w - size - 8
        for i, code in enumerate(cands[:10]):
            yy = 66 + i * (size + 18)
            cv2.rectangle(img, (x0 - 2, yy - 2), (x0 + size + 2, yy + size + 2), (30, 30, 30), -1)
            draw_rune_icon(img, code, x0, yy, size, (90, 200, 255) if code[0] == "S" else (80, 120, 255))
            ov.text((x0 - 96, yy + 18), f"{code} {wow_runes.SPELLS[code][0]}", (230, 230, 230), 14)

    cv2.rectangle(img, (0, h - 44), (w, h), (20, 20, 20), -1)
    ov.text((10, h - 42), "오른손: 핀치한 채 룬 그리기 → 떼기 → 주먹 → 검지로 가리키기 1초 · 손바닥 = 취소"
                          "   |   왼손: 손바닥 펴고 좌/우로 휘두르기 = 이동", (200, 200, 200), 14)
    ov.text((10, h - 22), "q 종료 · h 왼손/오른손 바꾸기 · a 조준 방식(위치/기울기) · r 룬 안내 켜기/끄기", (160, 160, 160), 14)
    return ov.flush(img)


# ─────────────────────────────────────────────────────────────────────────────
# 메인 루프
# ─────────────────────────────────────────────────────────────────────────────
def parse_args(argv=None):
    p = argparse.ArgumentParser(description="War of Wizards MediaPipe 손 추적기")
    p.add_argument("--camera", type=int, default=0, help="카메라 번호 (기본 0)")
    p.add_argument("--video", help="카메라 대신 쓸 영상 파일")
    p.add_argument("--width", type=int, default=960)
    p.add_argument("--height", type=int, default=540)
    p.add_argument("--host", default="127.0.0.1", help="Unity 가 실행 중인 PC (기본 같은 PC)")
    p.add_argument("--port", type=int, default=5065, help="Unity 가 받는 UDP 포트")
    p.add_argument("--listen", type=int, default=5066, help="Unity → 추적기 제어 메시지 포트 (0 = 끔)")
    p.add_argument("--deck", default="", help="룬 후보 (예: S03,S06,S07,S08,S11,S13,H01,H02). 비우면 19종 전체")
    p.add_argument("--recognizer", choices=["local", "server"], default="local",
                   help="local = 이 프로그램에서 인식, server = 백엔드 /api/stage6/ai/recognize 호출")
    p.add_argument("--server", default="http://localhost:8080")
    p.add_argument("--model", default=DEFAULT_MODEL)
    p.add_argument("--aim", choices=["position", "angle"], default="position")
    p.add_argument("--swap-hands", action="store_true", help="왼손잡이 등: 왼손/오른손 역할 바꾸기")
    p.add_argument("--no-mirror", action="store_true", help="화면 좌우 반전 끄기 (보통은 켜 둔다)")
    p.add_argument("--no-landmarks", action="store_true", help="frame 메시지에서 21점 좌표 빼기")
    p.add_argument("--no-preview", action="store_true", help="미리보기 창 없이 실행")
    p.add_argument("--max-frames", type=int, default=0, help="이 프레임 수 후 종료 (시험용)")
    return p.parse_args(argv)


def main(argv=None):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    args = parse_args(argv)
    hg.Settings.aim_mode = args.aim
    candidates = [c.strip().upper() for c in args.deck.split(",") if c.strip()] or list(wow_runes.SPELLS)
    bad = [c for c in candidates if c not in wow_runes.SPELLS]
    if bad:
        sys.exit(f"알 수 없는 룬 코드: {bad} (S01~S13, H01~H06)")

    import mediapipe as mp
    landmarker = create_landmarker(load_model(args.model))
    cap = open_capture(args)
    if not cap.isOpened():
        sys.exit(f"카메라 {args.camera} 을(를) 열 수 없습니다. 다른 프로그램이 쓰고 있는지 확인하거나 --camera 1 로 바꿔 보세요.")

    link = Link(args.host, args.port, args.listen)
    st = {"right": hg.RightHand(), "left": hg.LeftHand(), "lm_right": None, "lm_left": None, "link": link,
          "fps": 0.0, "log": deque(maxlen=20), "last_rune": None, "candidates": candidates,
          "show_runes": True, "swap": args.swap_hands}
    print(f"[MediaPipe] 손 추적 시작 — UDP {args.host}:{args.port} 로 송출, 제어 포트 {args.listen}")
    print(f"[MediaPipe] 룬 후보 {len(candidates)}개: {','.join(candidates)}")

    t_start = time.monotonic()
    # 영상 파일은 처리 속도와 상관없이 영상 속 시간으로 판정한다 (휘두르기 속도 등이 PC 성능에 따라 달라지지 않게)
    video_fps = (cap.get(cv2.CAP_PROP_FPS) or 30.0) if args.video else 0.0
    last_ts = -1
    frames = 0
    fps_t, fps_n = time.monotonic(), 0
    window = "War of Wizards - MediaPipe Hand Tracker"
    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                if args.video:
                    break
                time.sleep(0.01)
                continue
            if not args.no_mirror:
                frame = cv2.flip(frame, 1)
            h, w = frame.shape[:2]
            aspect = w / h
            now = t_start + frames / video_fps if args.video else time.monotonic()
            st["now"] = now
            ts = int((now - t_start) * 1000)
            if ts <= last_ts:
                ts = last_ts + 1
            last_ts = ts

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)
            lm_r, lm_l = split_hands(result, st["swap"])
            st["lm_right"], st["lm_left"] = lm_r, lm_l

            # Unity → 추적기: {"type":"candidates","spells":["S03",...]}  (쿨타임 끝난 슬롯 목록)
            for m in link.poll():
                if m.get("type") == "candidates":
                    cs = [c for c in m.get("spells", []) if c in wow_runes.SPELLS]
                    if cs != st["candidates"]:
                        print(f"[MediaPipe] Unity 가 룬 후보를 바꿈 ({len(cs)}개): {','.join(cs) or '없음'}")
                    st["candidates"] = cs
                elif m.get("type") == "hello":
                    link.send({"type": "hello", "from": "tracker", "candidates": st["candidates"]})

            events = [("right", e) for e in st["right"].update(lm_r, now, aspect)]
            events += [("left", e) for e in st["left"].update(lm_l, now, aspect)]
            for hand, ev in events:
                if ev["name"] == "pinch_end":
                    if ev["tooShort"]:
                        ev["rune"] = {"recognized": False, "reason": "too_short", "spellId": None, "score": 0.0}
                    else:
                        ev["rune"] = recognize_rune(ev["points"], st["candidates"], args)
                    st["last_rune"] = {"t": now, "points": ev["points"], "rune": ev["rune"]}
                msg = {"type": "event", "t": round(now - t_start, 3), "hand": hand}
                msg.update(ev)
                if ev["name"] == "pinch_end":
                    msg["points"] = points_json(ev["points"])
                    msg["rune"] = rune_json(ev["rune"])
                link.send(msg)
                st["log"].append((now, describe(hand, ev)))
                print(f"[{now - t_start:7.2f}s] {describe(hand, ev)}")

            frame_msg = {"type": "frame", "t": round(now - t_start, 3), "fps": round(st["fps"], 1),
                         "right": hand_packet(st["right"], lm_r, aspect),
                         "left": hand_packet(st["left"], lm_l, aspect)}
            if args.no_landmarks:
                frame_msg["right"].pop("landmarks", None)
                frame_msg["left"].pop("landmarks", None)
            link.send(frame_msg)

            frames += 1
            fps_n += 1
            wall = time.monotonic()
            if wall - fps_t >= 1.0:
                st["fps"] = fps_n / (wall - fps_t)
                fps_t, fps_n = wall, 0

            if not args.no_preview:
                cv2.imshow(window, render(frame, Overlay.shared, st, args))
                key = cv2.waitKey(1) & 0xFF
                if key in (ord("q"), 27):
                    break
                if key == ord("h"):
                    st["swap"] = not st["swap"]
                elif key == ord("a"):
                    args.aim = "angle" if args.aim == "position" else "position"
                    hg.Settings.aim_mode = args.aim
                elif key == ord("r"):
                    st["show_runes"] = not st["show_runes"]
                if cv2.getWindowProperty(window, cv2.WND_PROP_VISIBLE) < 1:
                    break
            if args.max_frames and frames >= args.max_frames:
                break
    finally:
        cap.release()
        landmarker.close()
        link.close()
        cv2.destroyAllWindows()
    print(f"[MediaPipe] 종료 — 프레임 {frames}, 보낸 메시지 {link.sent}")
    return frames


def describe(hand, ev):
    who = "오른손" if hand == "right" else "왼손"
    name = ev["name"]
    s = f"{who} {EVENT_KO.get(name, name)}"
    if name == "pinch_end":
        r = ev.get("rune", {})
        s += f" → 룬 {r.get('spellId')} {r.get('name', '')} ({r.get('score', 0):.2f}) " + \
             ("인식 성공" if r.get("recognized") else f"실패: {r.get('reason')}")
    elif name == "point":
        s += f" ({COLUMN_KO[ev['column']]}, 깊이 {ev['depth']:.2f})"
    elif name == "move":
        s += " ← 왼쪽" if ev["direction"] < 0 else " → 오른쪽"
    elif name == "tracking":
        s += " 시작" if ev["tracked"] else " 끊김"
    return s


Overlay.shared = Overlay()

if __name__ == "__main__":
    main()
