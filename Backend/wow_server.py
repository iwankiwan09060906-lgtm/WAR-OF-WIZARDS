# -*- coding: utf-8 -*-
"""
War of Wizards (워 오브 위자드) — 통합 백엔드 단일 파일 서버 (All-in-One Single Backend Server)
Spring Boot 17개 Controller / Service / Repository / Dual DB 아키텍처를
파이썬 표준 라이브러리(Zero-Dependency) 단일 파일로 완벽 구현한 올인원 서버입니다.

포함 기능:
  1. REST API (인증, 프로필, 8슬롯 덱, 19종 스킬, 전적, 큐/봇 매칭, 친선전 방, 친구, 우편, 실시간 접속)
  2. 실시간 SSE (Server-Sent Events) 브로드캐스터 (/api/live/events)
  3. 관리자(Admin) 콘솔 API 및 실시간 방송/설정 핫 리로드
  4. Photon Cloud 인증 및 Webhook 호환 엔드포인트
  5. Python AI 룬/모션 데이터셋 수집 및 모델 관리 엔드포인트
  6. 정적 웹 사이트 및 Unity WebGL 호환 파일 서빙
  7. SQLite3 임베디드 데이터베이스 자동 생성 및 초기 데이터(데모 계정/19종 스킬/기본 설정) 시딩
  8. 웹 UI(static/index.html) 연결: 친구 요청/수락, 우편+공지 읽음, 접속 상태, 사람끼리 매칭, 친선전 룸
  9. Google 로그인 (클라이언트 ID 설정 시), 룬 그림 인식(GestureTools/MediaPipe/wow_runes.py),
     시전 신호 → Unity (UDP 127.0.0.1:5067 + SSE 'spell')

실행 방법:
  python wow_server.py [포트]
  예: python wow_server.py 8080
"""

import os
import re
import sys
import json
import time
import uuid
import hmac
import base64
import random
import socket
import hashlib
import sqlite3
import threading
import traceback
import urllib.error
import urllib.request
from collections import deque
from datetime import datetime
from urllib.parse import urlparse, parse_qs, unquote, urlencode
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
import mimetypes

# ─────────────────────────────────────────────────────────────────────────────
# 기본 상수 및 설정
# ─────────────────────────────────────────────────────────────────────────────
DEFAULT_PORT = 8080
SERVER_DIR = os.path.dirname(os.path.abspath(__file__))
STATIC_DIR = os.path.join(SERVER_DIR, "static")
STATIC_ROOT = os.path.realpath(STATIC_DIR)
DB_FILE = os.environ.get("WOW_DB_FILE") or os.path.join(SERVER_DIR, "wow_database.sqlite")   # 시험용 DB 는 WOW_DB_FILE 로
JWT_SECRET = os.environ.get("JWT_SECRET", "wow-secret-key-super-safe-32chars-min-length-2026")
SERVER_KEY = os.environ.get("WOW_SERVER_KEY", "wow-server-secret-key-2026")
TOKEN_EXPIRY_HOURS = 24

# Google 로그인: 클라이언트 ID 는 환경변수 GOOGLE_CLIENT_ID → 이 파일 첫 줄 → 관리자 설정 auth.google_client_id 순서로 찾는다
GOOGLE_CLIENT_ID_FILE = os.path.join(SERVER_DIR, "google_client_id.txt")

# 시전 신호를 받을 Unity (SpellCastReceiver.cs). 관리자 설정 unity.spell_udp 로도 바꿀 수 있다.
UNITY_SPELL_UDP = os.environ.get("WOW_UNITY_SPELL_UDP", "127.0.0.1:5067")

# 데모 NPC 마법사 (랭킹 · 친구 화면을 채우는 계정, 로그인 불가). 앞의 4명은 항상 "접속 중"으로 보인다.
NPC_EMAIL_DOMAIN = "npc.wow.dev"
NPC_ALWAYS_ONLINE = ("Aria", "Mira", "Kael", "Nyx")
NPC_PLAYERS = [
    ("Merlin", 1720, 88, 21), ("LunaSpell", 1655, 74, 26), ("StormCaller", 1590, 61, 30),
    ("FrostWitch", 1522, 55, 31), ("Gandalf_K", 1470, 49, 33), ("Aria", 1415, 44, 35),
    ("Nyx", 1362, 40, 36), ("Ember", 1310, 37, 35), ("Orion", 1265, 33, 34),
    ("Selene", 1218, 30, 33), ("Kael", 1170, 27, 33), ("Zephyr", 1122, 24, 31),
    ("Mira", 1080, 21, 29), ("Thorn", 1035, 18, 27), ("Vex", 990, 15, 25),
    ("Lyra", 940, 12, 22), ("Draco", 880, 9, 20), ("Iris", 820, 6, 18),
]
PRESENCE_ONLINE_SEC = 60      # 하트비트가 이 시간 안에 왔으면 접속 중
NEW_PLAYER_RATING = 1000
NICKNAME_RE = re.compile(r"^[가-힣A-Za-z0-9_]{2,12}$")
ROOM_CODE_RE = re.compile(r"^[A-Z0-9]{6}$")

# 룬 인식기 (GestureTools/MediaPipe/wow_runes.py — MediaPipe 손 추적기와 같은 파일을 쓴다)
AI_MEDIAPIPE_DIR = os.path.join(os.path.dirname(SERVER_DIR), "GestureTools", "MediaPipe")
try:
    if AI_MEDIAPIPE_DIR not in sys.path:
        sys.path.append(AI_MEDIAPIPE_DIR)
    import wow_runes
except Exception:  # 폴더가 없어도 서버는 뜬다 (룬 인식 API 만 503)
    wow_runes = None

# SSE 클라이언트 목록: list of queue.Queue
SSE_CLIENTS = []
SSE_LOCK = threading.Lock()

# 매칭 대기열 (메모리)
MATCH_QUEUE = []  # [{ "player_id": int, "nickname": str, "rating": int, "joined_at": float }]
MATCH_RESULTS = {}  # player_id → 상대가 먼저 확인해서 만들어진 매칭 결과 (내가 확인할 때 꺼낸다)
QUEUE_LOCK = threading.Lock()

# 최근 시전 신호 (Unity 없이 확인용: GET /api/spells/recent)
SPELL_LOG = deque(maxlen=50)
UDP_SOCK = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# 스킬 19종 — 팀 스킬 그림 문서 기준 (왼쪽 룬 그림을 그리면 → 오른쪽 마법진이 Unity 에서 시전)
# (id, 코드, 이름, 영문, 등급, 대상, UI 종류, 쿨타임, 피해, 회복, 색, 설명)
SKILLS = [
    (1, "S01", "연막", "Smoke", "Normal", "Column", "projectile", 5.0, 0, 0, "#94a3b8", "적중 시 상대 명중률 감소 및 시야 외곽 안개 부여"),
    (2, "S02", "독 장판", "PoisonField", "Normal", "Area", "area", 5.0, 3, 0, "#84cc16", "구역 내 초당 지속 피해 누적"),
    (3, "S03", "파이어볼", "Fireball", "Normal", "Area", "area", 5.0, 14, 0, "#f97316", "소범위 집중 즉발 피해"),
    (4, "S04", "화살 세례", "ArrowRain", "Normal", "Area", "area", 5.0, 8, 0, "#fbbf24", "광범위 다수 분산 피해"),
    (5, "S05", "토네이도", "Tornado", "Normal", "Column", "projectile", 5.0, 6, 0, "#67e8f9", "경로 상 적 유닛 넉백 및 피해"),
    (6, "S06", "번개", "Lightning", "Normal", "Area", "area", 5.0, 16, 0, "#a78bfa", "지정 위치 타격 및 스플래시 피해"),
    (7, "S07", "총난사", "Barrage", "Normal", "Column", "projectile", 5.0, 12, 0, "#ec4899", "열 전방 유닛부터 순차 연사 타격"),
    (8, "S08", "방어 쉴드", "Shield", "Normal", "Self", "buff", 5.0, 0, 0, "#22d3ee", "피해 흡수 쉴드 생성 (피격 없을 시 HP 회복)"),
    (9, "S09", "타워 버프", "TowerBuff", "Normal", "Self", "buff", 5.0, 0, 0, "#c4b5fd", "본인 타워 방어력/공격력 일시 강화 (타워 파괴 시 비활성화)"),
    (10, "S10", "집중", "Focus", "Normal", "Self", "buff", 5.0, 0, 0, "#facc15", "다음 공격 1회의 명중률 및 공격력 대폭 증가"),
    (11, "S11", "도끼 전사", "AxeWarrior", "Normal", "Summon", "summon", 5.0, 0, 0, "#dc2626", "근접 공격형 소환수 소환"),
    (12, "S12", "드래곤", "Dragon", "Normal", "Summon", "summon", 5.0, 0, 0, "#818cf8", "공중 비행 원거리 화염 소환수 소환"),
    (13, "S13", "터렛", "Turret", "Normal", "Structure", "install", 5.0, 0, 0, "#71717a", "적 유닛 요격용 고정 포탑 설치 (진영당 1기)"),
    (101, "H01", "프리즈", "Freeze", "HighPower", "Global", "global", 20.0, 0, 0, "#38bdf8", "적 전체 미니언 및 소환수 5초간 행동 정지"),
    (102, "H02", "레이저 빔", "LaserBeam", "HighPower", "Beam", "beam", 20.0, 30, 0, "#e879f9", "0.5초 예고선 후 열 전체 관통 타격 (유닛 및 끝의 플레이어)"),
    (103, "H03", "흡혈", "Vampire", "HighPower", "Column", "projectile", 20.0, 12, 0, "#be123c", "적 플레이어/유닛 타격 시 실 피해량 비례 HP 회복"),
    (104, "H04", "반사 결계", "ReflectBarrier", "HighPower", "Self", "buff", 20.0, 0, 0, "#2dd4bf", "상대 플레이어의 스킬 공격 1회 무효화 및 일부 피해 반사"),
    (105, "H05", "분노", "Rage", "HighPower", "Global", "global", 20.0, 0, 0, "#ef4444", "필드 내 기존 아군 유닛들의 공격력/공격속도 증가"),
    (106, "H06", "소환탑", "SummonTower", "HighPower", "Structure", "install", 20.0, 0, 0, "#8b5cf6", "주기적으로 소형 근접 소환수를 생성하는 탑 설치"),
]
SKILL_BY_CODE = {s[1]: s for s in SKILLS}


def skill_images(code):
    """웹 · Unity 공용 스킬 그림 주소 (Backend/static/assets/skills/...). 파일만 바꾸면 반영된다."""
    return {
        "circleImage": f"/assets/skills/circle/{code}.png",      # 오른쪽 마법진 (시전 시 표시)
        "runeImage": f"/assets/skills/rune/{code}.png",          # 왼쪽 룬 그림 (굵은 선)
        "runeLineImage": f"/assets/skills/rune_line/{code}.png", # 룬 그리는 선 (가는 선)
    }


def spell_number(code):
    """계약서 SpellCastRequest.SpellId: S01~S13 → 1~13, H01~H06 → 101~106"""
    return int(code[1:]) + (100 if code[0] == "H" else 0)

# ─────────────────────────────────────────────────────────────────────────────
# 비밀번호 해시 및 JWT 유틸리티
# ─────────────────────────────────────────────────────────────────────────────
def hash_password(password: str, salt: bytes = None) -> str:
    """PBKDF2-HMAC-SHA256 해시 생성"""
    if not salt:
        salt = os.urandom(16)
    dk = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, 100000)
    return base64.b64encode(salt).decode("utf-8") + "$" + base64.b64encode(dk).decode("utf-8")

def verify_password(stored: str, password: str) -> bool:
    try:
        salt_b64, hash_b64 = stored.split("$")
        salt = base64.b64decode(salt_b64)
        dk = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, 100000)
        return hmac.compare_digest(base64.b64encode(dk).decode("utf-8"), hash_b64)
    except Exception:
        return False

def b64url_encode(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode("utf-8").rstrip("=")

def b64url_decode(data: str) -> bytes:
    pad = len(data) % 4
    if pad > 0:
        data += "=" * (4 - pad)
    return base64.urlsafe_b64decode(data)

def generate_jwt(user_id: int, email: str, role: str) -> str:
    header = {"alg": "HS256", "typ": "JWT"}
    now = int(time.time())
    payload = {
        "sub": str(user_id),
        "email": email,
        "role": role,
        "iat": now,
        "exp": now + (TOKEN_EXPIRY_HOURS * 3600)
    }
    h_str = b64url_encode(json.dumps(header, separators=(",", ":")).encode("utf-8"))
    p_str = b64url_encode(json.dumps(payload, separators=(",", ":")).encode("utf-8"))
    msg = f"{h_str}.{p_str}".encode("utf-8")
    sig = hmac.new(JWT_SECRET.encode("utf-8"), msg, hashlib.sha256).digest()
    sig_str = b64url_encode(sig)
    return f"{h_str}.{p_str}.{sig_str}"

def verify_jwt(token: str):
    try:
        parts = token.split(".")
        if len(parts) != 3:
            return None
        h_str, p_str, sig_str = parts
        msg = f"{h_str}.{p_str}".encode("utf-8")
        expected_sig = hmac.new(JWT_SECRET.encode("utf-8"), msg, hashlib.sha256).digest()
        actual_sig = b64url_decode(sig_str)
        if not hmac.compare_digest(expected_sig, actual_sig):
            return None
        payload = json.loads(b64url_decode(p_str).decode("utf-8"))
        if payload.get("exp", 0) < time.time():
            return None
        return payload
    except Exception:
        return None

# ─────────────────────────────────────────────────────────────────────────────
# 데이터베이스 초기화 및 시딩
# ─────────────────────────────────────────────────────────────────────────────
def get_db():
    conn = sqlite3.connect(DB_FILE, timeout=10)
    conn.row_factory = sqlite3.Row
    return conn


def ensure_column(c, table, column, decl):
    """예전 DB 파일에도 새 컬럼을 붙인다 (ALTER TABLE ADD COLUMN)."""
    cols = [r[1] for r in c.execute(f"PRAGMA table_info({table})").fetchall()]
    if column not in cols:
        c.execute(f"ALTER TABLE {table} ADD COLUMN {column} {decl}")


def get_config(conn, key, default=""):
    row = conn.execute("SELECT config_value FROM game_configs WHERE config_key = ?", (key,)).fetchone()
    return row["config_value"] if row and row["config_value"] != "" else default


def get_config_float(conn, key, default):
    try:
        return float(get_config(conn, key, default))
    except (TypeError, ValueError):
        return float(default)


def is_npc_email(email):
    return (email or "").endswith("@" + NPC_EMAIL_DOMAIN)


def unique_nickname(c, base):
    """닉네임 규칙(2~12자, 한글·영문·숫자·_)에 맞추고 겹치면 숫자를 붙인다."""
    nick = re.sub(r"[^가-힣A-Za-z0-9_]", "", base or "")[:12]
    if len(nick) < 2:
        nick = "마법사" + str(random.randint(100, 999))
    candidate = nick
    n = 1
    while c.execute("SELECT id FROM players WHERE nickname = ?", (candidate,)).fetchone():
        n += 1
        suffix = str(n)
        candidate = nick[:12 - len(suffix)] + suffix
    return candidate

def init_db():
    with get_db() as conn:
        c = conn.cursor()
        c.executescript("""
        CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            email TEXT UNIQUE NOT NULL,
            password_hash TEXT NOT NULL,
            role TEXT DEFAULT 'ROLE_USER',
            active INTEGER DEFAULT 1,
            failed_attempts INTEGER DEFAULT 0,
            locked_until REAL DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS players (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER UNIQUE NOT NULL,
            nickname TEXT UNIQUE NOT NULL,
            rating INTEGER DEFAULT 1200,
            wins INTEGER DEFAULT 0,
            losses INTEGER DEFAULT 0,
            draws INTEGER DEFAULT 0,
            avatar_url TEXT DEFAULT '',
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (user_id) REFERENCES users(id)
        );

        CREATE TABLE IF NOT EXISTS player_decks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            player_id INTEGER UNIQUE NOT NULL,
            skills TEXT NOT NULL,
            updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (player_id) REFERENCES players(id)
        );

        CREATE TABLE IF NOT EXISTS skills (
            id INTEGER PRIMARY KEY,
            code TEXT UNIQUE NOT NULL,
            name TEXT NOT NULL,
            name_en TEXT NOT NULL,
            tier TEXT NOT NULL, -- Normal, HighPower
            targeting TEXT NOT NULL, -- Column, Area, Beam, Summon, Structure, Self, Global
            cooldown REAL DEFAULT 5.0,
            damage REAL DEFAULT 0.0,
            heal REAL DEFAULT 0.0,
            description TEXT DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS matches (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            room_code TEXT DEFAULT '',
            mode TEXT DEFAULT 'matchmaking',
            result TEXT DEFAULT '',
            duration_sec REAL DEFAULT 0,
            match_token TEXT DEFAULT '',
            recorded_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS match_players (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            match_id INTEGER NOT NULL,
            player_id INTEGER NOT NULL,
            opponent_name TEXT DEFAULT 'Bot',
            casts INTEGER DEFAULT 0,
            damage_dealt REAL DEFAULT 0,
            damage_taken REAL DEFAULT 0,
            result TEXT DEFAULT '',
            rating_delta INTEGER DEFAULT 0,
            FOREIGN KEY (match_id) REFERENCES matches(id),
            FOREIGN KEY (player_id) REFERENCES players(id)
        );

        CREATE TABLE IF NOT EXISTS friendships (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            requester_id INTEGER NOT NULL,
            addressee_id INTEGER NOT NULL,
            status TEXT DEFAULT 'ACCEPTED', -- PENDING, ACCEPTED, REJECTED
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(requester_id, addressee_id)
        );

        CREATE TABLE IF NOT EXISTS mails (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            receiver_id INTEGER NOT NULL,
            sender_name TEXT DEFAULT '시스템',
            type TEXT DEFAULT 'NOTICE', -- NOTICE, FRIEND_REQUEST, INVITE, SYSTEM
            title TEXT NOT NULL,
            content TEXT NOT NULL,
            reward_type TEXT DEFAULT '',
            reward_amount INTEGER DEFAULT 0,
            is_read INTEGER DEFAULT 0,
            is_claimed INTEGER DEFAULT 0,
            room_code TEXT DEFAULT '',
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (receiver_id) REFERENCES players(id)
        );

        CREATE TABLE IF NOT EXISTS notices (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT NOT NULL,
            content TEXT NOT NULL,
            author_name TEXT DEFAULT '운영진',
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS game_rooms (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            code TEXT UNIQUE NOT NULL,
            title TEXT DEFAULT '친선전 방',
            host_player_id INTEGER NOT NULL,
            guest_player_id INTEGER DEFAULT NULL,
            mode TEXT DEFAULT 'friendly',
            status TEXT DEFAULT 'WAITING', -- WAITING, READY, PLAYING, CLOSED
            host_ready INTEGER DEFAULT 1,
            guest_ready INTEGER DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS login_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER,
            email TEXT,
            ip TEXT,
            user_agent TEXT,
            status TEXT,
            failure_reason TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS admin_audit_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            admin_user_id INTEGER,
            action TEXT,
            target_type TEXT,
            target_id TEXT,
            details TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS game_configs (
            config_key TEXT PRIMARY KEY,
            config_value TEXT NOT NULL,
            description TEXT DEFAULT '',
            updated_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS presence (
            player_id INTEGER PRIMARY KEY,
            last_heartbeat REAL,
            FOREIGN KEY (player_id) REFERENCES players(id)
        );

        CREATE TABLE IF NOT EXISTS gesture_samples (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            player_id INTEGER,
            skill_code TEXT NOT NULL,
            points_json TEXT NOT NULL,
            stroke_count INTEGER DEFAULT 1,
            score REAL DEFAULT 1.0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS motion_recordings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            player_id INTEGER,
            label TEXT NOT NULL,
            frames_json TEXT NOT NULL,
            duration_ms INTEGER DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS notice_reads (
            player_id INTEGER NOT NULL,
            notice_id INTEGER NOT NULL,
            read_at TEXT DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (player_id, notice_id)
        );

        -- 매칭으로 만들어진 게임 방: 웹이 "게임 시작" → 매칭 → 이 방 → Unity 가 접속(join) → 결과 보고(result)
        CREATE TABLE IF NOT EXISTS match_sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            room_code TEXT NOT NULL,
            mode TEXT DEFAULT 'ranked',
            player1_id INTEGER NOT NULL,
            player2_id INTEGER DEFAULT NULL,     -- NULL 이면 봇 상대
            bot_name TEXT DEFAULT '',
            bot_rating INTEGER DEFAULT 0,
            status TEXT DEFAULT 'MATCHED',       -- MATCHED → PLAYING → ENDED / CANCELLED
            p1_unity INTEGER DEFAULT 0,
            p2_unity INTEGER DEFAULT 0,
            p1_result TEXT DEFAULT '',
            p2_result TEXT DEFAULT '',
            p1_delta INTEGER DEFAULT 0,
            p2_delta INTEGER DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            updated_at REAL DEFAULT 0
        );
        """)

        # 예전 DB 파일에 새 컬럼 붙이기
        ensure_column(c, "users", "google_sub", "TEXT DEFAULT ''")
        ensure_column(c, "skills", "kind", "TEXT DEFAULT ''")
        ensure_column(c, "skills", "color", "TEXT DEFAULT ''")
        ensure_column(c, "skills", "rune_hint", "TEXT DEFAULT ''")
        ensure_column(c, "skills", "circle_image", "TEXT DEFAULT ''")
        ensure_column(c, "skills", "rune_image", "TEXT DEFAULT ''")
        ensure_column(c, "skills", "rune_line_image", "TEXT DEFAULT ''")

        # 19종 스킬 시딩 — 이름 · 설명이 바뀌면 기존 DB 도 덮어쓴다 (UPSERT)
        for (sid, code, name, name_en, tier, targeting, kind, cooldown, damage, heal, color, desc) in SKILLS:
            img = skill_images(code)
            hint = wow_runes.SPELLS[code][1] if wow_runes and code in wow_runes.SPELLS else ""
            c.execute("""
            INSERT INTO skills (id, code, name, name_en, tier, targeting, cooldown, damage, heal, description,
                                kind, color, rune_hint, circle_image, rune_image, rune_line_image)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(id) DO UPDATE SET
                code = excluded.code, name = excluded.name, name_en = excluded.name_en, tier = excluded.tier,
                targeting = excluded.targeting, cooldown = excluded.cooldown, damage = excluded.damage,
                heal = excluded.heal, description = excluded.description, kind = excluded.kind,
                color = excluded.color, rune_hint = excluded.rune_hint, circle_image = excluded.circle_image,
                rune_image = excluded.rune_image, rune_line_image = excluded.rune_line_image
            """, (sid, code, name, name_en, tier, targeting, cooldown, damage, heal, desc,
                  kind, color, hint, img["circleImage"], img["runeImage"], img["runeLineImage"]))

        # 게임 기본 설정 시딩
        default_configs = [
            ("matchmaking.bot_timeout", "15", "랭킹전 대기 이 시간(초)이 지나면 봇과 매칭"),
            ("rating.ranked_win", "16", "랭킹전 승리 시 레이팅 +"),
            ("rating.ranked_lose", "16", "랭킹전 패배 시 레이팅 -"),
            ("auth.google_client_id", "", "Google 로그인 OAuth 클라이언트 ID (웹 애플리케이션)"),
            ("unity.spell_udp", UNITY_SPELL_UDP, "시전 신호를 보낼 Unity 주소 (호스트:포트)"),
            ("cooldown.normal", "5.0", "일반 스킬 기본 쿨타임 (초)"),
            ("cooldown.highpower", "20.0", "고위력 스킬 기본 쿨타임 (초)"),
            ("game.wave_interval", "10.0", "미니언 웨이브 생성 주기 (초)"),
            ("game.afk_timeout", "30.0", "무활동(AFK) 기권 처리 제한 시간 (초)"),
            ("game.buff_interval", "300.0", "5분 경기 강화 타이머 (초)"),
            ("game.maintenance_mode", "false", "서버 점검 모드 여부"),
            ("game.event_exp_multiplier", "1.0", "경험치/랭킹 이벤트 배율"),
            ("game.highpower_limit", "2", "덱 내 고위력 스킬 최대 장착 수"),
            ("game.highpower_casts", "3", "경기당 고위력 스킬 사용 제한 횟수")
        ]
        for cf in default_configs:
            c.execute("""
            INSERT OR IGNORE INTO game_configs (config_key, config_value, description)
            VALUES (?, ?, ?)
            """, cf)

        # 데모 계정 및 관리자 계정 시딩
        # 1. 데모 사용자 demo@wow.dev / demo1234
        demo_user = c.execute("SELECT id FROM users WHERE email = 'demo@wow.dev'").fetchone()
        if not demo_user:
            c.execute("INSERT INTO users (email, password_hash, role) VALUES (?, ?, ?)",
                      ("demo@wow.dev", hash_password("demo1234"), "ROLE_USER"))
            demo_user_id = c.lastrowid
            c.execute("INSERT INTO players (user_id, nickname, rating, wins, losses) VALUES (?, ?, 1200, 5, 2)",
                      (demo_user_id, "데모마법사"))
            p_id = c.lastrowid
            # 기본 8슬롯 덱 (일반 6 + 고위력 2)
            default_deck = json.dumps(["S01", "S02", "S03", "S04", "S05", "S09", "H01", "H02"])
            c.execute("INSERT INTO player_decks (player_id, skills) VALUES (?, ?)", (p_id, default_deck))

        # 2. 관리자 사용자 admin@wow.dev / admin1234
        admin_user = c.execute("SELECT id FROM users WHERE email = 'admin@wow.dev'").fetchone()
        if not admin_user:
            c.execute("INSERT INTO users (email, password_hash, role) VALUES (?, ?, ?)",
                      ("admin@wow.dev", hash_password("admin1234"), "ROLE_ADMIN"))
            admin_user_id = c.lastrowid
            c.execute("INSERT INTO players (user_id, nickname, rating, wins, losses) VALUES (?, ?, 2000, 99, 1)",
                      (admin_user_id, "대마법사관리자"))
            p_id = c.lastrowid
            admin_deck = json.dumps(["S02", "S03", "S05", "S08", "S10", "S13", "H02", "H03"])
            c.execute("INSERT INTO player_decks (player_id, skills) VALUES (?, ?)", (p_id, admin_deck))

        # 기본 공지사항 시딩
        if not c.execute("SELECT id FROM notices LIMIT 1").fetchone():
            c.execute("INSERT INTO notices (title, content, author_name) VALUES (?, ?, ?)",
                      ("War of Wizards 파이널 통합 서버 오픈",
                       "War of Wizards 최종 통합 서버가 실행되었습니다. Quest 3, PC Unity, Web 브라우저 모두 연동됩니다.",
                       "시스템 운영팀"))
        for title, content in (
                ("[공지] 스킬 19종 · 덱 규칙",
                 "덱은 19종 중 8장, 고위력 스킬은 최대 2장까지 편성할 수 있습니다.\n고위력 스킬은 게임당 스킬별 3회까지 시전 가능합니다."),
                ("[공지] 룬을 그리면 마법이 나갑니다",
                 "전투 화면 오른쪽 '룬 그리기' 칸에 마우스로 룬 그림(마법서의 왼쪽 그림)을 그리면 그 스킬이 시전되고,\n"
                 "마법진 그림과 함께 Unity 로 시전 신호가 갑니다. 웹캠 손동작은 GestureTools/MediaPipe/run_hand_tracker.bat 를 쓰세요.")):
            if not c.execute("SELECT id FROM notices WHERE title = ?", (title,)).fetchone():
                c.execute("INSERT INTO notices (title, content) VALUES (?, ?)", (title, content))

        # 데모 NPC 마법사 (랭킹 · 친구 화면용, 로그인 불가 계정)
        npc_ids = {}
        for nick, rating, wins, losses in NPC_PLAYERS:
            row = c.execute("SELECT id FROM players WHERE nickname = ?", (nick,)).fetchone()
            if row:
                npc_ids[nick] = row["id"]
                continue
            email = f"{nick.lower()}@{NPC_EMAIL_DOMAIN}"
            if c.execute("SELECT id FROM users WHERE email = ?", (email,)).fetchone():
                continue
            c.execute("INSERT INTO users (email, password_hash, role) VALUES (?, ?, 'ROLE_USER')",
                      (email, hash_password(uuid.uuid4().hex + uuid.uuid4().hex)))
            uid = c.lastrowid
            c.execute("INSERT INTO players (user_id, nickname, rating, wins, losses) VALUES (?, ?, ?, ?, ?)",
                      (uid, nick, rating, wins, losses))
            npc_ids[nick] = c.lastrowid
            c.execute("INSERT INTO player_decks (player_id, skills) VALUES (?, ?)",
                      (npc_ids[nick], json.dumps(["S01", "S02", "S03", "S04", "S05", "S06", "H01", "H02"])))

        # 데모 계정: 친구 (수락 3 · 받은 요청 1 · 보낸 요청 1) + 전적 5판
        demo_row = c.execute("""SELECT p.id FROM players p JOIN users u ON u.id = p.user_id
                                WHERE u.email = 'demo@wow.dev'""").fetchone()
        if demo_row and npc_ids:
            demo_id = demo_row["id"]
            if not c.execute("SELECT id FROM friendships WHERE requester_id = ? OR addressee_id = ?",
                             (demo_id, demo_id)).fetchone():
                for a, b, status in ((demo_id, "Aria", "ACCEPTED"), ("Kael", demo_id, "ACCEPTED"),
                                     (demo_id, "Mira", "ACCEPTED"), ("Zephyr", demo_id, "PENDING"),
                                     (demo_id, "Draco", "PENDING")):
                    a = npc_ids.get(a, a)
                    b = npc_ids.get(b, b)
                    if isinstance(a, int) and isinstance(b, int):
                        c.execute("INSERT OR IGNORE INTO friendships (requester_id, addressee_id, status) VALUES (?, ?, ?)",
                                  (a, b, status))
            if not c.execute("SELECT id FROM match_players WHERE player_id = ?", (demo_id,)).fetchone():
                modes = ["ranked", "ranked", "bot", "friendly", "ranked"]
                results = ["WIN", "LOSE", "WIN", "WIN", "LOSE"]
                opponents = ["Kael", "Merlin", "Bot", "Aria", "Nyx"]
                for t in range(4, -1, -1):          # 오래된 것부터 넣어 id 순서 = 시간 순서
                    when = time.strftime("%Y-%m-%d %H:%M:%S", time.gmtime(time.time() - 3600 * 7 * (t + 1)))
                    delta = (16 if results[t] == "WIN" else -16) if modes[t] == "ranked" else 0
                    c.execute("INSERT INTO matches (mode, result, duration_sec, recorded_at) VALUES (?, ?, ?, ?)",
                              (modes[t], results[t], 300 + t * 95, when))
                    c.execute("""INSERT INTO match_players (match_id, player_id, opponent_name, casts, result, rating_delta)
                                 VALUES (?, ?, ?, ?, ?, ?)""",
                              (c.lastrowid, demo_id, opponents[t], 24 + t * 6, results[t], delta))

        # 기본 환영 우편
        if demo_user:
            demo_p = c.execute("SELECT id FROM players WHERE user_id = ?", (demo_user["id"],)).fetchone()
            if demo_p and not c.execute("SELECT id FROM mails WHERE receiver_id = ?", (demo_p["id"],)).fetchone():
                c.execute("INSERT INTO mails (receiver_id, title, content, type) VALUES (?, ?, ?, ?)",
                          (demo_p["id"], "아레나에 오신 것을 환영합니다!",
                           "8장의 룬 마법 덱을 조합하고 1:1 VR 배틀 아레나에서 승리하십시오.", "SYSTEM"))

        conn.commit()

# ─────────────────────────────────────────────────────────────────────────────
# 실시간 SSE 이벤트 브로드캐스트 엔진
# ─────────────────────────────────────────────────────────────────────────────
def broadcast_sse(event_type: str, data: dict):
    """모든 연결된 SSE 클라이언트(Unity, Web)로 실시간 이벤트 전송"""
    msg = f"event: {event_type}\ndata: {json.dumps(data, ensure_ascii=False)}\n\n".encode("utf-8")
    with SSE_LOCK:
        active = []
        for client in SSE_CLIENTS:
            try:
                client.send_event(msg)
                active.append(client)
            except Exception:
                pass
        SSE_CLIENTS[:] = active

class SSEClient:
    def __init__(self, wfile):
        self.wfile = wfile
        self.closed = False

    def send_event(self, byte_data: bytes):
        if not self.closed:
            try:
                self.wfile.write(byte_data)
                self.wfile.flush()
            except Exception:
                self.closed = True
                raise

# ─────────────────────────────────────────────────────────────────────────────
# HTTP 요청 핸들러
# ─────────────────────────────────────────────────────────────────────────────
# 예전 웹 화면 파일 이름 → 지금 웹 UI 화면 이름 (옛 주소 호환)
OLD_PAGES = {name: name for name in ("login", "signup", "lobby", "mypage", "settings", "friends", "room", "mailbox",
                                     "ranking", "spellbook", "queue", "countdown", "battle", "result", "admin")}
OLD_PAGES.update({"error": "404", "admin-old": "admin"})


class WowHttpHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    # 웹 화면이 1~2초마다 부르는 확인용 요청은 콘솔에 찍지 않는다 (오류는 그대로 찍힘)
    QUIET_PREFIXES = ("/api/matchmaking/status", "/api/rooms/", "/api/presence/", "/api/auth/me",
                      "/assets/", "/app/", "/client/")

    def log_message(self, fmt, *args):
        try:
            code = str(args[1]) if len(args) > 1 else ""
            path = urlparse(self.path).path
            if code.startswith(("2", "3")) and path.startswith(self.QUIET_PREFIXES):
                return
        except Exception:
            pass
        super().log_message(fmt, *args)

    # 처리 중 예외가 나도 연결을 그냥 끊지 않고 500 JSON 을 돌려준다
    def _safe(self, fn):
        try:
            fn()
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass
        except Exception as e:
            traceback.print_exc()
            try:
                if self.command == "GET" and self._wants_page():
                    return self.send_page_file("50x.html", 500)
                self.send_error_json(500, f"서버 내부 오류: {type(e).__name__}: {e}")
            except Exception:
                pass

    # 브라우저가 화면(HTML)을 연 요청인지: 화면이면 오류도 JSON 대신 안내 화면으로 보여 준다
    def _wants_page(self):
        path = urlparse(self.path).path
        return not path.startswith("/api/") and "text/html" in self.headers.get("Accept", "")

    def send_page_file(self, name: str, status: int):
        with open(os.path.join(STATIC_ROOT, name), "rb") as f:
            data = f.read()
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        self._safe(self._do_GET)

    def do_POST(self):
        self._safe(self._do_POST)

    def do_PUT(self):
        self._safe(self._do_PUT)

    def do_DELETE(self):
        self._safe(self._do_DELETE)

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Server-Key, X-Webhook-Secret")
        self.send_header("Content-Length", "0")
        self.end_headers()

    def me_player(self, conn, user=None):
        """토큰의 사용자 → players 행 (없으면 None)"""
        user = user or self.get_auth_user()
        if not user:
            return None
        return conn.execute("SELECT * FROM players WHERE user_id = ?", (user["sub"],)).fetchone()

    def send_json(self, status_code: int, data):
        # 공백 없는 JSON: Unity 쪽이 문자열로 "\"state\":\"matched\"" 를 찾는 코드와 맞춘다
        body = json.dumps(data, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def send_error_json(self, status_code: int, message: str):
        self.send_json(status_code, {"status": status_code, "message": message})

    def get_auth_user(self):
        auth = self.headers.get("Authorization", "")
        if auth.startswith("Bearer "):
            token = auth[7:].strip()
            return verify_jwt(token)
        # Query param 토큰 체크 (SSE, WebGL 등)
        parsed = urlparse(self.path)
        qs = parse_qs(parsed.query)
        if "access_token" in qs and qs["access_token"]:
            return verify_jwt(qs["access_token"][0])
        return None

    def read_json_body(self):
        length = int(self.headers.get("Content-Length", 0))
        if length > 0:
            raw = self.rfile.read(length).decode("utf-8")
            try:
                data = json.loads(raw)
                return data if isinstance(data, dict) else {}
            except Exception:
                return {}
        return {}

    # ─────────────────────────────────────────────────────────────────────────
    # GET 요청 처리
    # ─────────────────────────────────────────────────────────────────────────
    def _do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path
        qs = parse_qs(parsed.query)

        # 0. 로그인 설정 (웹 UI 가 Google 버튼을 쓸 수 있는지)
        if path == "/api/auth/config":
            cid = google_client_id()
            return self.send_json(200, {"googleClientId": cid, "googleEnabled": bool(cid)})

        # 0-1. 접속 중인 플레이어 id (하트비트 60초 이내 + 데모 NPC 4명)
        if path == "/api/presence/online":
            with get_db() as conn:
                since = time.time() - PRESENCE_ONLINE_SEC
                ids = [r["player_id"] for r in conn.execute(
                    "SELECT player_id FROM presence WHERE last_heartbeat >= ?", (since,)).fetchall()]
                marks = ",".join("?" * len(NPC_ALWAYS_ONLINE))
                ids += [r["id"] for r in conn.execute(
                    f"""SELECT p.id FROM players p JOIN users u ON u.id = p.user_id
                        WHERE p.nickname IN ({marks}) AND u.email LIKE ?""",
                    (*NPC_ALWAYS_ONLINE, "%@" + NPC_EMAIL_DOMAIN)).fetchall()]
            return self.send_json(200, {"online": sorted(set(ids))})

        # 0-2. 우편함 + 공지 (웹 UI 형식: kind = mail · invite · notice)
        if path == "/api/mailbox":
            with get_db() as conn:
                p = self.me_player(conn)
                if not p:
                    return self.send_error_json(401, "로그인이 필요합니다.")
                return self.send_json(200, mailbox_items(conn, p["id"]))

        # 0-3. 최근 시전 신호 (Unity 없이 확인용)
        if path == "/api/spells/recent":
            return self.send_json(200, {"unity": unity_spell_target(), "recent": list(SPELL_LOG)[::-1]})

        # 1. 헬스 체크
        if path == "/api/health" or path == "/health":
            self.send_json(200, {
                "status": "UP",
                "server": "War of Wizards Unified Final Server",
                "version": "1.0.0-final",
                "time": datetime.utcnow().isoformat() + "Z",
                "clients_connected": len(SSE_CLIENTS)
            })
            return

        # 2. SSE 실시간 스트림 (/api/live/events)
        if path == "/api/live/events":
            user = self.get_auth_user()
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream; charset=utf-8")
            self.send_header("Cache-Control", "no-cache")
            self.send_header("Connection", "keep-alive")
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()

            client = SSEClient(self.wfile)
            with SSE_LOCK:
                SSE_CLIENTS.append(client)

            # 최초 환영 메시지
            client.send_event(f"event: connected\ndata: {json.dumps({'message': 'Connected to War of Wizards Live Stream'})}\n\n".encode("utf-8"))

            try:
                while not client.closed:
                    time.sleep(15)
                    client.send_event(b": ping\n\n")
            except Exception:
                client.closed = True
                with SSE_LOCK:
                    if client in SSE_CLIENTS:
                        SSE_CLIENTS.remove(client)
            return

        # 3. 실시간 부트스트랩 데이터 (/api/live/bootstrap)
        if path == "/api/live/bootstrap":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "인증 토큰이 필요합니다.")
            with get_db() as conn:
                p = conn.execute("SELECT * FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어 정보가 없습니다.")
                deck_row = conn.execute("SELECT skills FROM player_decks WHERE player_id = ?", (p["id"],)).fetchone()
                deck = json.loads(deck_row["skills"]) if deck_row else []
                skills = [dict(r) for r in conn.execute("SELECT * FROM skills").fetchall()]
                configs = {r["config_key"]: r["config_value"] for r in conn.execute("SELECT config_key, config_value FROM game_configs").fetchall()}
                unread_mails = conn.execute("SELECT COUNT(*) as cnt FROM mails WHERE receiver_id = ? AND is_read = 0", (p["id"],)).fetchone()["cnt"]

                self.send_json(200, {
                    "profile": {
                        "id": p["id"],
                        "nickname": p["nickname"],
                        "rating": p["rating"],
                        "wins": p["wins"],
                        "losses": p["losses"]
                    },
                    "deck": deck,
                    "skills": skills,
                    "gameConfigs": configs,
                    "unreadMailCount": unread_mails
                })
            return

        # 4. 실시간 게임 설정만 받기 (/api/live/config)
        if path == "/api/live/config":
            with get_db() as conn:
                configs = {r["config_key"]: r["config_value"] for r in conn.execute("SELECT config_key, config_value FROM game_configs").fetchall()}
                self.send_json(200, configs)
            return

        # 5. 인증 내 정보 (/api/auth/me)
        if path == "/api/auth/me":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "인증이 유효하지 않습니다.")
            with get_db() as conn:
                u = conn.execute("SELECT * FROM users WHERE id = ?", (user["sub"],)).fetchone()
                if not u or u["active"] == 0:
                    return self.send_error_json(401, "계정을 찾을 수 없거나 정지되었습니다.")
                p = conn.execute("SELECT * FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                new_token = generate_jwt(u["id"], u["email"], u["role"])
                self.send_json(200, {
                    "accessToken": new_token,
                    "userId": u["id"],
                    "email": u["email"],
                    "role": u["role"],
                    "playerId": p["id"] if p else None,
                    "nickname": p["nickname"] if p else ""
                })
            return

        # 6. 닉네임 사용 가능 여부 (/api/auth/nickname-available)
        if path == "/api/auth/nickname-available":
            nick = qs.get("nickname", [""])[0].strip()
            if not nick:
                return self.send_json(200, {"available": False})
            with get_db() as conn:
                row = conn.execute("SELECT id FROM players WHERE nickname = ?", (nick,)).fetchone()
                self.send_json(200, {"available": row is None})
            return

        # 7. 내 프로필 (/api/players/me)
        if path == "/api/players/me":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            with get_db() as conn:
                p = conn.execute("SELECT * FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "프로필이 없습니다.")
                self.send_json(200, dict(p))
            return

        # 8. 랭킹 목록 (/api/players/ranking) — 관리자 계정은 빼고, 레이팅 → 승수 순
        if path == "/api/players/ranking":
            try:
                limit = max(1, min(500, int(qs.get("limit", [100])[0])))
            except ValueError:
                limit = 100
            with get_db() as conn:
                rows = conn.execute("""
                SELECT p.id, p.nickname, p.rating, p.wins, p.losses, p.draws,
                       ROW_NUMBER() OVER(ORDER BY p.rating DESC, p.wins DESC) as rank
                FROM players p JOIN users u ON u.id = p.user_id
                WHERE u.role != 'ROLE_ADMIN' AND u.active = 1
                ORDER BY p.rating DESC, p.wins DESC LIMIT ?
                """, (limit,)).fetchall()
                self.send_json(200, [dict(r) for r in rows])
            return

        # 9. 다른 플레이어 프로필 (/api/players/{id})
        if path.startswith("/api/players/"):
            try:
                pid = int(path.split("/")[-1])
                with get_db() as conn:
                    p = conn.execute("SELECT * FROM players WHERE id = ?", (pid,)).fetchone()
                    if p:
                        return self.send_json(200, dict(p))
                    return self.send_error_json(404, "플레이어를 찾을 수 없습니다.")
            except ValueError:
                pass

        # 10. 스킬 전체 목록 (/api/skills)
        if path == "/api/skills":
            with get_db() as conn:
                rows = conn.execute("SELECT * FROM skills ORDER BY id ASC").fetchall()
                self.send_json(200, [dict(r) for r in rows])
            return

        # 11. 내 덱 조회 (/api/decks/me)
        if path == "/api/decks/me":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            with get_db() as conn:
                p = conn.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어가 없습니다.")
                row = conn.execute("SELECT skills FROM player_decks WHERE player_id = ?", (p["id"],)).fetchone()
                skills = json.loads(row["skills"]) if row else []
                self.send_json(200, {"playerId": p["id"], "skills": skills})
            return

        # 12. 전적 기록 조회 (/api/matches/player/{id})
        if path.startswith("/api/matches/player/"):
            pid = int(path.split("/")[-1])
            limit = int(qs.get("limit", [30])[0])
            with get_db() as conn:
                rows = conn.execute("""
                SELECT m.id, m.mode, mp.result, mp.opponent_name as opponent,
                       m.duration_sec as durationSec, mp.casts, mp.damage_dealt as damageDealt,
                       mp.damage_taken as damageTaken, mp.rating_delta as ratingDelta,
                       m.recorded_at as recordedAt
                FROM match_players mp
                JOIN matches m ON mp.match_id = m.id
                WHERE mp.player_id = ?
                ORDER BY m.id DESC LIMIT ?
                """, (pid, limit)).fetchall()
                self.send_json(200, [dict(r) for r in rows])
            return

        # 13. 친구 목록 (/api/friends) — ?all=1 이면 받은/보낸 요청까지 (relation: friend · incoming · outgoing)
        if path == "/api/friends":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            include_pending = qs.get("all", ["0"])[0] in ("1", "true")
            with get_db() as conn:
                p = conn.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어 정보 없음")
                statuses = ("ACCEPTED", "PENDING") if include_pending else ("ACCEPTED",)
                rows = conn.execute(f"""
                SELECT f.id, f.requester_id, f.status, o.id as friendPlayerId, o.nickname, o.rating, u.email
                FROM friendships f
                JOIN players o ON o.id = CASE WHEN f.requester_id = ? THEN f.addressee_id ELSE f.requester_id END
                JOIN users u ON u.id = o.user_id
                WHERE (f.requester_id = ? OR f.addressee_id = ?) AND f.status IN ({",".join("?" * len(statuses))})
                ORDER BY o.nickname
                """, (p["id"], p["id"], p["id"], *statuses)).fetchall()
                out = []
                for r in rows:
                    relation = "friend" if r["status"] == "ACCEPTED" else (
                        "outgoing" if r["requester_id"] == p["id"] else "incoming")
                    out.append({"id": r["id"], "friendPlayerId": r["friendPlayerId"], "nickname": r["nickname"],
                                "rating": r["rating"], "status": r["status"], "relation": relation,
                                "npc": is_npc_email(r["email"])})
                self.send_json(200, out)
            return

        # 14. 우편함 목록 (/api/mails)
        if path == "/api/mails":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            with get_db() as conn:
                p = conn.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어 정보 없음")
                rows = conn.execute("SELECT * FROM mails WHERE receiver_id = ? ORDER BY id DESC", (p["id"],)).fetchall()
                self.send_json(200, [dict(r) for r in rows])
            return

        # 15. 안 읽은 우편 개수 (/api/mails/unread-count)
        if path == "/api/mails/unread-count":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            with get_db() as conn:
                p = conn.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                cnt = conn.execute("SELECT COUNT(*) as cnt FROM mails WHERE receiver_id = ? AND is_read = 0", (p["id"],)).fetchone()["cnt"]
                self.send_json(200, {"count": cnt, "unread": cnt})
            return

        # 16. 방 정보 조회 (/api/rooms/{code})
        if path.startswith("/api/rooms/"):
            code = unquote(path.split("/")[-1]).upper()
            with get_db() as conn:
                r = conn.execute("SELECT * FROM game_rooms WHERE code = ?", (code,)).fetchone()
                if not r:
                    return self.send_error_json(404, "방을 찾을 수 없습니다.")
                host = conn.execute("SELECT nickname, rating FROM players WHERE id = ?", (r["host_player_id"],)).fetchone()
                guest = conn.execute("SELECT nickname, rating FROM players WHERE id = ?", (r["guest_player_id"],)).fetchone() if r["guest_player_id"] else None
                res = dict(r)
                res["hostNickname"] = host["nickname"] if host else ""
                res["hostRating"] = host["rating"] if host else None
                res["guestNickname"] = guest["nickname"] if guest else None
                res["guestRating"] = guest["rating"] if guest else None
                self.send_json(200, res)
            return

        # 17. 매칭 대기열 상태 (/api/matchmaking/status)
        #     대기 중인 다른 사람과 레이팅 범위가 맞으면 서로 매칭, bot_timeout 초가 지나면 봇과 매칭
        if path == "/api/matchmaking/status":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            with get_db() as conn:
                p = conn.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어가 없습니다.")
                bot_timeout = get_config_float(conn, "matchmaking.bot_timeout", 15)
            return self.send_json(200, matchmaking_status(p["id"], bot_timeout))

        # 17-1. 내 게임 방 (/api/match/current) — 웹 연결 화면과 Unity 가 같이 본다
        if path == "/api/match/current":
            with get_db() as conn:
                p = self.me_player(conn)
                if not p:
                    return self.send_error_json(401, "로그인이 필요합니다.")
                s = conn.execute("""SELECT * FROM match_sessions
                                    WHERE (player1_id = ? OR player2_id = ?)
                                      AND (status IN ('MATCHED', 'PLAYING') OR (status = 'ENDED' AND updated_at > ?))
                                    ORDER BY id DESC LIMIT 1""", (p["id"], p["id"], time.time() - 600)).fetchone()
                return self.send_json(200, {"match": session_view(conn, s, p["id"]) if s else None})

        # 18. 공지사항 (/api/notices)
        if path == "/api/notices":
            with get_db() as conn:
                rows = conn.execute("SELECT * FROM notices ORDER BY id DESC").fetchall()
                self.send_json(200, [dict(r) for r in rows])
            return

        # 19. 관리자 대시보드 (/api/admin/dashboard)
        if path == "/api/admin/dashboard":
            user = self.get_auth_user()
            if not user or user.get("role") != "ROLE_ADMIN":
                return self.send_error_json(403, "관리자 권한이 필요합니다.")
            with get_db() as conn:
                user_count = conn.execute("SELECT COUNT(*) as cnt FROM users").fetchone()["cnt"]
                match_count = conn.execute("SELECT COUNT(*) as cnt FROM matches").fetchone()["cnt"]
                gesture_count = conn.execute("SELECT COUNT(*) as cnt FROM gesture_samples").fetchone()["cnt"]
                motion_count = conn.execute("SELECT COUNT(*) as cnt FROM motion_recordings").fetchone()["cnt"]
                self.send_json(200, {
                    "totalUsers": user_count,
                    "totalMatches": match_count,
                    "gestureSamples": gesture_count,
                    "motionRecordings": motion_count,
                    "activeSseClients": len(SSE_CLIENTS),
                    "dbStatus": "OK (SQLite Unified)"
                })
            return

        # 20. 관리자 유저 목록 (/api/admin/users)
        if path == "/api/admin/users":
            user = self.get_auth_user()
            if not user or user.get("role") != "ROLE_ADMIN":
                return self.send_error_json(403, "관리자 권한 필요")
            q = qs.get("q", [""])[0].strip()
            with get_db() as conn:
                if q:
                    rows = conn.execute("""
                    SELECT u.id, u.email, u.role, u.active, u.created_at, p.nickname, p.rating
                    FROM users u LEFT JOIN players p ON u.id = p.user_id
                    WHERE u.email LIKE ? OR p.nickname LIKE ?
                    """, (f"%{q}%", f"%{q}%")).fetchall()
                else:
                    rows = conn.execute("""
                    SELECT u.id, u.email, u.role, u.active, u.created_at, p.nickname, p.rating
                    FROM users u LEFT JOIN players p ON u.id = p.user_id
                    """).fetchall()
                self.send_json(200, {"users": [dict(r) for r in rows]})
            return

        # 21~24. 관리자 전용 조회 — 로그인 기록(이메일 포함) 등이 그대로 노출되지 않게 관리자만
        if path in ("/api/admin/configs", "/api/admin/audit-logs", "/api/admin/login-history", "/api/admin/motion"):
            user = self.get_auth_user()
            if not user or user.get("role") != "ROLE_ADMIN":
                return self.send_error_json(403, "관리자 권한이 필요합니다.")

        # 21. 관리자 설정 목록 (/api/admin/configs)
        if path == "/api/admin/configs":
            with get_db() as conn:
                rows = conn.execute("SELECT * FROM game_configs").fetchall()
                self.send_json(200, [dict(r) for r in rows])
            return

        # 22. 관리자 감사 로그 (/api/admin/audit-logs)
        if path == "/api/admin/audit-logs":
            with get_db() as conn:
                rows = conn.execute("SELECT * FROM admin_audit_log ORDER BY id DESC LIMIT 100").fetchall()
                self.send_json(200, [dict(r) for r in rows])
            return

        # 23. 관리자 로그인 기록 (/api/admin/login-history)
        if path == "/api/admin/login-history":
            with get_db() as conn:
                rows = conn.execute("SELECT * FROM login_history ORDER BY id DESC LIMIT 100").fetchall()
                self.send_json(200, [dict(r) for r in rows])
            return

        # 24. 관리자 모션 데이터 통계 (/api/admin/motion)
        if path == "/api/admin/motion":
            with get_db() as conn:
                g_cnt = conn.execute("SELECT COUNT(*) as c FROM gesture_samples").fetchone()["c"]
                m_cnt = conn.execute("SELECT COUNT(*) as c FROM motion_recordings").fetchone()["c"]
                self.send_json(200, {"gestureSampleCount": g_cnt, "motionRecordingCount": m_cnt})
            return

        # 25. 정적 파일 서빙 (HTML, CSS, JS, etc.)
        self.serve_static(path)

    # ─────────────────────────────────────────────────────────────────────────
    # POST 요청 처리
    # ─────────────────────────────────────────────────────────────────────────
    def _do_POST(self):
        parsed = urlparse(self.path)
        path = parsed.path
        body = self.read_json_body()

        # 1. 회원가입 (/api/auth/register)
        if path == "/api/auth/register":
            email = str(body.get("email", "")).strip().lower()
            password = str(body.get("password", ""))
            nickname = str(body.get("nickname", "")).strip()

            if not email or not password or not nickname:
                return self.send_error_json(400, "이메일, 비밀번호, 닉네임은 필수입니다.")
            if "@" not in email:
                return self.send_error_json(400, "올바른 이메일 주소를 입력해주세요.")
            if len(password) < 6:
                return self.send_error_json(400, "비밀번호는 6자 이상이어야 합니다.")
            if not NICKNAME_RE.match(nickname):
                return self.send_error_json(400, "닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.")

            with get_db() as conn:
                c = conn.cursor()
                if c.execute("SELECT id FROM users WHERE email = ?", (email,)).fetchone():
                    return self.send_error_json(400, "이미 가입된 이메일입니다.")
                if c.execute("SELECT id FROM players WHERE lower(nickname) = lower(?)", (nickname,)).fetchone():
                    return self.send_error_json(400, "이미 사용 중인 닉네임입니다.")

                c.execute("INSERT INTO users (email, password_hash) VALUES (?, ?)",
                          (email, hash_password(password)))
                uid = c.lastrowid
                pid = create_player(c, uid, nickname)
                # 가입 로그
                c.execute("INSERT INTO login_history (user_id, email, status) VALUES (?, ?, 'REGISTER')", (uid, email))
                conn.commit()

                token = generate_jwt(uid, email, "ROLE_USER")
                return self.send_json(200, {
                    "accessToken": token,
                    "userId": uid,
                    "email": email,
                    "role": "ROLE_USER",
                    "playerId": pid,
                    "nickname": nickname
                })

        # 2. 로그인 (/api/auth/login)
        if path == "/api/auth/login":
            email = body.get("email", "").strip().lower()
            password = body.get("password", "")

            with get_db() as conn:
                c = conn.cursor()
                user = c.execute("SELECT * FROM users WHERE email = ?", (email,)).fetchone()
                if not user or not verify_password(user["password_hash"], password):
                    c.execute("INSERT INTO login_history (email, status, failure_reason) VALUES (?, 'FAILED', '비밀번호 불일치')", (email,))
                    conn.commit()
                    return self.send_error_json(400, "이메일 또는 비밀번호가 올바르지 않습니다.")

                if user["active"] == 0:
                    return self.send_error_json(403, "정지된 계정입니다. 관리자에게 문의하세요.")

                p = c.execute("SELECT * FROM players WHERE user_id = ?", (user["id"],)).fetchone()
                c.execute("INSERT INTO login_history (user_id, email, status) VALUES (?, ?, 'LOGIN')", (user["id"], email))
                conn.commit()

                token = generate_jwt(user["id"], user["email"], user["role"])
                return self.send_json(200, {
                    "accessToken": token,
                    "userId": user["id"],
                    "email": user["email"],
                    "role": user["role"],
                    "playerId": p["id"] if p else None,
                    "nickname": p["nickname"] if p else ""
                })

        # 2-0. 게스트 플레이어 만들기 (/api/auth/guest) — 닉네임만으로 바로 입장 (게임 연결 화면 · 시험용)
        if path == "/api/auth/guest":
            nickname = str(body.get("nickname", "")).strip()
            if not NICKNAME_RE.match(nickname):
                return self.send_error_json(400, "닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.")
            with get_db() as conn:
                c = conn.cursor()
                if c.execute("SELECT id FROM players WHERE lower(nickname) = lower(?)", (nickname,)).fetchone():
                    return self.send_error_json(400, "이미 사용 중인 닉네임입니다.")
                email = f"guest-{uuid.uuid4().hex[:12]}@guest.wow.dev"
                c.execute("INSERT INTO users (email, password_hash) VALUES (?, ?)",
                          (email, hash_password(uuid.uuid4().hex + uuid.uuid4().hex)))
                uid = c.lastrowid
                pid = create_player(c, uid, nickname)
                c.execute("INSERT INTO login_history (user_id, email, status) VALUES (?, ?, 'GUEST')", (uid, email))
                conn.commit()
            return self.send_json(200, {"accessToken": generate_jwt(uid, email, "ROLE_USER"), "userId": uid, "email": email,
                                        "role": "ROLE_USER", "playerId": pid, "nickname": nickname, "guest": True})

        # 2-1. Google 로그인 (/api/auth/google) — 웹 UI 가 Google 팝업에서 받은 토큰을 서버가 Google 에 확인
        if path == "/api/auth/google":
            cid = google_client_id()
            if not cid:
                return self.send_error_json(400, "서버에 Google 클라이언트 ID 가 설정되지 않았습니다. (Docs/Backend/WebUiServer.md 참고)")
            try:
                info = verify_google_token(cid, body.get("accessToken"), body.get("credential"))
            except PermissionError as e:
                return self.send_error_json(401, str(e))
            except Exception as e:
                return self.send_error_json(502, f"Google 확인 서버에 연결하지 못했습니다: {type(e).__name__}")
            with get_db() as conn:
                c = conn.cursor()
                user = c.execute("SELECT * FROM users WHERE google_sub = ? AND google_sub != ''", (info["sub"],)).fetchone() \
                    or c.execute("SELECT * FROM users WHERE email = ?", (info["email"],)).fetchone()
                if user:
                    if user["active"] == 0:
                        return self.send_error_json(403, "정지된 계정입니다. 관리자에게 문의하세요.")
                    c.execute("UPDATE users SET google_sub = ? WHERE id = ?", (info["sub"], user["id"]))
                    uid, email, role = user["id"], user["email"], user["role"]
                    p = c.execute("SELECT * FROM players WHERE user_id = ?", (uid,)).fetchone()
                    pid = p["id"] if p else create_player(c, uid, unique_nickname(c, info.get("name") or email.split("@")[0]))
                else:
                    # 처음 온 Google 사용자 → 계정 자동 생성 (비밀번호 로그인은 못 하는 임의 비밀번호)
                    email, role = info["email"], "ROLE_USER"
                    c.execute("INSERT INTO users (email, password_hash, google_sub) VALUES (?, ?, ?)",
                              (email, hash_password(uuid.uuid4().hex + uuid.uuid4().hex), info["sub"]))
                    uid = c.lastrowid
                    pid = create_player(c, uid, unique_nickname(c, info.get("name") or email.split("@")[0]))
                nickname = c.execute("SELECT nickname FROM players WHERE id = ?", (pid,)).fetchone()["nickname"]
                c.execute("INSERT INTO login_history (user_id, email, status) VALUES (?, ?, 'LOGIN_GOOGLE')", (uid, email))
                conn.commit()
            return self.send_json(200, {"accessToken": generate_jwt(uid, email, role), "userId": uid, "email": email,
                                        "role": role, "playerId": pid, "nickname": nickname})

        # 2-2. 비밀번호 재설정 요청 (/api/auth/password-reset) — 메일 서버가 없으므로 관리자 콘솔 기록으로 남긴다
        if path == "/api/auth/password-reset":
            email = str(body.get("email", "")).strip().lower()
            with get_db() as conn:
                u = conn.execute("SELECT id, google_sub FROM users WHERE email = ?", (email,)).fetchone()
                if not u or is_npc_email(email):
                    return self.send_error_json(404, "가입되지 않은 이메일입니다.")
                conn.execute("INSERT INTO login_history (user_id, email, status, failure_reason) VALUES (?, ?, 'RESET_REQUEST', ?)",
                             (u["id"], email, "비밀번호 재설정 요청"))
                conn.commit()
            msg = "비밀번호 재설정 요청을 접수했습니다. (메일 서버가 없어 관리자 콘솔 로그인 기록에 남겼습니다)"
            if u["google_sub"]:
                msg = "Google 로 가입한 계정입니다. 'Google 계정으로 계속하기' 로 로그인하세요."
            return self.send_json(200, {"message": msg})

        # 3. 로그아웃 (/api/auth/logout)
        if path == "/api/auth/logout":
            user = self.get_auth_user()
            if user:
                with get_db() as conn:
                    p = self.me_player(conn, user)
                    if p:   # 바로 "접속 끊김"으로 보이게
                        conn.execute("DELETE FROM presence WHERE player_id = ?", (p["id"],))
                        conn.commit()
            return self.send_json(200, {"message": "로그아웃 완료"})

        # 3-1. 룬 그림 인식 (/api/runes/recognize) — 여러 획 가능, 쓸 수 있는 슬롯만 후보
        if path == "/api/runes/recognize":
            if wow_runes is None:
                return self.send_error_json(503, "룬 인식기(GestureTools/MediaPipe/wow_runes.py)를 찾을 수 없습니다.")
            raw = body.get("strokes") or ([body.get("points")] if body.get("points") else [])
            y_sign = -1.0 if body.get("yDown") else 1.0
            strokes, total = [], 0
            for stroke in raw[:30]:
                pts = []
                for q in (stroke or [])[:1500]:
                    try:
                        x, y = (q["x"], q["y"]) if isinstance(q, dict) else (q[0], q[1])
                        pts.append((float(x), y_sign * float(y)))
                    except (KeyError, IndexError, TypeError, ValueError):
                        continue
                total += len(pts)
                if pts:
                    strokes.append(pts)
                if total > 4000:
                    break
            cands = body.get("candidates") or None
            if cands is not None:
                cands = [str(c).upper() for c in cands]
            result = wow_runes.recognize(strokes, cands)
            if result.get("spellId"):
                result.update(skill_images(result["spellId"]))
            return self.send_json(200, {"result": result})

        # 3-2. 시전 신호 → Unity (/api/spells/cast)
        #      phase: ready(룬 인식 → 마법진 띄우기) · cast(시전, column 0 L / 1 C / 2 R) · cancel
        if path == "/api/spells/cast":
            phase = str(body.get("phase", "cast"))
            code = str(body.get("spellId") or "").upper()
            if phase not in ("ready", "cast", "cancel"):
                return self.send_error_json(400, "phase 는 ready · cast · cancel 중 하나입니다.")
            if phase != "cancel" and code not in SKILL_BY_CODE:
                return self.send_error_json(400, f"알 수 없는 스킬 코드: {code}")
            caster, pid = "", 0
            user = self.get_auth_user()
            if user:
                with get_db() as conn:
                    p = self.me_player(conn, user)
                    if p:
                        caster, pid = p["nickname"], p["id"]
            msg = build_spell_message(phase, code, body, caster, pid)
            udp = send_spell_to_unity(msg)
            SPELL_LOG.append(msg)
            broadcast_sse("spell", msg)
            return self.send_json(200, {"sent": True, "unity": udp, "message": msg})

        # 3-3. 우편 · 공지 읽음 (/api/mailbox/read) — id: "m12"(우편) · "n3"(공지). 보상 우편은 수령 처리
        if path == "/api/mailbox/read":
            item_id = str(body.get("id", ""))
            with get_db() as conn:
                p = self.me_player(conn)
                if not p:
                    return self.send_error_json(401, "로그인이 필요합니다.")
                try:
                    num = int(item_id[1:])
                except ValueError:
                    return self.send_error_json(400, "잘못된 우편 id 입니다.")
                if item_id.startswith("m"):
                    conn.execute("""UPDATE mails SET is_read = 1,
                                    is_claimed = CASE WHEN reward_type != '' THEN 1 ELSE is_claimed END
                                    WHERE id = ? AND receiver_id = ?""", (num, p["id"]))
                elif item_id.startswith("n"):
                    conn.execute("INSERT OR IGNORE INTO notice_reads (player_id, notice_id) VALUES (?, ?)", (p["id"], num))
                else:
                    return self.send_error_json(400, "잘못된 우편 id 입니다.")
                conn.commit()
            return self.send_json(200, {"read": True})

        # 3-4. 친구 요청 응답 (/api/friends/respond) {playerId: 요청 보낸 사람, accept: true/false}
        if path == "/api/friends/respond":
            with get_db() as conn:
                me = self.me_player(conn)
                if not me:
                    return self.send_error_json(401, "로그인 필요")
                try:
                    other = int(body.get("playerId"))
                except (TypeError, ValueError):
                    return self.send_error_json(400, "playerId 가 필요합니다.")
                f = conn.execute("""SELECT id FROM friendships WHERE requester_id = ? AND addressee_id = ?
                                    AND status = 'PENDING'""", (other, me["id"])).fetchone()
                if not f:
                    return self.send_error_json(404, "처리할 친구 요청이 없습니다.")
                if body.get("accept"):
                    conn.execute("UPDATE friendships SET status = 'ACCEPTED' WHERE id = ?", (f["id"],))
                else:
                    conn.execute("DELETE FROM friendships WHERE id = ?", (f["id"],))
                conn.commit()
            return self.send_json(200, {"accepted": bool(body.get("accept"))})

        # 4. 전적 기록 (/api/matches)
        if path == "/api/matches":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            mode = str(body.get("mode", "friendly"))
            result = str(body.get("result", "WIN")).upper()  # WIN, LOSE, DRAW (소문자도 받음)
            if result not in ("WIN", "LOSE", "DRAW"):
                return self.send_error_json(400, "result 는 WIN · LOSE · DRAW 중 하나입니다.")
            opponent = str(body.get("opponent", "Bot"))[:40]
            duration = float(body.get("durationSec", 0) or 0)
            casts = int(body.get("casts", 0) or 0)
            dmg_dealt = float(body.get("damageDealt", 0) or 0)
            dmg_taken = float(body.get("damageTaken", 0) or 0)

            with get_db() as conn:
                p = conn.execute("SELECT * FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어가 없습니다.")
                saved = record_match_result(conn, p, mode, result, opponent, duration, casts, dmg_dealt, dmg_taken)
                conn.commit()
            return self.send_json(200, saved)

        # 5. 매칭 큐 참가 (/api/matchmaking/queue)
        if path == "/api/matchmaking/queue":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            with get_db() as conn:
                p = conn.execute("SELECT * FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어가 없습니다.")
                with QUEUE_LOCK:
                    # 중복 확인 + 지난번 매칭 결과 정리
                    MATCH_QUEUE[:] = [item for item in MATCH_QUEUE if item["player_id"] != p["id"]]
                    MATCH_RESULTS.pop(p["id"], None)
                    MATCH_QUEUE.append({
                        "player_id": p["id"],
                        "nickname": p["nickname"],
                        "rating": p["rating"],
                        "joined_at": time.time()
                    })
                return self.send_json(200, {"message": "매칭 대기열에 등록되었습니다."})

        # 5-1. 게임 방 (/api/match/{id}/join · result · leave) — Unity 게임이 부른다
        #      join   : Unity 가 이 방에 들어왔음 (둘 다 들어오면 PLAYING, 봇 상대는 혼자 들어와도 PLAYING)
        #      result : 내 결과 보고 {result: WIN|LOSE|DRAW, durationSec, casts, damageDealt, damageTaken} → 전적 · 레이팅
        #      leave  : 시작 전 취소 / 연결 끊김
        if path.startswith("/api/match/"):
            parts = [x for x in path.split("/") if x]          # api, match, ID, action
            if len(parts) != 4 or parts[3] not in ("join", "result", "leave"):
                return self.send_error_json(404, "경로를 찾을 수 없습니다.")
            try:
                match_id = int(parts[2])
            except ValueError:
                return self.send_error_json(400, "잘못된 게임 방 번호입니다.")
            action = parts[3]
            with get_db() as conn:
                p = self.me_player(conn)
                if not p:
                    return self.send_error_json(401, "로그인이 필요합니다.")
                s = conn.execute("SELECT * FROM match_sessions WHERE id = ?", (match_id,)).fetchone()
                if not s or p["id"] not in (s["player1_id"], s["player2_id"]):
                    return self.send_error_json(404, "내 게임 방이 아닙니다.")
                mine = "p1" if s["player1_id"] == p["id"] else "p2"
                theirs = "p2" if mine == "p1" else "p1"
                bot = s["player2_id"] is None
                now = time.time()
                if action == "join":
                    if s["status"] in ("ENDED", "CANCELLED"):
                        return self.send_error_json(400, "이미 끝났거나 취소된 게임 방입니다.")
                    conn.execute(f"UPDATE match_sessions SET {mine}_unity = 1, updated_at = ? WHERE id = ?", (now, match_id))
                    if bot or s[f"{theirs}_unity"]:
                        conn.execute("UPDATE match_sessions SET status = 'PLAYING' WHERE id = ?", (match_id,))
                elif action == "leave":
                    if s["status"] in ("MATCHED", "PLAYING"):
                        conn.execute("UPDATE match_sessions SET status = 'CANCELLED', updated_at = ? WHERE id = ?", (now, match_id))
                else:  # result
                    if s["status"] == "CANCELLED":
                        return self.send_error_json(400, "취소된 게임 방입니다.")
                    if s[f"{mine}_result"]:
                        return self.send_error_json(400, "이미 결과를 보고했습니다.")
                    result = str(body.get("result", "")).upper()
                    if result not in ("WIN", "LOSE", "DRAW"):
                        return self.send_error_json(400, "result 는 WIN · LOSE · DRAW 중 하나입니다.")
                    view = session_view(conn, s, p["id"])
                    saved = record_match_result(conn, p, s["mode"], result, view["opponent"]["nickname"],
                                                float(body.get("durationSec", 0) or 0), int(body.get("casts", 0) or 0),
                                                float(body.get("damageDealt", 0) or 0), float(body.get("damageTaken", 0) or 0))
                    conn.execute(f"UPDATE match_sessions SET {mine}_result = ?, {mine}_delta = ?, updated_at = ? WHERE id = ?",
                                 (result, saved["ratingDelta"], now, match_id))
                    if bot or s[f"{theirs}_result"]:
                        conn.execute("UPDATE match_sessions SET status = 'ENDED' WHERE id = ?", (match_id,))
                conn.commit()
                s = conn.execute("SELECT * FROM match_sessions WHERE id = ?", (match_id,)).fetchone()
                view = session_view(conn, s, p["id"])
            broadcast_sse("match", {"action": action, "matchId": match_id, "playerId": p["id"], "status": view["status"]})
            return self.send_json(200, {"match": view})

        # 6~9. 친선전 방 (/api/rooms, /api/rooms/{code}/join · ready · start · leave · kick)
        if path == "/api/rooms" or path.startswith("/api/rooms/"):
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            with get_db() as conn:
                p = self.me_player(conn, user)
                if not p:
                    return self.send_error_json(404, "플레이어가 없습니다.")
                return self.room_action(conn, p, path, body)

        # 10. 접속 하트비트 (/api/presence/heartbeat)
        if path == "/api/presence/heartbeat":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인 필요")
            with get_db() as conn:
                p = conn.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if p:
                    conn.execute("INSERT OR REPLACE INTO presence (player_id, last_heartbeat) VALUES (?, ?)",
                                 (p["id"], time.time()))
                    conn.commit()
            return self.send_json(200, {"status": "OK"})

        # 11. 제스처 샘플 등록 (/api/gestures/samples)
        if path == "/api/gestures/samples":
            user = self.get_auth_user()
            pid = None
            if user:
                with get_db() as conn:
                    p = conn.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                    pid = p["id"] if p else None
            skill_code = body.get("skillCode", "S01")
            points = body.get("points", [])
            with get_db() as conn:
                conn.execute("INSERT INTO gesture_samples (player_id, skill_code, points_json) VALUES (?, ?, ?)",
                             (pid, skill_code, json.dumps(points)))
                conn.commit()
            return self.send_json(200, {"message": "제스처 샘플 저장 완료"})

        # 12. VR 모션 녹화 등록 (/api/motions)
        if path == "/api/motions":
            label = body.get("label", "motion")
            frames = body.get("frames", [])
            with get_db() as conn:
                conn.execute("INSERT INTO motion_recordings (label, frames_json) VALUES (?, ?)",
                             (label, json.dumps(frames)))
                conn.commit()
            return self.send_json(200, {"message": "모션 녹화 저장 완료"})

        # 13. 관리자 실시간 방송 메시지 (/api/admin/live/broadcast)
        if path == "/api/admin/live/broadcast":
            user = self.get_auth_user()
            if not user or user.get("role") != "ROLE_ADMIN":
                return self.send_error_json(403, "관리자 권한 필요")
            broadcast_sse("broadcast", body)
            return self.send_json(200, {"message": "방송 완료"})

        # 14. 관리자 공지 생성 (/api/admin/notices)
        if path == "/api/admin/notices":
            user = self.get_auth_user()
            if not user or user.get("role") != "ROLE_ADMIN":
                return self.send_error_json(403, "관리자 권한 필요")
            title = body.get("title", "")
            content = body.get("body", body.get("content", ""))
            with get_db() as conn:
                conn.execute("INSERT INTO notices (title, content) VALUES (?, ?)", (title, content))
                conn.commit()
            broadcast_sse("notice", {"title": title, "content": content})
            return self.send_json(200, {"message": "공지 등록 완료"})

        # 15. 친구 요청 (/api/friends/requests)
        if path == "/api/friends/requests":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인 필요")
            nick = str(body.get("nickname", "")).strip()
            with get_db() as conn:
                c = conn.cursor()
                me = c.execute("SELECT id, nickname FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not me:
                    return self.send_error_json(404, "플레이어가 없습니다.")
                target = c.execute("""SELECT p.id, u.email FROM players p JOIN users u ON u.id = p.user_id
                                      WHERE lower(p.nickname) = lower(?)""", (nick,)).fetchone()
                if not target:
                    return self.send_error_json(404, "해당 닉네임의 마법사를 찾을 수 없습니다.")
                if target["id"] == me["id"]:
                    return self.send_error_json(400, "자기 자신에게는 친구 요청을 보낼 수 없습니다.")
                rows = c.execute("""SELECT * FROM friendships WHERE (requester_id = ? AND addressee_id = ?)
                                    OR (requester_id = ? AND addressee_id = ?)""",
                                 (me["id"], target["id"], target["id"], me["id"])).fetchall()
                if any(r["status"] == "ACCEPTED" for r in rows):
                    return self.send_error_json(400, "이미 친구입니다.")
                incoming = next((r for r in rows if r["status"] == "PENDING" and r["requester_id"] == target["id"]), None)
                if incoming:   # 상대가 먼저 요청해 두었으면 바로 친구
                    c.execute("UPDATE friendships SET status = 'ACCEPTED' WHERE id = ?", (incoming["id"],))
                    conn.commit()
                    return self.send_json(200, {"message": "친구가 되었습니다.", "status": "ACCEPTED"})
                if any(r["status"] == "PENDING" for r in rows):
                    return self.send_error_json(400, "이미 친구 요청이 진행 중입니다.")
                c.execute("DELETE FROM friendships WHERE requester_id = ? AND addressee_id = ?", (me["id"], target["id"]))
                c.execute("INSERT INTO friendships (requester_id, addressee_id, status) VALUES (?, ?, 'PENDING')",
                          (me["id"], target["id"]))
                if not is_npc_email(target["email"]):
                    c.execute("""INSERT INTO mails (receiver_id, sender_name, type, title, content)
                                 VALUES (?, ?, 'FRIEND_REQUEST', ?, ?)""",
                              (target["id"], me["nickname"], f"{me['nickname']} 님의 친구 요청",
                               "친구 화면의 '받은 요청'에서 수락하거나 거절할 수 있습니다."))
                conn.commit()
            return self.send_json(200, {"message": "친구 요청을 보냈습니다.", "status": "PENDING"})

        # 16. 초대 우편 발송 (/api/mails/invite) — 친구에게만
        if path == "/api/mails/invite":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인 필요")
            try:
                fid = int(body.get("friendId"))
            except (TypeError, ValueError):
                return self.send_error_json(400, "friendId 가 필요합니다.")
            room_code = str(body.get("roomCode") or "").upper()
            with get_db() as conn:
                c = conn.cursor()
                p = c.execute("SELECT id, nickname FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어가 없습니다.")
                if not c.execute("""SELECT id FROM friendships WHERE status = 'ACCEPTED' AND
                                    ((requester_id = ? AND addressee_id = ?) OR (requester_id = ? AND addressee_id = ?))""",
                                 (p["id"], fid, fid, p["id"])).fetchone():
                    return self.send_error_json(400, "친구에게만 초대를 보낼 수 있습니다.")
                c.execute("INSERT INTO mails (receiver_id, sender_name, type, title, content, room_code) VALUES (?, ?, 'INVITE', ?, ?, ?)",
                          (fid, p["nickname"], f"{p['nickname']} 님의 친선전 초대",
                           f"{p['nickname']} 님이 친선전에 초대했습니다. 룸 코드: {room_code}", room_code))
                conn.commit()
            broadcast_sse("invite", {"from": p["nickname"], "toPlayerId": fid, "roomCode": room_code})
            return self.send_json(200, {"message": "초대 전송 완료"})

        # 17. Photon 웹훅 / 인증
        if path.startswith("/api/photon/"):
            return self.send_json(200, {"ResultCode": 1, "UserId": body.get("UserId", "user")})

        self.send_error_json(404, "경로를 찾을 수 없습니다.")

    # ─────────────────────────────────────────────────────────────────────────
    # PUT 요청 처리
    # ─────────────────────────────────────────────────────────────────────────
    def _do_PUT(self):
        parsed = urlparse(self.path)
        path = parsed.path
        body = self.read_json_body()

        # 1. 덱 저장 (/api/decks/me)
        if path == "/api/decks/me":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인이 필요합니다.")
            skills = body.get("skills", [])
            if not isinstance(skills, list) or len(skills) != 8:
                return self.send_error_json(400, f"덱은 정확히 8장이어야 합니다. (현재 {len(skills) if isinstance(skills, list) else 0}장)")
            skills = [str(s).upper() for s in skills]
            if len(set(skills)) != 8:
                return self.send_error_json(400, "같은 스킬을 중복해서 넣을 수 없습니다.")
            if any(s not in SKILL_BY_CODE for s in skills):
                return self.send_error_json(400, "존재하지 않는 스킬이 포함되어 있습니다.")

            # 고위력 2장 제한 검증
            with get_db() as conn:
                c = conn.cursor()
                p = c.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if not p:
                    return self.send_error_json(404, "플레이어가 없습니다.")

                high_power_count = 0
                for sk_code in skills:
                    sk = c.execute("SELECT tier FROM skills WHERE code = ?", (sk_code,)).fetchone()
                    if sk and sk["tier"] == "HighPower":
                        high_power_count += 1

                if high_power_count > 2:
                    return self.send_error_json(400, f"고위력 스킬은 최대 2장까지만 포함할 수 있습니다. (현재: {high_power_count}장)")

                c.execute("INSERT OR REPLACE INTO player_decks (player_id, skills) VALUES (?, ?)",
                          (p["id"], json.dumps(skills)))
                conn.commit()
            return self.send_json(200, {"skills": skills})

        # 2. 닉네임 변경 (/api/players/me)
        if path == "/api/players/me":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인 필요")
            nick = str(body.get("nickname", "")).strip()
            if not NICKNAME_RE.match(nick):
                return self.send_error_json(400, "닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.")
            with get_db() as conn:
                c = conn.cursor()
                if c.execute("SELECT id FROM players WHERE lower(nickname) = lower(?)", (nick,)).fetchone():
                    return self.send_error_json(400, "이미 사용 중인 닉네임입니다.")
                c.execute("UPDATE players SET nickname = ? WHERE user_id = ?", (nick, user["sub"]))
                conn.commit()
            return self.send_json(200, {"nickname": nick})

        # 3. 비밀번호 변경 (/api/auth/password)
        if path == "/api/auth/password":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인 필요")
            cur_pwd = body.get("currentPassword", "")
            new_pwd = body.get("newPassword", "")
            with get_db() as conn:
                c = conn.cursor()
                u = c.execute("SELECT * FROM users WHERE id = ?", (user["sub"],)).fetchone()
                if not verify_password(u["password_hash"], cur_pwd):
                    return self.send_error_json(400, "현재 비밀번호가 일치하지 않습니다.")
                c.execute("UPDATE users SET password_hash = ? WHERE id = ?", (hash_password(new_pwd), u["id"]))
                conn.commit()
            return self.send_json(200, {"message": "비밀번호 변경 완료"})

        # 4. 관리자 게임 설정 업데이트 (/api/admin/configs/{key})
        if path.startswith("/api/admin/configs/"):
            user = self.get_auth_user()
            if not user or user.get("role") != "ROLE_ADMIN":
                return self.send_error_json(403, "관리자 권한 필요")
            key = path.split("/")[-1]
            val = body.get("value", "")
            with get_db() as conn:
                conn.execute("INSERT OR REPLACE INTO game_configs (config_key, config_value) VALUES (?, ?)", (key, str(val)))
                conn.commit()
            broadcast_sse("config", {key: val})
            return self.send_json(200, {"key": key, "value": val})

        self.send_error_json(404, "경로 없음")

    # ─────────────────────────────────────────────────────────────────────────
    # DELETE 요청 처리
    # ─────────────────────────────────────────────────────────────────────────
    def _do_DELETE(self):
        parsed = urlparse(self.path)
        path = parsed.path

        # 1. 큐 취소 (/api/matchmaking/queue)
        if path == "/api/matchmaking/queue":
            user = self.get_auth_user()
            if not user:
                return self.send_error_json(401, "로그인 필요")
            with get_db() as conn:
                p = conn.execute("SELECT id FROM players WHERE user_id = ?", (user["sub"],)).fetchone()
                if p:
                    with QUEUE_LOCK:
                        MATCH_QUEUE[:] = [item for item in MATCH_QUEUE if item["player_id"] != p["id"]]
            return self.send_json(200, {"message": "대기열에서 나갔습니다."})

        # 2. 우편 삭제 (/api/mails/{id}) — 내 우편만
        if path.startswith("/api/mails/"):
            with get_db() as conn:
                p = self.me_player(conn)
                if not p:
                    return self.send_error_json(401, "로그인 필요")
                mid = int(path.split("/")[-1])
                conn.execute("DELETE FROM mails WHERE id = ? AND receiver_id = ?", (mid, p["id"]))
                conn.commit()
            return self.send_json(200, {"message": "우편 삭제 완료"})

        # 3. 친구 삭제 — /api/friends/player/{상대 playerId} (웹 UI) · /api/friends/{friendship id} (예전 방식)
        if path.startswith("/api/friends/"):
            with get_db() as conn:
                p = self.me_player(conn)
                if not p:
                    return self.send_error_json(401, "로그인 필요")
                parts = path.split("/")
                if len(parts) == 5 and parts[3] == "player":
                    other = int(parts[4])
                    conn.execute("""DELETE FROM friendships WHERE (requester_id = ? AND addressee_id = ?)
                                    OR (requester_id = ? AND addressee_id = ?)""", (p["id"], other, other, p["id"]))
                else:
                    fid = int(parts[-1])
                    conn.execute("DELETE FROM friendships WHERE id = ? AND (requester_id = ? OR addressee_id = ?)",
                                 (fid, p["id"], p["id"]))
                conn.commit()
            return self.send_json(200, {"message": "친구 삭제 완료"})

        self.send_error_json(404, "경로 없음")

    # ─────────────────────────────────────────────────────────────────────────
    # 친선전 방 (/api/rooms ...)
    # ─────────────────────────────────────────────────────────────────────────
    def room_action(self, conn, p, path, body=None):
        parts = [x for x in path.split("/") if x]          # api, rooms, CODE, action
        c = conn.cursor()
        if len(parts) == 2:                                  # 방 만들기 — 내가 열어 둔 예전 방은 닫는다
            c.execute("UPDATE game_rooms SET status = 'CLOSED' WHERE host_player_id = ? AND status IN ('WAITING', 'READY')",
                      (p["id"],))
            # Unity ISessionService.CreateFriendlyRoom() 은 코드를 바로 돌려줘야 해서 클라이언트가 코드를 제안할 수 있다.
            # 형식이 맞고 비어 있으면 그 코드를 쓰고, 아니면 서버가 새로 만든다 (응답의 code 가 최종 코드).
            requested = str((body or {}).get("code") or "").strip().upper()
            code = requested if ROOM_CODE_RE.match(requested) else uuid.uuid4().hex[:6].upper()
            while c.execute("SELECT id FROM game_rooms WHERE code = ?", (code,)).fetchone():
                code = uuid.uuid4().hex[:6].upper()
            c.execute("INSERT INTO game_rooms (code, host_player_id, mode) VALUES (?, ?, 'friendly')", (code, p["id"]))
            conn.commit()
            return self.send_json(200, {"code": code, "roomCode": code})
        if len(parts) != 4:
            return self.send_error_json(404, "경로를 찾을 수 없습니다.")
        code, action = unquote(parts[2]).upper(), parts[3]
        r = c.execute("SELECT * FROM game_rooms WHERE code = ?", (code,)).fetchone()
        if not r:
            return self.send_error_json(404, "존재하지 않는 방입니다.")
        is_host = r["host_player_id"] == p["id"]
        is_guest = r["guest_player_id"] == p["id"]

        if action == "join":
            if is_host:
                return self.send_json(200, {"message": "방장으로 입장", "code": code})
            if r["status"] in ("CLOSED", "PLAYING"):
                return self.send_error_json(400, "이미 닫혔거나 대전이 시작된 방입니다.")
            if r["guest_player_id"] and not is_guest:
                return self.send_error_json(400, "방이 이미 꽉 찼습니다.")
            c.execute("UPDATE game_rooms SET guest_player_id = ?, guest_ready = 0 WHERE code = ?", (p["id"], code))
            conn.commit()
            broadcast_sse("room", {"action": "join", "roomCode": code, "guestPlayerId": p["id"]})
            return self.send_json(200, {"message": "방에 참가했습니다.", "code": code})

        if action == "ready":
            qs = parse_qs(urlparse(self.path).query)
            is_ready = qs.get("ready", ["true"])[0] == "true"
            if not is_guest:
                return self.send_error_json(400, "이 방의 게스트가 아닙니다.")
            c.execute("UPDATE game_rooms SET guest_ready = ? WHERE code = ?", (1 if is_ready else 0, code))
            conn.commit()
            broadcast_sse("room", {"action": "ready", "roomCode": code, "ready": is_ready})
            return self.send_json(200, {"ready": is_ready})

        if action == "start":
            if not is_host:
                return self.send_error_json(403, "방장만 대전을 시작할 수 있습니다.")
            c.execute("UPDATE game_rooms SET status = 'PLAYING' WHERE code = ?", (code,))
            conn.commit()
            broadcast_sse("room", {"action": "start", "roomCode": code})
            return self.send_json(200, {"message": "게임이 시작되었습니다."})

        if action == "leave":
            if is_host:
                c.execute("UPDATE game_rooms SET status = 'CLOSED' WHERE code = ?", (code,))
            elif is_guest:
                c.execute("UPDATE game_rooms SET guest_player_id = NULL, guest_ready = 0 WHERE code = ?", (code,))
            conn.commit()
            broadcast_sse("room", {"action": "leave", "roomCode": code, "playerId": p["id"], "host": is_host})
            return self.send_json(200, {"message": "방에서 나갔습니다."})

        if action == "kick":
            if not is_host:
                return self.send_error_json(403, "방장만 내보낼 수 있습니다.")
            c.execute("UPDATE game_rooms SET guest_player_id = NULL, guest_ready = 0 WHERE code = ?", (code,))
            conn.commit()
            broadcast_sse("room", {"action": "kick", "roomCode": code})
            return self.send_json(200, {"message": "게스트를 내보냈습니다."})

        return self.send_error_json(404, "경로를 찾을 수 없습니다.")

    # ─────────────────────────────────────────────────────────────────────────
    # 정적 파일 서빙 유틸리티 (static 폴더 밖 파일은 못 읽게 막는다)
    # ─────────────────────────────────────────────────────────────────────────
    MIME = {".html": "text/html; charset=utf-8", ".js": "application/javascript; charset=utf-8",
            ".css": "text/css; charset=utf-8", ".json": "application/json; charset=utf-8",
            ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".svg": "image/svg+xml",
            ".webp": "image/webp", ".ico": "image/x-icon", ".txt": "text/plain; charset=utf-8",
            ".wasm": "application/wasm", ".data": "application/octet-stream"}

    def serve_static(self, path: str):
        path = unquote(path)
        if path == "/" or path == "":
            path = "/index.html"
        file_path = os.path.realpath(os.path.join(STATIC_ROOT, path.lstrip("/")))
        if file_path != STATIC_ROOT and not file_path.startswith(STATIC_ROOT + os.sep):
            return self.send_error_json(403, "접근할 수 없는 경로입니다.")
        if os.path.isdir(file_path) and os.path.isfile(os.path.join(file_path, "index.html")):
            file_path = os.path.join(file_path, "index.html")
        status = 200
        if not os.path.isfile(file_path):
            ext = os.path.splitext(path)[1].lower()
            # /game/ 은 Unity WebGL 빌드 자리: 없으면 404 (게임 화면이 '빌드 없음'으로 판단)
            if path.startswith(("/api/", "/game/")) or (ext and ext != ".html"):
                return self.send_error_json(404, "파일 또는 API를 찾을 수 없습니다.")
            # 예전 화면 파일 주소(/lobby.html 등)는 새 화면(#/lobby)으로 넘긴다
            old_page = path.strip("/")[:-5] if path.endswith(".html") else ""
            if old_page in OLD_PAGES:
                self.send_response(302)
                self.send_header("Location", f"/#/{OLD_PAGES[old_page]}")
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            # 없는 화면 주소 → 404 안내 화면 (웹 UI 화면 이동은 #/로비 같은 해시 주소를 씀)
            file_path, status = os.path.join(STATIC_ROOT, "404.html"), 404
        if not os.path.isfile(file_path):
            return self.send_error_json(404, "파일 또는 API를 찾을 수 없습니다.")
        ext = os.path.splitext(file_path)[1].lower()
        ctype = self.MIME.get(ext) or mimetypes.guess_type(file_path)[0] or "application/octet-stream"
        with open(file_path, "rb") as f:
            data = f.read()
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-cache")      # 그림 · 화면 파일을 바꾸면 새로고침만으로 반영
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(data)


# ─────────────────────────────────────────────────────────────────────────────
# 도우미 함수 (요청 처리에서 공용)
# ─────────────────────────────────────────────────────────────────────────────
def create_player(c, uid, nickname):
    """새 플레이어 + 기본 덱 + 환영 우편 (기획서 §23.1: 우편에 보상 · 재화 없음)"""
    c.execute("INSERT INTO players (user_id, nickname, rating) VALUES (?, ?, ?)", (uid, nickname, NEW_PLAYER_RATING))
    pid = c.lastrowid
    default_deck = json.dumps(["S01", "S02", "S03", "S04", "S05", "S09", "H01", "H02"])
    c.execute("INSERT INTO player_decks (player_id, skills) VALUES (?, ?)", (pid, default_deck))
    c.execute("""INSERT INTO mails (receiver_id, title, content, type)
                 VALUES (?, ?, ?, 'SYSTEM')""",
              (pid, "가입을 환영합니다!", "마법사 등록을 축하드려요. 마법서에서 덱 8장을 편성하고 첫 대전을 시작해보세요."))
    return pid


def mailbox_items(conn, pid):
    """우편(mails) + 공지(notices) → 웹 UI 우편함 형식, 최신순"""
    items = []
    for m in conn.execute("SELECT * FROM mails WHERE receiver_id = ? ORDER BY id DESC", (pid,)).fetchall():
        reward = f"{m['reward_type']} {m['reward_amount']}" if m["reward_type"] and m["reward_amount"] else None
        items.append({
            "id": f"m{m['id']}", "kind": "invite" if m["type"] == "INVITE" else "mail",
            "title": m["title"], "body": m["content"], "reward": reward, "claimed": bool(m["is_claimed"]),
            "from": m["sender_name"] if m["sender_name"] and m["sender_name"] != "시스템" else None,
            "roomCode": m["room_code"] or None, "read": bool(m["is_read"]), "createdAt": m["created_at"],
        })
    for n in conn.execute("""SELECT n.*, EXISTS(SELECT 1 FROM notice_reads r WHERE r.player_id = ? AND r.notice_id = n.id)
                             AS is_read FROM notices n ORDER BY n.id DESC""", (pid,)).fetchall():
        items.append({"id": f"n{n['id']}", "kind": "notice", "title": n["title"], "body": n["content"],
                      "from": n["author_name"], "read": bool(n["is_read"]), "createdAt": n["created_at"]})
    items.sort(key=lambda it: it["createdAt"] or "", reverse=True)
    return items


def matchmaking_status(pid, bot_timeout):
    """대기열 상태. 다른 사람과 레이팅 범위(50 + 대기초×15)가 맞으면 서로 매칭, 시간이 지나면 봇."""
    now = time.time()
    with QUEUE_LOCK:
        done = MATCH_RESULTS.pop(pid, None)
        if done and now - done["at"] < 120:
            return done["result"]
        entry = next((q for q in MATCH_QUEUE if q["player_id"] == pid), None)
        if not entry:
            return {"state": "none"}
        elapsed = now - entry["joined_at"]
        window = 50 + 15 * elapsed
        others = sorted((q for q in MATCH_QUEUE if q["player_id"] != pid and
                         abs(q["rating"] - entry["rating"]) <= max(window, 50 + 15 * (now - q["joined_at"]))),
                        key=lambda q: q["joined_at"])
        if others:
            other = others[0]
            MATCH_QUEUE[:] = [q for q in MATCH_QUEUE if q["player_id"] not in (pid, other["player_id"])]
            room = f"RANK-{uuid.uuid4().hex[:6].upper()}"
            mid = create_match_session(room, other["player_id"], pid)   # 먼저 기다린 사람이 player1
            MATCH_RESULTS[other["player_id"]] = {"at": now, "result": {
                "state": "matched", "opponentNickname": entry["nickname"], "opponentRating": entry["rating"],
                "opponentPlayerId": pid, "bot": False, "roomCode": room, "matchId": mid}}
            return {"state": "matched", "opponentNickname": other["nickname"], "opponentRating": other["rating"],
                    "opponentPlayerId": other["player_id"], "bot": False, "roomCode": room, "matchId": mid}
        if elapsed >= bot_timeout:
            MATCH_QUEUE.remove(entry)
            rating = max(0, entry["rating"] + random.randint(-50, 50))
            room = f"BOT-{uuid.uuid4().hex[:6].upper()}"
            mid = create_match_session(room, pid, None, f"Bot ({rating})", rating)
            return {"state": "matched", "opponentNickname": f"Bot ({rating})", "opponentRating": rating,
                    "bot": True, "roomCode": room, "matchId": mid}
        return {"state": "searching", "elapsedSeconds": int(elapsed), "ratingWindow": int(window),
                "botTimeout": bot_timeout, "queueSize": len(MATCH_QUEUE)}


def create_match_session(room, p1, p2, bot_name="", bot_rating=0):
    """매칭이 잡히면 게임 방(match_sessions)을 만든다. 같은 사람의 끝나지 않은 예전 방은 취소."""
    with get_db() as conn:
        ids = [p for p in (p1, p2) if p]
        marks = ",".join("?" * len(ids))
        conn.execute(f"""UPDATE match_sessions SET status = 'CANCELLED', updated_at = ?
                         WHERE status IN ('MATCHED', 'PLAYING') AND (player1_id IN ({marks}) OR player2_id IN ({marks}))""",
                     (time.time(), *ids, *ids))
        cur = conn.execute("""INSERT INTO match_sessions (room_code, mode, player1_id, player2_id, bot_name, bot_rating, updated_at)
                              VALUES (?, 'ranked', ?, ?, ?, ?, ?)""", (room, p1, p2, bot_name, bot_rating, time.time()))
        conn.commit()
        mid = cur.lastrowid
    broadcast_sse("match", {"action": "matched", "matchId": mid, "roomCode": room, "players": ids, "bot": p2 is None})
    return mid


def record_match_result(conn, p, mode, result, opponent, duration=0.0, casts=0, dmg_dealt=0.0, dmg_taken=0.0):
    """전적 한 줄 + 레이팅 반영 (랭킹전만 ±). /api/matches 와 /api/match/{id}/result 가 같이 쓴다."""
    c = conn.cursor()
    c.execute("INSERT INTO matches (mode, result, duration_sec) VALUES (?, ?, ?)", (mode, result, duration))
    mid = c.lastrowid
    ranked = mode.lower() in ("ranked", "matchmaking", "rank")
    win_pts = int(get_config_float(conn, "rating.ranked_win", 16))
    lose_pts = int(get_config_float(conn, "rating.ranked_lose", 16))
    delta = (win_pts if result == "WIN" else (-lose_pts if result == "LOSE" else 0)) if ranked else 0
    new_rating = max(0, p["rating"] + delta)
    c.execute("""INSERT INTO match_players (match_id, player_id, opponent_name, casts, damage_dealt, damage_taken, result, rating_delta)
                 VALUES (?, ?, ?, ?, ?, ?, ?, ?)""", (mid, p["id"], opponent, casts, dmg_dealt, dmg_taken, result, delta))
    c.execute("UPDATE players SET rating = ?, wins = ?, losses = ?, draws = ? WHERE id = ?",
              (new_rating, p["wins"] + (result == "WIN"), p["losses"] + (result == "LOSE"), p["draws"] + (result == "DRAW"), p["id"]))
    return {"matchId": mid, "result": result, "ratingDelta": delta, "newRating": new_rating}


def session_view(conn, s, me_id):
    """게임 방 정보 (나 기준): 상대, Unity 접속 여부, 결과"""
    i_am_p1 = s["player1_id"] == me_id
    me = conn.execute("SELECT id, nickname, rating FROM players WHERE id = ?", (me_id,)).fetchone()
    other_id = s["player2_id"] if i_am_p1 else s["player1_id"]
    if other_id:
        o = conn.execute("SELECT id, nickname, rating FROM players WHERE id = ?", (other_id,)).fetchone()
        opponent = {"playerId": o["id"], "nickname": o["nickname"], "rating": o["rating"], "bot": False}
    else:
        opponent = {"playerId": None, "nickname": s["bot_name"] or "Bot", "rating": s["bot_rating"], "bot": True}
    mine, theirs = ("p1", "p2") if i_am_p1 else ("p2", "p1")
    return {
        "matchId": s["id"], "roomCode": s["room_code"], "mode": s["mode"], "status": s["status"],
        "me": {"playerId": me["id"], "nickname": me["nickname"], "rating": me["rating"]},
        "opponent": opponent,
        "unity": {"me": bool(s[f"{mine}_unity"]), "opponent": True if opponent["bot"] else bool(s[f"{theirs}_unity"])},
        "result": s[f"{mine}_result"] or None, "ratingDelta": s[f"{mine}_delta"],
        "opponentResult": s[f"{theirs}_result"] or None,
        "createdAt": s["created_at"],
    }


def google_client_id():
    cid = os.environ.get("GOOGLE_CLIENT_ID", "").strip()
    if cid:
        return cid
    try:
        with open(GOOGLE_CLIENT_ID_FILE, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith("#"):
                    return line
    except OSError:
        pass
    try:
        with get_db() as conn:
            return get_config(conn, "auth.google_client_id", "").strip()
    except Exception:
        return ""


def _http_json(url, headers=None):
    req = urllib.request.Request(url, headers=headers or {})
    try:
        with urllib.request.urlopen(req, timeout=6) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        if 400 <= e.code < 500:
            raise PermissionError("Google 토큰이 유효하지 않거나 만료되었습니다.")
        raise


def verify_google_token(client_id, access_token=None, id_token=None):
    """Google 에 토큰을 직접 물어 본다 (서명 · 만료 확인은 Google 이 함). 우리 클라이언트 ID 로 발급된 것만 인정."""
    if id_token:
        info = _http_json("https://oauth2.googleapis.com/tokeninfo?" + urlencode({"id_token": id_token}))
        if info.get("aud") != client_id:
            raise PermissionError("다른 앱용으로 발급된 Google 토큰입니다.")
        name = info.get("name", "")
    elif access_token:
        info = _http_json("https://oauth2.googleapis.com/tokeninfo?" + urlencode({"access_token": access_token}))
        if client_id not in (info.get("aud"), info.get("azp")):
            raise PermissionError("다른 앱용으로 발급된 Google 토큰입니다.")
        profile = _http_json("https://openidconnect.googleapis.com/v1/userinfo",
                             {"Authorization": "Bearer " + access_token})
        info.setdefault("sub", profile.get("sub"))
        info["email"] = info.get("email") or profile.get("email")
        info["email_verified"] = info.get("email_verified", profile.get("email_verified"))
        name = profile.get("name", "")
    else:
        raise PermissionError("Google 토큰이 없습니다.")
    if not info.get("sub") or not info.get("email"):
        raise PermissionError("Google 계정 이메일을 받지 못했습니다. (email 권한 필요)")
    if str(info.get("email_verified")).lower() not in ("true", "1"):
        raise PermissionError("이메일 인증이 안 된 Google 계정입니다.")
    return {"sub": str(info["sub"]), "email": info["email"].lower(), "name": name}


def unity_spell_target():
    try:
        with get_db() as conn:
            value = get_config(conn, "unity.spell_udp", UNITY_SPELL_UDP)
    except Exception:
        value = UNITY_SPELL_UDP
    host, _, port = value.rpartition(":")
    try:
        return host or "127.0.0.1", int(port)
    except ValueError:
        return "127.0.0.1", 5067


def build_spell_message(phase, code, body, caster, pid):
    """Unity SpellCastReceiver.cs 가 JsonUtility 로 바로 읽는 평평한 JSON (null 없음)"""
    msg = {"type": "spell", "phase": phase, "spellId": code, "spellNumber": 0, "name": "", "nameEn": "",
           "tier": "", "kind": "", "targeting": "", "color": "", "circleImage": "", "runeImage": "",
           "runeLineImage": "", "column": -1, "depth": 0.5, "slot": -1,
           "caster": caster, "playerId": pid, "source": str(body.get("source") or "web")[:20],
           "t": round(time.time(), 3)}
    sk = SKILL_BY_CODE.get(code)
    if sk:
        msg.update({"spellNumber": spell_number(code), "name": sk[2], "nameEn": sk[3], "tier": sk[4],
                    "targeting": sk[5], "kind": sk[6], "color": sk[10]})
        msg.update(skill_images(code))
    for key, lo, hi, cast in (("column", 0, 2, int), ("slot", 0, 7, int), ("depth", 0.0, 1.0, float)):
        try:
            v = body.get(key)
            if v is not None:
                msg[key] = min(hi, max(lo, cast(v)))
        except (TypeError, ValueError):
            pass
    return msg


def send_spell_to_unity(msg):
    host, port = unity_spell_target()
    try:
        UDP_SOCK.sendto(json.dumps(msg, ensure_ascii=False).encode("utf-8"), (host, port))
        return f"{host}:{port}"
    except OSError as e:
        return f"{host}:{port} 전송 실패 ({e})"


# ─────────────────────────────────────────────────────────────────────────────
# 메인 서버 실행 진입점
# ─────────────────────────────────────────────────────────────────────────────
def main():
    try:
        if hasattr(sys.stdout, "reconfigure"):
            sys.stdout.reconfigure(encoding="utf-8")
        if hasattr(sys.stderr, "reconfigure"):
            sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

    port = DEFAULT_PORT
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
        except ValueError:
            pass

    print("=================================================================")
    print(" War of Wizards (워 오브 위자드) - 통합 백엔드 올인원 서버")
    print("=================================================================")
    print(f"[*] SQLite 데이터베이스 초기화 중... ({DB_FILE})")
    init_db()
    print("[*] 데이터베이스 준비 완료! (기본 데모 계정 / 19종 스킬 / 설정 시딩됨)")
    print(f"[*] 정적 웹 리소스 폴더: {STATIC_DIR}")
    print(f"[*] 서버 주소: http://localhost:{port}")
    print(f"    - 메인 웹 페이지: http://localhost:{port}/")
    print(f"    - 관리자 콘솔:   http://localhost:{port}/#/admin (관리자 계정으로 로그인)")
    print(f"    - 헬스 체크:     http://localhost:{port}/api/health")
    print("    - 기본 계정: 데모(demo@wow.dev / demo1234), 관리자(admin@wow.dev / admin1234)")
    print("=================================================================")
    print("[*] 서버가 요청을 기다리고 있습니다. (종료하려면 Ctrl+C)...")

    host, uport = unity_spell_target()
    print(f"    - 웹 UI 룬 시전 → Unity UDP {host}:{uport} (+ SSE /api/live/events 'spell')")
    print(f"    - Google 로그인: {'켜짐' if google_client_id() else '꺼짐 (Backend/google_client_id.txt 에 클라이언트 ID)'}")
    print(f"    - 룬 인식기: {'GestureTools/MediaPipe/wow_runes.py' if wow_runes else '없음 (룬 인식 API 503)'}")

    server = ThreadingHTTPServer(("0.0.0.0", port), WowHttpHandler)
    server.daemon_threads = True      # SSE 연결이 남아 있어도 Ctrl+C 로 바로 종료
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[*] 서버가 안전하게 종료되었습니다.")

if __name__ == "__main__":
    main()
