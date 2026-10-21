# -*- coding: utf-8 -*-
"""
Unity 없이 MediaPipe 추적기 출력을 확인하는 "가짜 Unity" 수신기.

    python udp_monitor.py                         # 5065 포트로 받은 이벤트 출력
    python udp_monitor.py --deck S03,S06,S07      # 추적기에 룬 후보 목록도 보낸다 (Unity 가 할 일)

Unity 쪽 MediaPipeHandInput 이 하는 일과 같다: 이벤트 → IPlayerInput 이벤트 이름으로 바꿔 출력.
"""

import argparse
import json
import socket
import sys
import time

CONTRACT = {
    "pinch_start": "OnPinchStarted()",
    "pinch_end": "OnPinchEnded(Vector2[])",
    "fist": "OnFistDetected()",
    "point": "OnPointDetected()",
    "palm_open": "OnPalmOpenDetected()",
    "move": "OnMoveGesture(sbyte)",
    "tracking": "IsRight/LeftHandTracked",
    "pinch_cancel": "(그리기 취소)",
}


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser()
    p.add_argument("--port", type=int, default=5065)
    p.add_argument("--tracker", default="127.0.0.1:5066", help="추적기 제어 포트 (후보 룬 보내기)")
    p.add_argument("--deck", default="")
    p.add_argument("--seconds", type=float, default=0, help="이 시간 후 종료 (0 = 계속)")
    args = p.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("0.0.0.0", args.port))
    sock.settimeout(0.5)
    th, tp = args.tracker.split(":")
    tracker = (th, int(tp))
    deck = [c.strip().upper() for c in args.deck.split(",") if c.strip()]
    print(f"[가짜 Unity] UDP {args.port} 수신 대기 중... (Ctrl+C 종료)")

    start = time.monotonic()
    last_hello = 0.0
    frames = 0
    last_status = 0.0
    counts = {}
    while not args.seconds or time.monotonic() - start < args.seconds:
        now = time.monotonic()
        if deck and now - last_hello > 2.0:
            sock.sendto(json.dumps({"type": "candidates", "spells": deck}).encode(), tracker)
            last_hello = now
        try:
            data, _ = sock.recvfrom(65535)
        except socket.timeout:
            continue
        except ConnectionResetError:
            continue
        msg = json.loads(data.decode("utf-8"))
        if msg.get("type") == "frame":
            frames += 1
            if now - last_status > 1.0:
                r, l = msg["right"], msg["left"]
                print(f"  · frame #{msg['seq']}  fps {msg.get('fps')}  오른손 {r.get('pose')}  왼손 {l.get('pose')}")
                last_status = now
        elif msg.get("type") == "event":
            name = msg["name"]
            counts[name] = counts.get(name, 0) + 1
            extra = ""
            if name == "pinch_end":
                rune = msg.get("rune", {})
                extra = f" 점 {len(msg['points'])}개 → 룬 {rune.get('spellId')} {rune.get('name', '')} " \
                        f"점수 {rune.get('score')} {'성공' if rune.get('recognized') else '실패(' + str(rune.get('reason')) + ')'}"
            elif name == "point":
                extra = f" 열 {msg['column']} 깊이 {msg['depth']}"
            elif name == "move":
                extra = f" 방향 {msg['direction']:+d}"
            elif name == "tracking":
                extra = f" {'시작' if msg['tracked'] else '끊김'}"
            print(f"[{msg['t']:7.2f}s] {msg['hand']:5s} {name:12s} → {CONTRACT.get(name, '?')}{extra}")
    print(f"[가짜 Unity] 종료 — frame {frames}개, 이벤트 {counts}")


if __name__ == "__main__":
    main()
