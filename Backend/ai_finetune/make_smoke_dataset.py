# -*- coding: utf-8 -*-
"""
미세조정 설치 확인용 가짜 데이터셋 — 실제 학습에는 쓰지 말 것 (진짜 판 데이터는 관리자 콘솔 → AI 학습 → 데이터셋 만들기)

임시 DB · 임시 학습 폴더로 게임 서버를 따로 띄워서 (실제 wow_database.sqlite · ai_data 는 건드리지 않음)
  ① 세 레인에 적 유닛이 몰린 가짜 전투 화면을 그리고
  ② /api/ai/experience 로 "그 장면 + 행동"을 기록, 판 결과(이김 · 짐)를 붙인 뒤
  ③ /api/ai/dataset 으로 내보낸 데이터셋을 Backend/ai_finetune/smoke_dataset/ 에 복사한다.
이긴 행동 규칙: 적이 3기 이상 몰린 레인에 화살 세례(S04), 아니면 가장 밀리는 레인에 도끼 전사(S11).

    python make_smoke_dataset.py [판 수, 기본 80]      (Pillow 필요)
"""
import base64
import io
import json
import os
import random
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
BACKEND = os.path.dirname(HERE)
OUT = os.path.join(HERE, "smoke_dataset")
DECK = ["S03", "S04", "S06", "S08", "S11", "S13", "H01", "H02"]
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


PORT = free_port()
BASE = f"http://127.0.0.1:{PORT}"


def call(method, path, body=None, token=None):
    h = {"Content-Type": "application/json"}
    if token:
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + path, data=json.dumps(body).encode() if body is not None else None, headers=h, method=method)
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read() or b"null")


def draw_scene(lanes, hp):
    """640x360 가짜 전투 화면: 세 레인, 위쪽 빨간 점 = 적 유닛, 아래쪽 파란 점 = 내 유닛, 아래 HP 막대"""
    img = Image.new("RGB", (640, 360), (14, 22, 32))
    d = ImageDraw.Draw(img)
    for i, (allies, enemies) in enumerate(lanes):
        x0 = 30 + i * 200
        d.rectangle([x0, 20, x0 + 180, 300], fill=(28, 40, 56), outline=(60, 80, 100))
        for k in range(enemies):
            cx, cy = x0 + 30 + (k % 4) * 40, 50 + (k // 4) * 36
            d.ellipse([cx - 12, cy - 12, cx + 12, cy + 12], fill=(230, 70, 90))
        for k in range(allies):
            cx, cy = x0 + 30 + (k % 4) * 40, 270 - (k // 4) * 36
            d.ellipse([cx - 12, cy - 12, cx + 12, cy + 12], fill=(60, 200, 255))
    d.rectangle([30, 318, 610, 338], outline=(80, 100, 120))
    d.rectangle([31, 319, 31 + int(578 * hp), 337], fill=(60, 200, 255))
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=85)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def winning_action(lanes):
    enemies = [e for _, e in lanes]
    busiest = max(range(3), key=lambda i: enemies[i])
    if enemies[busiest] >= 3:
        return {"spellId": "S04", "slot": DECK.index("S04"), "column": busiest, "depth": 0.3}
    weakest = min(range(3), key=lambda i: lanes[i][0] - lanes[i][1])
    return {"spellId": "S11", "slot": DECK.index("S11"), "column": weakest, "depth": 0.3}


def main():
    games = int(sys.argv[1]) if len(sys.argv) > 1 else 80
    tmp = tempfile.mkdtemp(prefix="wow_smoke_")
    env = dict(os.environ, WOW_DB_FILE=os.path.join(tmp, "smoke.sqlite"), WOW_AI_DATA=os.path.join(tmp, "ai_data"),
               PYTHONIOENCODING="utf-8")
    for k in ("ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"):
        env.pop(k, None)
    proc = subprocess.Popen([sys.executable, os.path.join(BACKEND, "wow_server.py"), str(PORT)], env=env,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            try:
                call("GET", "/api/health")
                break
            except (urllib.error.URLError, OSError):
                time.sleep(0.2)
        tok = call("POST", "/api/auth/login", {"email": "1조", "password": "1111"})["accessToken"]
        admin = call("POST", "/api/auth/login", {"email": "admin@wow.dev", "password": "admin1234"})["accessToken"]
        rng = random.Random(7)
        for g in range(games):
            lanes = [(rng.randint(0, 4), rng.randint(0, 6)) for _ in range(3)]
            hp = rng.uniform(0.4, 1.0)
            good = winning_action(lanes)
            win = rng.random() < 0.6
            act = good if win else {"spellId": rng.choice(["S03", "S06", "S13"]), "slot": 0, "column": rng.randint(0, 2), "depth": 0.5}
            state = {"deck": DECK, "ready": [True] * 8, "selfHp": round(1000 * hp), "selfColumn": 1, "time": rng.randint(20, 400),
                     "lanes": [{"allies": a, "enemies": e} for a, e in lanes]}
            game = f"smoke{g}"
            call("POST", "/api/ai/experience", {"gameId": game, "source": "smoke", "state": state, "action": act,
                                                 "image": draw_scene(lanes, hp)}, tok)
            call("POST", "/api/ai/experience", {"gameId": game, "outcome": "WIN" if win else "LOSE"}, tok)
        res = call("POST", "/api/ai/dataset", {}, admin)
        # 이 스크립트가 만드는 파일만 바꾼다 (폴더째 지우지 않음)
        shutil.rmtree(os.path.join(OUT, "frames"), ignore_errors=True)
        shutil.copytree(res["dir"], OUT, dirs_exist_ok=True)
        sys.path.insert(0, BACKEND)
        import wow_learn
        wow_learn.write_finetune_configs(OUT, "smoke-lora")       # 학습 결과 → Backend/ai_finetune/saves/smoke-lora
        print(f"판 {games}개 → 이긴 장면 {res['lines']}줄 (그림 {res['withImage']}장) → {OUT}")
    finally:
        proc.terminate()
        proc.wait(timeout=10)
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    main()
