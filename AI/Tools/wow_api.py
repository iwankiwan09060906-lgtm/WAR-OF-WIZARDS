"""
WAR OF WIZARDS - Python <-> Spring 백엔드 연결 도구
File: WAR-OF-WIZARDS/AI/Tools/wow_api.py

Python(학습 스크립트, 테스트 도구, 서버 시뮬레이터)이 Spring 서버 API 를 쓰기 위한 작은 클라이언트.

환경변수
  WOW_SERVER_URL  기본 http://localhost:8080
  WOW_SERVER_KEY  기본 dev-server-key-change-me  (application.yml 의 wow.server-api-key 와 같아야 함)

명령줄 사용 예
  python wow_api.py health
  python wow_api.py login demo@wow.dev demo1234
  python wow_api.py ranking
  python wow_api.py stats                         # 스킬별 제스처 샘플 수
  python wow_api.py seed-gestures --per-class 20  # 합성 룬 샘플 업로드 (파이프라인 테스트용)
  python wow_api.py upload-model ../Models/gesture_model.onnx
  python wow_api.py report-match 21 22 --winner 21 --mode ranked   # 전용 서버처럼 결과 보고
"""

import argparse
import json
import math
import os
import random
import sys
import uuid

import requests

DEFAULT_URL = os.environ.get("WOW_SERVER_URL", "http://localhost:8080")
DEFAULT_SERVER_KEY = os.environ.get("WOW_SERVER_KEY", "dev-server-key-change-me")


class WowApiError(Exception):
    def __init__(self, status, message):
        super().__init__(f"[{status}] {message}")
        self.status = status
        self.message = message


class WowApi:
    """로그인 토큰(사용자 API) 과 서버 키(게임 서버/학습 API) 를 모두 다루는 클라이언트."""

    def __init__(self, base_url=DEFAULT_URL, server_key=DEFAULT_SERVER_KEY, timeout=15):
        self.base_url = base_url.rstrip("/")
        self.server_key = server_key
        self.timeout = timeout
        self.token = None
        self.player = None

    # ── 공통 ──────────────────────────────────────────────────────────
    def _request(self, method, path, *, server=False, **kwargs):
        headers = kwargs.pop("headers", {})
        if server:
            headers["X-Server-Key"] = self.server_key
        elif self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        headers.setdefault("X-Client", "python")
        res = requests.request(method, self.base_url + path, headers=headers, timeout=self.timeout, **kwargs)
        if not res.ok:
            try:
                message = res.json().get("message", res.text)
            except ValueError:
                message = res.text
            raise WowApiError(res.status_code, message)
        if not res.content:
            return None
        if "application/json" in res.headers.get("Content-Type", ""):
            return res.json()
        return res.content

    # ── 공개 / 로그인 사용자 ──────────────────────────────────────────
    def health(self):
        return self._request("GET", "/api/health")

    def register(self, email, password, nickname):
        data = self._request("POST", "/api/auth/register",
                             json={"email": email, "password": password, "nickname": nickname})
        self._set_session(data)
        return data

    def login(self, email, password):
        data = self._request("POST", "/api/auth/login", json={"email": email, "password": password})
        self._set_session(data)
        return data

    def _set_session(self, data):
        self.token = data["accessToken"]
        self.player = data

    def me(self):
        return self._request("GET", "/api/players/me")

    def ranking(self, limit=20):
        return self._request("GET", f"/api/players/ranking?limit={limit}")

    def skills(self):
        return self._request("GET", "/api/skills")

    def gesture_stats(self):
        return self._request("GET", "/api/gestures/stats")

    def model_info(self):
        return self._request("GET", "/api/gestures/model/info")

    def download_model(self, out_path):
        content = self._request("GET", "/api/gestures/model")
        with open(out_path, "wb") as f:
            f.write(content)
        return out_path

    def upload_gesture_as_user(self, label, points_xyz, duration_ms=0, hand="right"):
        """로그인한 사용자로 샘플 업로드 (Unity 와 같은 경로)."""
        return self._request("POST", "/api/gestures/samples",
                             json={"label": label, "points": flatten(points_xyz),
                                   "durationMs": duration_ms, "hand": hand})

    # ── 서버 키 (학습 파이프라인 / 전용 서버 흉내) ────────────────────
    def export_gestures(self, label=None, page_size=500):
        """모든 샘플을 페이지 단위로 내려받는다. 각 항목: {id, label, points[flat], durationMs, ...}"""
        samples, after_id = [], 0
        while True:
            params = {"afterId": after_id, "limit": page_size}
            if label:
                params["label"] = label
            page = self._request("GET", "/api/server/gestures/samples", server=True, params=params)
            if not page:
                return samples
            samples.extend(page)
            after_id = page[-1]["id"]
            if len(page) < page_size:
                return samples

    def import_gesture(self, label, points_xyz, duration_ms=0, hand="right"):
        return self._request("POST", "/api/server/gestures/samples", server=True,
                             json={"label": label, "points": flatten(points_xyz),
                                   "durationMs": duration_ms, "hand": hand})

    def upload_model(self, onnx_path):
        # 파일을 먼저 읽고 닫는다 — 같은 PC 에서 서버가 이 파일을 덮어쓸 때 Windows 파일 잠금 충돌 방지
        with open(onnx_path, "rb") as f:
            data = f.read()
        files = {"file": (os.path.basename(onnx_path), data, "application/octet-stream")}
        return self._request("POST", "/api/server/gestures/model", server=True, files=files)

    def report_match(self, player_ids, winner_id=None, mode="ranked", duration_sec=300, room_code=None):
        body = {
            "matchToken": f"py-{uuid.uuid4()}",
            "mode": mode,
            "winnerPlayerId": winner_id,
            "durationSec": duration_sec,
            "roomCode": room_code,
            "players": [{"playerId": pid, "casts": random.randint(10, 40),
                         "damageDealt": random.randint(100, 600),
                         "damageTaken": random.randint(100, 600)} for pid in player_ids],
        }
        return self._request("POST", "/api/server/matches", server=True, json=body)

    def server_player(self, player_id):
        return self._request("GET", f"/api/server/players/{player_id}", server=True)


def flatten(points_xyz):
    """[[x,y,z], ...] 또는 numpy (N,3) → [x0,y0,z0,x1,...]"""
    flat = []
    for p in points_xyz:
        flat.extend([float(p[0]), float(p[1]), float(p[2])])
    return flat


def unflatten(flat):
    return [[flat[i], flat[i + 1], flat[i + 2]] for i in range(0, len(flat) - 2, 3)]


def synthetic_rune(class_index, num_points=80, noise=0.03):
    """클래스마다 모양이 다른 합성 룬 궤적 (실제 데이터가 모이기 전 파이프라인 테스트용)."""
    pts = []
    turns = 1 + class_index % 4            # 감기는 횟수
    height = 0.1 * (class_index // 4)      # 앞으로 나오는 정도
    squash = 0.4 + 0.15 * (class_index % 5)
    for i in range(num_points):
        t = i / (num_points - 1)
        angle = 2 * math.pi * turns * t + class_index * 0.3
        x = math.cos(angle) * (0.5 + 0.5 * t) + random.gauss(0, noise)
        y = math.sin(angle) * squash + random.gauss(0, noise)
        z = height * t + random.gauss(0, noise)
        pts.append([x, y, z])
    return pts


def main():
    parser = argparse.ArgumentParser(description="War of Wizards 서버 도구")
    parser.add_argument("--url", default=DEFAULT_URL)
    parser.add_argument("--key", default=DEFAULT_SERVER_KEY, help="X-Server-Key")
    sub = parser.add_subparsers(dest="cmd", required=True)
    sub.add_parser("health")
    p = sub.add_parser("login"); p.add_argument("email"); p.add_argument("password")
    sub.add_parser("ranking")
    sub.add_parser("stats")
    sub.add_parser("model-info")
    p = sub.add_parser("seed-gestures"); p.add_argument("--per-class", type=int, default=10)
    p = sub.add_parser("upload-model"); p.add_argument("file")
    p = sub.add_parser("download-model"); p.add_argument("out")
    p = sub.add_parser("report-match")
    p.add_argument("players", type=int, nargs="+")
    p.add_argument("--winner", type=int)
    p.add_argument("--mode", default="ranked", choices=["ranked", "friendly", "bot"])
    args = parser.parse_args()

    api = WowApi(args.url, args.key)
    try:
        if args.cmd == "health":
            result = api.health()
        elif args.cmd == "login":
            result = api.login(args.email, args.password)
        elif args.cmd == "ranking":
            result = api.ranking()
        elif args.cmd == "stats":
            result = api.gesture_stats()
        elif args.cmd == "model-info":
            result = api.model_info()
        elif args.cmd == "seed-gestures":
            classes = api.model_info()["classes"]
            for idx, label in enumerate(classes):
                for _ in range(args.per_class):
                    api.import_gesture(label, synthetic_rune(idx), duration_ms=random.randint(600, 1500))
            result = api.gesture_stats()
        elif args.cmd == "upload-model":
            result = api.upload_model(args.file)
        elif args.cmd == "download-model":
            result = {"saved": api.download_model(args.out)}
        elif args.cmd == "report-match":
            result = api.report_match(args.players, args.winner, args.mode)
        else:
            parser.error("알 수 없는 명령")
            return
    except WowApiError as e:
        print(f"오류 {e}", file=sys.stderr)
        sys.exit(1)
    except requests.ConnectionError:
        print(f"서버에 연결할 수 없습니다: {args.url} (Spring 서버가 실행 중인지 확인)", file=sys.stderr)
        sys.exit(1)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
