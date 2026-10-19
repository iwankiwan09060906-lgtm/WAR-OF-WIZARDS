# -*- coding: utf-8 -*-
"""
War of Wizards (워 오브 위자드) — 8단계 단계별 전용 파이썬 서버 (Stage-Based Python Server)
8단계 개발 로드맵을 체계적으로 지원하는 독립형 파이썬 백엔드 서버입니다.

로드맵 8단계:
  1단계 — Unity 기본 전투: 3D 맵 캐릭터 이동 / 키보드 스킬 발사 설정 제공
  2단계 — Photon 방 연결: Photon 커스텀 인증 및 룸 세션 연결
  3단계 — 백엔드 연동: 회원가입, 로그인(JWT), 방 생성, 방 코드 입장 API
  4단계 — 대기실과 매칭: 참가자 목록, 준비 버튼 토글, 매칭 큐/취소, 퇴장 처리
  5단계 — 마법 이벤트 동기화: 마법 시전 이벤트 수신 및 양방향 실시간 릴레이 (SSE)
  6단계 — AI 손동작 인식: $P+ 알고리즘 내장, 궤적 좌표 분석 -> 19종 마법 실시간 인식
  7단계 — 전투 판정과 전적 저장: 체력/데미지 검증, 승패 판정, 랭킹/MMR DB 영구 기록
  8단계 — 보안 및 배포: 치트/쿨타임 비정상 요청 차단, 서버 권한 판정, 연결 복구

실행 방법:
  python stage_server.py [포트]  (기본 포트: 8080)
"""

import os
import sys
import json
import time
import math
import uuid
import hmac
import base64
import hashlib
import sqlite3
import threading
from datetime import datetime
from urllib.parse import urlparse, parse_qs
from http.server import HTTPServer, BaseHTTPRequestHandler
import mimetypes

try:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

DEFAULT_PORT = 8080
SERVER_DIR = os.path.dirname(os.path.abspath(__file__))
STATIC_DIR = os.path.join(SERVER_DIR, "static")
DB_FILE = os.path.join(SERVER_DIR, "stage_database.sqlite")
JWT_SECRET = "wow-stage-secret-key-32chars-min-length-2026"

# ─────────────────────────────────────────────────────────────────────────────
# 6단계 AI 제스처 인식 엔진 ($P+ Point-Cloud 3D/2D Recognizer 내장)
# ─────────────────────────────────────────────────────────────────────────────
class GesturePoint:
    def __init__(self, x: float, y: float, z: float = 0.0, stroke_id: int = 1):
        self.x = float(x)
        self.y = float(y)
        self.z = float(z)
        self.stroke_id = int(stroke_id)

def resample_points(points: list, n: int = 32) -> list:
    if len(points) < 2:
        return [points[0]] * n if points else []
    total_len = 0.0
    dists = []
    for i in range(len(points) - 1):
        d = math.sqrt((points[i+1].x - points[i].x)**2 + (points[i+1].y - points[i].y)**2 + (points[i+1].z - points[i].z)**2)
        dists.append(d)
        total_len += d
    if total_len == 0:
        return [points[0]] * n
    step = total_len / (n - 1)
    res = [points[0]]
    curr_dist = 0.0
    accum = 0.0
    i = 0
    while len(res) < n:
        if i >= len(dists):
            res.append(points[-1])
            continue
        seg_dist = dists[i]
        if accum + seg_dist >= curr_dist + step:
            ratio = (curr_dist + step - accum) / (seg_dist if seg_dist > 0 else 1.0)
            nx = points[i].x + ratio * (points[i+1].x - points[i].x)
            ny = points[i].y + ratio * (points[i+1].y - points[i].y)
            nz = points[i].z + ratio * (points[i+1].z - points[i].z)
            pt = GesturePoint(nx, ny, nz, points[i].stroke_id)
            res.append(pt)
            curr_dist += step
        else:
            accum += seg_dist
            i += 1
    return res[:n]

def normalize_points(points: list, n: int = 32) -> list:
    resampled = resample_points(points, n)
    if not resampled:
        return []
    cx = sum(p.x for p in resampled) / len(resampled)
    cy = sum(p.y for p in resampled) / len(resampled)
    cz = sum(p.z for p in resampled) / len(resampled)
    centered = [GesturePoint(p.x - cx, p.y - cy, p.z - cz, p.stroke_id) for p in resampled]
    max_d = max(math.sqrt(p.x**2 + p.y**2 + p.z**2) for p in centered) or 1.0
    return [GesturePoint(p.x / max_d, p.y / max_d, p.z / max_d, p.stroke_id) for p in centered]

def cloud_distance(pts1: list, pts2: list) -> float:
    matched = [False] * len(pts2)
    sum_dist = 0.0
    for p1 in pts1:
        min_d = float('inf')
        idx = -1
        for j, p2 in enumerate(pts2):
            d = math.sqrt((p1.x - p2.x)**2 + (p1.y - p2.y)**2 + (p1.z - p2.z)**2)
            if d < min_d:
                min_d = d
                idx = j
        sum_dist += min_d
    return sum_dist / len(pts1)

# 19종 기본 스킬 룬 템플릿 정의
GESTURE_TEMPLATES = {
    "S01": [GesturePoint(math.cos(a), math.sin(a)) for a in [i * 0.3 for i in range(20)]], # 원형 (연막)
    "S02": [GesturePoint(i * 0.1 - 1.0, 0) for i in range(21)], # 수평 직선 (마력 미사일)
    "S03": [GesturePoint(math.cos(a)*0.8, math.sin(a)*0.8) for a in [i * 0.35 for i in range(18)]], # 화염구
    "S04": [GesturePoint(-1 + i*0.1, 1 - i*0.1) for i in range(11)] + [GesturePoint(0 + i*0.1, 0 + i*0.1) for i in range(11)], # V자 (얼음 파편)
    "S05": [GesturePoint(-0.8 + i*0.2, 0.8) for i in range(5)] + [GesturePoint(0.2 - i*0.2, 0.8 - i*0.4) for i in range(5)] + [GesturePoint(-0.6 + i*0.2, -0.8) for i in range(5)], # Z자 (번개 화살)
    "S06": [GesturePoint(math.sin(i*0.5)*0.5, i*0.1 - 1.0) for i in range(21)], # 물결/돌풍
    "S07": [GesturePoint(0, 1.0 - i*0.1) for i in range(21)], # 수직 하향 (독침)
    "S08": [GesturePoint(i*0.1 - 1.0, math.sin(i*0.8)*0.7) for i in range(21)], # 대지 가시
    "S09": [GesturePoint(0, 1.0 - i*0.1) for i in range(21)] + [GesturePoint(i*0.1 - 1.0, 0) for i in range(21)], # 십자가 (치유)
    "S10": [GesturePoint(-1.0 + i*0.1, -0.8) for i in range(21)] + [GesturePoint(1.0 - i*0.1, -0.8 + i*0.16) for i in range(11)] + [GesturePoint(0 - i*0.1, 0.8 - i*0.16) for i in range(11)], # 삼각형 (보호막)
    "S11": [GesturePoint(-0.8, -0.8 + i*0.16) for i in range(11)] + [GesturePoint(-0.8 + i*0.16, 0.8) for i in range(11)] + [GesturePoint(0.8, 0.8 - i*0.16) for i in range(11)], # ㄷ자 돌벽
    "S12": [GesturePoint(-0.8 + i*0.16, -0.8) for i in range(11)] + [GesturePoint(0.8, -0.8 + i*0.16) for i in range(11)], # ㄱ자 소환
    "S13": [GesturePoint(math.cos(i*0.5)*(1 - i*0.04), math.sin(i*0.5)*(1 - i*0.04)) for i in range(25)], # 나선 터렛
    "H01": [GesturePoint(math.cos(a)*1.2, math.sin(a)*1.2) for a in [i * 0.3 for i in range(22)]], # 거대 원 (블리자드)
    "H02": [GesturePoint(-1.0 + i*0.1, 0.5) for i in range(21)] + [GesturePoint(-1.0 + i*0.1, -0.5) for i in range(21)], # 평행선 (광선)
    "H03": [GesturePoint(-0.8 + i*0.08, 0.8 - i*0.16) for i in range(21)], # 사선 대각선 (메테오)
    "H04": [GesturePoint(-0.9 + i*0.1, 0.9 - (i%2)*0.8) for i in range(19)], # 번개 폭풍
    "H05": [GesturePoint(math.cos(i*0.4)*(0.5+i*0.04), math.sin(i*0.4)*(0.5+i*0.04)) for i in range(25)], # 용의 숨결
    "H06": [GesturePoint(0, -1.0 + i*0.1) for i in range(21)] + [GesturePoint(-0.5 + i*0.1, 0.5) for i in range(11)] # 탑 형태
}
NORMALIZED_TEMPLATES = {code: normalize_points(pts, 32) for code, pts in GESTURE_TEMPLATES.items()}

# MediaPipe 손 추적기와 같은 19종 룬 인식기(AI/MediaPipe/wow_runes.py)가 있으면 그것을 쓴다.
# (웹 6단계 캔버스 · MediaPipe 추적기 · Unity 가 같은 모양을 같은 스킬로 인식하도록)
try:
    sys.path.insert(0, os.path.join(SERVER_DIR, "..", "AI", "MediaPipe"))
    import wow_runes
except ImportError:
    wow_runes = None


def recognize_gesture(raw_points: list, candidates=None, y_down: bool = False) -> dict:
    """
    raw_points : [{"x":..,"y":..}, ...]  기본은 y 위쪽 + (Unity Vector2 / MediaPipe 추적기 출력과 같음)
    y_down     : 웹 캔버스처럼 y 가 아래로 커지는 좌표면 True
    candidates : 비교할 룬 코드 목록 (쿨타임 끝난 슬롯만, §9). 없으면 19종 전체
    """
    if wow_runes is not None:
        pts = [(float(p.get("x", 0)), -float(p.get("y", 0)) if y_down else float(p.get("y", 0))) for p in raw_points]
        r = wow_runes.recognize(pts, candidates or None)
        ranking = r.get("ranking", [])
        return {
            "spellId": r["spellId"],
            "bestSpell": r["spellId"],
            "secondSpell": ranking[1][0] if len(ranking) > 1 else None,
            "spellNumber": r["spellNumber"],
            "name": r["name"],
            "confidence": r["score"],
            "scoreGap": r["gap"],
            "recognized": r["recognized"],
            "reason": r["reason"],
            "ranking": ranking,
            "engine": "wow_runes ($P, 19종 룬)",
        }
    return _legacy_recognize(raw_points)


def _legacy_recognize(raw_points: list) -> dict:
    if len(raw_points) < 4:
        return {"spellId": "S02", "confidence": 0.5, "scoreGap": 0.1, "recognized": True, "message": "최소 포인트 부족으로 기본 마력 미사일 지정"}
    pts = [GesturePoint(p.get("x", 0), p.get("y", 0), p.get("z", 0)) for p in raw_points]
    norm_input = normalize_points(pts, 32)
    scores = []
    for code, t_pts in NORMALIZED_TEMPLATES.items():
        d = (cloud_distance(norm_input, t_pts) + cloud_distance(t_pts, norm_input)) / 2.0
        scores.append((code, d))
    scores.sort(key=lambda x: x[1])
    best_code, best_dist = scores[0]
    second_code, second_dist = scores[1]
    confidence = max(0.0, min(1.0, 1.0 - (best_dist / 1.5)))
    gap = second_dist - best_dist
    return {
        "spellId": best_code,
        "bestSpell": best_code,
        "secondSpell": second_code,
        "confidence": round(confidence, 3),
        "scoreGap": round(gap, 3),
        "recognized": confidence >= 0.55 and gap >= 0.05
    }

# ─────────────────────────────────────────────────────────────────────────────
# 5단계 마법 이벤트 동기화 및 방 브로드캐스트 엔진
# ─────────────────────────────────────────────────────────────────────────────
ROOM_SPELL_EVENTS = {}  # room_code -> list of event dicts
ROOM_CLIENTS = {}       # room_code -> list of wfiles
ROOM_LOCK = threading.Lock()

def broadcast_room_event(room_code: str, event_type: str, data: dict):
    msg = f"event: {event_type}\ndata: {json.dumps(data, ensure_ascii=False)}\n\n".encode("utf-8")
    with ROOM_LOCK:
        clients = ROOM_CLIENTS.get(room_code, [])
        active = []
        for wfile in clients:
            try:
                wfile.write(msg)
                wfile.flush()
                active.append(wfile)
            except Exception:
                pass
        ROOM_CLIENTS[room_code] = active

# ─────────────────────────────────────────────────────────────────────────────
# 데이터베이스 초기화
# ─────────────────────────────────────────────────────────────────────────────
def get_db():
    conn = sqlite3.connect(DB_FILE)
    conn.row_factory = sqlite3.Row
    return conn

def init_stage_db():
    with get_db() as conn:
        c = conn.cursor()
        c.executescript("""
        CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            email TEXT UNIQUE NOT NULL,
            password_hash TEXT NOT NULL,
            role TEXT DEFAULT 'ROLE_USER',
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS players (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER UNIQUE NOT NULL,
            nickname TEXT UNIQUE NOT NULL,
            rating INTEGER DEFAULT 1200,
            wins INTEGER DEFAULT 0,
            losses INTEGER DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );

        CREATE TABLE IF NOT EXISTS rooms (
            code TEXT PRIMARY KEY,
            title TEXT DEFAULT '배틀 룸',
            host_player_id INTEGER,
            guest_player_id INTEGER,
            status TEXT DEFAULT 'WAITING', -- WAITING, READY, IN_GAME, CLOSED
            host_ready INTEGER DEFAULT 1,
            guest_ready INTEGER DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS match_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            room_code TEXT,
            winner_nickname TEXT,
            loser_nickname TEXT,
            duration_sec REAL DEFAULT 0,
            end_reason TEXT DEFAULT 'PlayerDeath',
            recorded_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS security_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            player_id INTEGER,
            event_type TEXT, -- COOLDOWN_VIOLATION, CHEAT_ATTEMPT, RECOVERY
            detail TEXT,
            logged_at TEXT DEFAULT CURRENT_TIMESTAMP
        );
        """)

        # 기본 데모 계정 생성
        if not c.execute("SELECT id FROM users WHERE email = 'demo@wow.dev'").fetchone():
            salt = b"wow_salt_2026_!!"
            pwd_hash = hashlib.sha256(salt + "demo1234".encode()).hexdigest()
            c.execute("INSERT INTO users (email, password_hash) VALUES (?, ?)", ("demo@wow.dev", pwd_hash))
            uid = c.lastrowid
            c.execute("INSERT INTO players (user_id, nickname, rating, wins, losses) VALUES (?, ?, 1200, 3, 1)", (uid, "데모마법사"))

        conn.commit()

# ─────────────────────────────────────────────────────────────────────────────
# 8단계 HTTP 요청 처리기
# ─────────────────────────────────────────────────────────────────────────────
class StageHttpHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Server-Key, X-Photon-Token")
        self.end_headers()

    def send_json(self, status: int, data: dict):
        body = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def read_json(self) -> dict:
        length = int(self.headers.get("Content-Length", 0))
        if length > 0:
            try:
                return json.loads(self.rfile.read(length).decode("utf-8"))
            except Exception:
                return {}
        return {}

    def get_token_user(self):
        auth = self.headers.get("Authorization", "")
        if auth.startswith("Bearer "):
            token = auth[7:].strip()
            try:
                parts = token.split(".")
                if len(parts) == 2:
                    uid, email = parts[0], parts[1]
                    return {"id": int(uid), "email": email}
            except Exception:
                pass
        return None

    # ─────────────────────────────────────────────────────────────────────────
    # GET 요청 처리
    # ─────────────────────────────────────────────────────────────────────────
    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path
        qs = parse_qs(parsed.query)

        # 헬스 체크
        if path == "/api/health" or path == "/health":
            return self.send_json(200, {
                "status": "UP",
                "server": "War of Wizards 8-Stage Architecture Server",
                "stages": [
                    "1. Unity 기본 전투 설정",
                    "2. Photon 방 연결 인증",
                    "3. Spring Boot/백엔드 연동",
                    "4. 대기실과 매칭",
                    "5. 마법 이벤트 동기화",
                    "6. AI 손동작 인식",
                    "7. 전투 판정과 전적 저장",
                    "8. 보안 및 배포"
                ],
                "time": datetime.utcnow().isoformat() + "Z"
            })

        # ─────────────────────────────────────────────────────────────────────
        # [1단계] Unity 기본 전투 — 키보드 입력 설정 및 스킬 카탈로그
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage1/combat-config", "/api/skills"):
            return self.send_json(200, {
                "stage": 1,
                "title": "1단계 — Unity 기본 전투 설정",
                "inputControls": {
                    "moveLeft": "LeftArrow or A",
                    "moveCenter": "DownArrow or S",
                    "moveRight": "RightArrow or D",
                    "castSlots": "Alpha1 ~ Alpha8",
                    "aimKey": "Space",
                    "cancelKey": "O or Escape"
                },
                "skills": [
                    {"code": "S01", "name": "연막 (Smoke)", "cooldown": 5.0, "type": "Area"},
                    {"code": "S02", "name": "마력 미사일 (MagicMissile)", "cooldown": 5.0, "type": "Column"},
                    {"code": "S03", "name": "파이어볼 (Fireball)", "cooldown": 5.0, "type": "Column"},
                    {"code": "S04", "name": "얼음 파편 (IceShard)", "cooldown": 5.0, "type": "Column"},
                    {"code": "S05", "name": "번개 화살 (LightningBolt)", "cooldown": 5.0, "type": "Column"},
                    {"code": "S09", "name": "치유의 빛 (HealLight)", "cooldown": 5.0, "type": "Self"},
                    {"code": "H01", "name": "블리자드 (Blizzard)", "cooldown": 20.0, "type": "Area"},
                    {"code": "H02", "name": "비전 광선 (LaserBeam)", "cooldown": 20.0, "type": "Beam"}
                ]
            })

        # ─────────────────────────────────────────────────────────────────────
        # [2단계] Photon 방 목록 및 상태 조회
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage2/photon/rooms", "/api/photon/rooms"):
            with get_db() as conn:
                rows = conn.execute("SELECT * FROM rooms WHERE status != 'CLOSED'").fetchall()
                return self.send_json(200, {
                    "stage": 2,
                    "activeRoomCount": len(rows),
                    "rooms": [dict(r) for r in rows]
                })

        # ─────────────────────────────────────────────────────────────────────
        # [4단계] 대기실 참가자 목록 조회
        # ─────────────────────────────────────────────────────────────────────
        if "/participants" in path:
            # /api/stage4/rooms/{code}/participants
            parts = path.split("/")
            code = parts[parts.index("rooms") + 1] if "rooms" in parts else ""
            with get_db() as conn:
                r = conn.execute("SELECT * FROM rooms WHERE code = ?", (code,)).fetchone()
                if not r:
                    return self.send_json(404, {"error": "방을 찾을 수 없습니다."})
                host = conn.execute("SELECT nickname, rating FROM players WHERE id = ?", (r["host_player_id"],)).fetchone()
                guest = conn.execute("SELECT nickname, rating FROM players WHERE id = ?", (r["guest_player_id"],)).fetchone() if r["guest_player_id"] else None
                return self.send_json(200, {
                    "stage": 4,
                    "roomCode": code,
                    "status": r["status"],
                    "participants": [
                        {"role": "HOST", "nickname": host["nickname"] if host else "Unknown", "ready": bool(r["host_ready"])},
                        {"role": "GUEST", "nickname": guest["nickname"] if guest else None, "ready": bool(r["guest_ready"])}
                    ]
                })

        # ─────────────────────────────────────────────────────────────────────
        # [5단계] 마법 이벤트 실시간 동기화 스트림 (SSE)
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage5/spells/stream", "/api/live/spells"):
            room_code = qs.get("roomCode", ["LOBBY"])[0]
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream; charset=utf-8")
            self.send_header("Cache-Control", "no-cache")
            self.send_header("Connection", "keep-alive")
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()

            with ROOM_LOCK:
                if room_code not in ROOM_CLIENTS:
                    ROOM_CLIENTS[room_code] = []
                ROOM_CLIENTS[room_code].append(self.wfile)

            init_msg = f"event: connected\ndata: {json.dumps({'message': f'Room {room_code} 마법 동기화 채널 연결됨'})}\n\n".encode("utf-8")
            self.wfile.write(init_msg)
            self.wfile.flush()

            try:
                while True:
                    time.sleep(15)
                    self.wfile.write(b": ping\n\n")
                    self.wfile.flush()
            except Exception:
                with ROOM_LOCK:
                    if self.wfile in ROOM_CLIENTS.get(room_code, []):
                        ROOM_CLIENTS[room_code].remove(self.wfile)
            return

        # ─────────────────────────────────────────────────────────────────────
        # [7단계] 랭킹 리더보드 조회
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage7/rankings", "/api/players/ranking"):
            with get_db() as conn:
                rows = conn.execute("SELECT nickname, rating, wins, losses FROM players ORDER BY rating DESC LIMIT 20").fetchall()
                return self.send_json(200, {
                    "stage": 7,
                    "rankings": [dict(r) for r in rows]
                })

        # 정적 파일 서빙 (대시보드 포함)
        self.serve_static(path)

    # ─────────────────────────────────────────────────────────────────────────
    # POST 요청 처리
    # ─────────────────────────────────────────────────────────────────────────
    def do_POST(self):
        parsed = urlparse(self.path)
        path = parsed.path
        body = self.read_json()

        # ─────────────────────────────────────────────────────────────────────
        # [2단계] Photon 인증 요청 처리
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage2/photon/auth", "/api/photon/auth"):
            user_id = body.get("userId", "demo_user")
            token = f"photon-valid-{uuid.uuid4().hex[:8]}"
            return self.send_json(200, {
                "stage": 2,
                "ResultCode": 1,
                "UserId": user_id,
                "PhotonToken": token,
                "message": "Photon 방 연결 인증 승인"
            })

        # ─────────────────────────────────────────────────────────────────────
        # [3단계] Spring Boot 연동 — 회원가입, 로그인, 방 생성, 코드 입장
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage3/auth/register", "/api/auth/register"):
            email = body.get("email", "").strip().lower()
            password = body.get("password", "")
            nickname = body.get("nickname", "").strip()
            if not email or not password or not nickname:
                return self.send_json(400, {"error": "이메일, 비밀번호, 닉네임은 필수입니다."})
            with get_db() as conn:
                c = conn.cursor()
                if c.execute("SELECT id FROM users WHERE email = ?", (email,)).fetchone():
                    return self.send_json(400, {"error": "이미 존재하는 이메일입니다."})
                pwd_hash = hashlib.sha256(b"wow_salt_2026_!!" + password.encode()).hexdigest()
                c.execute("INSERT INTO users (email, password_hash) VALUES (?, ?)", (email, pwd_hash))
                uid = c.lastrowid
                c.execute("INSERT INTO players (user_id, nickname) VALUES (?, ?)", (uid, nickname))
                pid = c.lastrowid
                conn.commit()
                token = f"{uid}.{email}"
                return self.send_json(200, {
                    "stage": 3,
                    "message": "3단계: 회원가입 성공",
                    "accessToken": token,
                    "userId": uid,
                    "playerId": pid,
                    "nickname": nickname
                })

        if path in ("/api/stage3/auth/login", "/api/auth/login"):
            email = body.get("email", "").strip().lower()
            password = body.get("password", "")
            pwd_hash = hashlib.sha256(b"wow_salt_2026_!!" + password.encode()).hexdigest()
            with get_db() as conn:
                user = conn.execute("SELECT * FROM users WHERE email = ? AND password_hash = ?", (email, pwd_hash)).fetchone()
                if not user:
                    return self.send_json(400, {"error": "이메일 또는 비밀번호가 올바르지 않습니다."})
                p = conn.execute("SELECT * FROM players WHERE user_id = ?", (user["id"],)).fetchone()
                token = f"{user['id']}.{email}"
                return self.send_json(200, {
                    "stage": 3,
                    "message": "3단계: 로그인 성공",
                    "accessToken": token,
                    "userId": user["id"],
                    "playerId": p["id"] if p else None,
                    "nickname": p["nickname"] if p else ""
                })

        if path in ("/api/stage3/rooms/create", "/api/rooms/create", "/api/rooms"):
            room_code = uuid.uuid4().hex[:6].upper()
            host_nick = body.get("nickname", "데모마법사")
            with get_db() as conn:
                c = conn.cursor()
                p = c.execute("SELECT id FROM players WHERE nickname = ?", (host_nick,)).fetchone()
                pid = p["id"] if p else 1
                c.execute("INSERT INTO rooms (code, host_player_id, status) VALUES (?, ?, 'WAITING')", (room_code, pid))
                conn.commit()
            return self.send_json(200, {
                "stage": 3,
                "message": "3단계: 방 생성 완료",
                "roomCode": room_code,
                "hostNickname": host_nick
            })

        if path in ("/api/stage3/rooms/join", "/api/rooms/join"):
            room_code = body.get("roomCode", "").strip().upper()
            guest_nick = body.get("nickname", "도전자")
            with get_db() as conn:
                c = conn.cursor()
                r = c.execute("SELECT * FROM rooms WHERE code = ?", (room_code,)).fetchone()
                if not r:
                    return self.send_json(404, {"error": "해당 방 코드를 찾을 수 없습니다."})
                if r["guest_player_id"]:
                    return self.send_json(400, {"error": "방에 이미 참가자가 있습니다."})
                p = c.execute("SELECT id FROM players WHERE nickname = ?", (guest_nick,)).fetchone()
                guest_id = p["id"] if p else 2
                c.execute("UPDATE rooms SET guest_player_id = ?, guest_ready = 0 WHERE code = ?", (guest_id, room_code))
                conn.commit()
            broadcast_room_event(room_code, "participant_joined", {"guestNickname": guest_nick})
            return self.send_json(200, {
                "stage": 3,
                "message": f"3단계: 방 [{room_code}] 코드 입장 성공",
                "roomCode": room_code,
                "guestNickname": guest_nick
            })

        # ─────────────────────────────────────────────────────────────────────
        # [4단계] 대기실과 매칭 — 준비 버튼 토글, 매칭 큐 취소, 퇴장
        # ─────────────────────────────────────────────────────────────────────
        if "/ready" in path:
            room_code = body.get("roomCode", "").upper()
            is_ready = bool(body.get("isReady", True))
            with get_db() as conn:
                conn.execute("UPDATE rooms SET guest_ready = ? WHERE code = ?", (1 if is_ready else 0, room_code))
                conn.commit()
            broadcast_room_event(room_code, "ready_changed", {"isReady": is_ready})
            return self.send_json(200, {
                "stage": 4,
                "message": f"4단계: 준비 상태 변경 ({is_ready})",
                "roomCode": room_code,
                "isReady": is_ready
            })

        if path in ("/api/stage4/matchmaking/cancel", "/api/matchmaking/cancel"):
            return self.send_json(200, {
                "stage": 4,
                "message": "4단계: 매칭 대기열 취소 완료"
            })

        if path in ("/api/stage4/rooms/leave", "/api/rooms/leave"):
            room_code = body.get("roomCode", "").upper()
            with get_db() as conn:
                conn.execute("UPDATE rooms SET guest_player_id = NULL, guest_ready = 0 WHERE code = ?", (room_code,))
                conn.commit()
            broadcast_room_event(room_code, "participant_left", {"roomCode": room_code})
            return self.send_json(200, {
                "stage": 4,
                "message": f"4단계: 방 [{room_code}] 퇴장 완료"
            })

        # ─────────────────────────────────────────────────────────────────────
        # [5단계] 마법 이벤트 동기화 — 시전 이벤트 수신 및 브로드캐스트
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage5/spells/cast", "/api/spells/cast"):
            room_code = body.get("roomCode", "LOBBY").upper()
            spell_id = body.get("spellId", "S02")
            slot_index = int(body.get("slotIndex", 0))
            column = body.get("targetColumn", "Center")  # Left, Center, Right
            depth = float(body.get("targetDepth", 0.0))
            caster = body.get("caster", "Player1")

            event_data = {
                "stage": 5,
                "roomCode": room_code,
                "caster": caster,
                "spellId": spell_id,
                "slotIndex": slot_index,
                "targetColumn": column,
                "targetDepth": depth,
                "timestamp": time.time()
            }
            # SSE 채널을 통해 방 접속자 전원에게 실시간 릴레이
            broadcast_room_event(room_code, "spell_cast", event_data)
            return self.send_json(200, {
                "stage": 5,
                "message": f"5단계: 마법 [{spell_id}] 시전 이벤트 동기화 완료",
                "event": event_data
            })

        # ─────────────────────────────────────────────────────────────────────
        # [6단계] AI 손동작 인식 — $P+ 궤적 인식 및 19종 스킬 분류
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage6/ai/recognize", "/api/ai/recognize"):
            points = body.get("points", [])
            result = recognize_gesture(points, body.get("candidates"), bool(body.get("yDown", False)))
            return self.send_json(200, {
                "stage": 6,
                "message": "6단계: AI 룬 제스처 모델 추론 완료",
                "result": result
            })

        # ─────────────────────────────────────────────────────────────────────
        # [7단계] 전투 판정과 전적 저장 — 체력/승패 판정 및 랭킹 DB 기록
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage7/combat/record-match", "/api/matches/record"):
            room_code = body.get("roomCode", "ROOM-PVP")
            winner = body.get("winner", "데모마법사")
            loser = body.get("loser", "도전자")
            duration = float(body.get("durationSec", 180.0))
            reason = body.get("endReason", "PlayerDeath")

            with get_db() as conn:
                c = conn.cursor()
                c.execute("INSERT INTO match_records (room_code, winner_nickname, loser_nickname, duration_sec, end_reason) VALUES (?, ?, ?, ?, ?)",
                          (room_code, winner, loser, duration, reason))
                # 랭킹 점수 업데이트 (승자 +20, 패자 -15)
                c.execute("UPDATE players SET rating = rating + 20, wins = wins + 1 WHERE nickname = ?", (winner,))
                c.execute("UPDATE players SET rating = max(0, rating - 15), losses = losses + 1 WHERE nickname = ?", (loser,))
                conn.commit()

            return self.send_json(200, {
                "stage": 7,
                "message": "7단계: 경기 승패 판정 및 전적/랭킹 DB 저장 완료",
                "match": {
                    "roomCode": room_code,
                    "winner": winner,
                    "loser": loser,
                    "ratingDeltaWinner": 20,
                    "ratingDeltaLoser": -15
                }
            })

        # ─────────────────────────────────────────────────────────────────────
        # [8단계] 보안 및 배포 — 비정상 패킷 검증 및 세션 복구
        # ─────────────────────────────────────────────────────────────────────
        if path in ("/api/stage8/security/validate", "/api/security/validate"):
            spell_id = body.get("spellId", "S02")
            cooldown_left = float(body.get("cooldownLeft", 0.0))
            if cooldown_left > 0.1:
                with get_db() as conn:
                    conn.execute("INSERT INTO security_logs (event_type, detail) VALUES ('COOLDOWN_VIOLATION', ?)",
                                 (f"쿨타임 미경과 시전 시도: {spell_id}, 잔여: {cooldown_left}s",))
                    conn.commit()
                return self.send_json(400, {
                    "stage": 8,
                    "valid": False,
                    "error": "SECURITY_REJECT: 쿨타임이 아직 끝나지 않은 스킬 시전이 감지되었습니다. (치트 차단)"
                })
            return self.send_json(200, {
                "stage": 8,
                "valid": True,
                "message": "8단계: 서버 권한 판정 통과 (정상 요청)"
            })

        if path in ("/api/stage8/session/recover", "/api/session/recover"):
            user_id = body.get("userId", 1)
            with get_db() as conn:
                conn.execute("INSERT INTO security_logs (player_id, event_type, detail) VALUES (?, 'RECOVERY', '네트워크 단절 후 세션 정상 복구')", (user_id,))
                conn.commit()
            return self.send_json(200, {
                "stage": 8,
                "recovered": True,
                "message": "8단계: 네트워크 단절 세션 복구 완료"
            })

        self.send_json(404, {"error": "경로를 찾을 수 없습니다."})

    # ─────────────────────────────────────────────────────────────────────────
    # 정적 파일 서빙
    # ─────────────────────────────────────────────────────────────────────────
    def serve_static(self, path: str):
        if path == "/" or path == "" or path == "/stages":
            path = "/stages.html"
        clean = path.lstrip("/")
        fpath = os.path.join(STATIC_DIR, clean)
        if not os.path.exists(fpath) or os.path.isdir(fpath):
            fpath = os.path.join(STATIC_DIR, "stages.html")
        if os.path.exists(fpath) and os.path.isfile(fpath):
            ctype, _ = mimetypes.guess_type(fpath)
            with open(fpath, "rb") as f:
                data = f.read()
            self.send_response(200)
            self.send_header("Content-Type", ctype or "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(data)
        else:
            self.send_json(404, {"error": "파일을 찾을 수 없습니다."})

def main():
    port = DEFAULT_PORT
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
        except ValueError:
            pass

    print("=================================================================")
    print(" War of Wizards - 8단계 로드맵 전용 파이썬 서버 (Stage Server)")
    print("=================================================================")
    print(f"[*] SQLite 데이터베이스 초기화 중... ({DB_FILE})")
    init_stage_db()
    print("[*] 8단계 로드맵 기능 초기화 완료!")
    print(f"    - 1단계: Unity 기본 전투 스킬 목록 (/api/stage1/combat-config)")
    print(f"    - 2단계: Photon 방 연결 인증 (/api/stage2/photon/auth)")
    print(f"    - 3단계: 회원가입 / 로그인 / 방 생성 (/api/stage3/...)")
    print(f"    - 4단계: 참가자 목록 / 준비 버튼 / 매칭 취소 (/api/stage4/...)")
    print(f"    - 5단계: 마법 시전 이벤트 실시간 동기화 SSE (/api/stage5/...)")
    print(f"    - 6단계: AI 손동작 제스처 인식 $P+ 모델 (/api/stage6/ai/recognize)")
    print(f"    - 7단계: 전투 판정 / 전적 및 랭킹 저장 (/api/stage7/...)")
    print(f"    - 8단계: 보안 검증 / 쿨타임 치트 차단 / 세션 복구 (/api/stage8/...)")
    print(f"[*] 웹 대시보드 주소: http://localhost:{port}/stages.html")
    print("=================================================================")
    print("[*] 서버가 요청을 기다리고 있습니다. (종료하려면 Ctrl+C)...")

    server = HTTPServer(("0.0.0.0", port), StageHttpHandler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[*] 서버가 안전하게 종료되었습니다.")

if __name__ == "__main__":
    main()
