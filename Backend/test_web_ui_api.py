# -*- coding: utf-8 -*-
"""
웹 UI(static/index.html)가 쓰는 서버 API 검사.

임시 DB 복사본으로 서버를 따로 띄워서 검사하므로 실제 wow_database.sqlite 는 바뀌지 않는다.
    python test_web_ui_api.py
"""
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

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "GestureTools", "MediaPipe"))
import wow_runes  # noqa: E402

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


PORT = free_port()
UNITY_PORT = free_port()
BASE = f"http://127.0.0.1:{PORT}"
passed = 0


def call(method, path, body=None, token=None, raw=False):
    headers = {}
    if token:
        headers["Authorization"] = "Bearer " + token
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as r:
            payload = r.read()
            return r.status, (payload if raw else (json.loads(payload) if payload else None)), r.headers
    except urllib.error.HTTPError as e:
        payload = e.read()
        try:
            payload = json.loads(payload)
        except Exception:
            pass
        return e.code, payload, e.headers


def check(name, cond, detail=""):
    global passed
    if not cond:
        raise AssertionError(f"[실패] {name} {detail}")
    passed += 1
    print(f"  OK  {name}")


def login(email, password):
    st, r, _ = call("POST", "/api/auth/login", {"email": email, "password": password})
    assert st == 200, (st, r)
    return r


def register(tag):
    nick = f"시험{tag}{random.randint(100, 999)}"
    email = f"t{tag}{random.randint(10000, 99999)}@test.dev"
    st, r, _ = call("POST", "/api/auth/register", {"email": email, "password": "pass1234", "nickname": nick})
    assert st == 200, (st, r)
    return r


def main():
    tmp = tempfile.mkdtemp(prefix="wow_test_")
    db = os.path.join(tmp, "test.sqlite")
    src = os.path.join(HERE, "wow_database.sqlite")
    if os.path.exists(src):
        shutil.copy(src, db)
    env = dict(os.environ, WOW_DB_FILE=db, WOW_UNITY_SPELL_UDP=f"127.0.0.1:{UNITY_PORT}", PYTHONIOENCODING="utf-8")
    env.pop("GOOGLE_CLIENT_ID", None)
    proc = subprocess.Popen([sys.executable, os.path.join(HERE, "wow_server.py"), str(PORT)], env=env,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    unity = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    unity.bind(("127.0.0.1", UNITY_PORT))
    unity.settimeout(2)
    try:
        for _ in range(100):
            try:
                if call("GET", "/api/health")[0] == 200:
                    break
            except Exception:
                time.sleep(0.1)
        run_checks(unity)
    finally:
        proc.terminate()
        proc.wait(timeout=5)
        unity.close()
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"\n[V] 웹 UI 서버 API 검사 {passed}개 모두 통과")


def run_checks(unity):
    print("[화면 파일]")
    st, body, h = call("GET", "/", raw=True)
    check("/ → 새 웹 UI index.html", st == 200 and b"client/main.js" in body)
    st, _, h = call("GET", "/client/main.js", raw=True)
    check("JS 파일 MIME", st == 200 and "javascript" in h.get("Content-Type", ""))
    for page in ("404.html", "50x.html"):
        st, body, _ = call("GET", "/" + page, raw=True)
        check(f"에러 화면 {page}", st == 200 and b"client/wow.css" in body)
    st, body, h = call("GET", "/assets/skills/circle/S06.png", raw=True)
    check("스킬 마법진 그림 PNG", st == 200 and body[:4] == b"\x89PNG")
    st, _, _ = call("GET", "/assets/skills/circle/X99.png", raw=True)
    check("없는 그림은 404 (index.html 로 대신하지 않음)", st == 404)
    st, _, _ = call("GET", "/..%2Fwow_database.sqlite", raw=True)
    check("static 폴더 밖 파일 차단", st in (403, 404))
    st, body, h = call("GET", "/nowhere/page", raw=True)
    check("없는 화면 주소는 404 안내 화면", st == 404 and "text/html" in h.get("Content-Type", "") and b"404" in body)
    st, body, _ = call("GET", "/lobby.html", raw=True)
    check("예전 화면 주소(/lobby.html)는 새 웹 UI 로 이동", st == 200 and b"client/main.js" in body)

    print("[로그인 · 설정]")
    st, cfg, _ = call("GET", "/api/auth/config")
    check("Google 설정 조회 (ID 없으면 꺼짐)", st == 200 and cfg["googleEnabled"] is False)
    st, r, _ = call("POST", "/api/auth/google", {"accessToken": "x"})
    check("Google ID 없으면 400 안내", st == 400 and "클라이언트 ID" in r["message"])
    demo = login("demo@wow.dev", "demo1234")
    tok = demo["accessToken"]
    st, me, _ = call("GET", "/api/auth/me", token=tok)
    check("세션 복구 /api/auth/me", st == 200 and me["playerId"] == demo["playerId"])
    st, r, _ = call("POST", "/api/auth/password-reset", {"email": "demo@wow.dev"})
    check("비밀번호 재설정 요청 접수", st == 200 and "접수" in r["message"])
    st, r, _ = call("POST", "/api/auth/password-reset", {"email": "nobody@x.dev"})
    check("없는 이메일 재설정은 404", st == 404)

    print("[스킬 19종 · 덱]")
    st, skills, _ = call("GET", "/api/skills")
    names = {s["code"]: s["name"] for s in skills}
    check("스킬 19종 새 이름 (S02 독 장판 · H06 소환탑)", len(skills) == 19 and names["S02"] == "독 장판" and names["H06"] == "소환탑")
    check("스킬에 마법진/룬 그림 주소", all(s["circle_image"].endswith(f"/{s['code']}.png") for s in skills))
    st, r, _ = call("PUT", "/api/decks/me", {"skills": ["S01"] * 8}, tok)
    check("덱 중복 거부", st == 400 and "중복" in r["message"])
    st, r, _ = call("PUT", "/api/decks/me", {"skills": ["S01", "S02", "S03", "S04", "S05", "H01", "H02", "H03"]}, tok)
    check("덱 고위력 3장 거부", st == 400)
    deck = ["S03", "S02", "S07", "S08", "S11", "S13", "H01", "H02"]
    st, r, _ = call("PUT", "/api/decks/me", {"skills": deck}, tok)
    check("덱 저장", st == 200 and call("GET", "/api/decks/me", token=tok)[1]["skills"] == deck)

    print("[랭킹 · 전적]")
    st, rank, _ = call("GET", "/api/players/ranking?limit=100")
    check("랭킹: NPC 포함 · 관리자 제외", st == 200 and rank[0]["nickname"] == "Merlin"
          and all(r["nickname"] != "대마법사관리자" for r in rank))
    st, hist, _ = call("GET", f"/api/matches/player/{demo['playerId']}?limit=30", token=tok)
    check("데모 전적 5판", st == 200 and len(hist) >= 5)
    before = call("GET", f"/api/players/{demo['playerId']}")[1]["rating"]
    st, r, _ = call("POST", "/api/matches", {"mode": "ranked", "result": "win", "opponent": "Bot (1200)", "durationSec": 300, "casts": 20}, tok)
    check("랭킹전 승리 +16", st == 200 and r["ratingDelta"] == 16 and r["newRating"] == before + 16)
    st, r, _ = call("POST", "/api/matches", {"mode": "friendly", "result": "lose", "opponent": "Aria"}, tok)
    check("친선전은 레이팅 변동 없음", st == 200 and r["ratingDelta"] == 0)

    print("[친구 · 접속 · 우편]")
    st, fr, _ = call("GET", "/api/friends?all=1", token=tok)
    rel = {f["nickname"]: f["relation"] for f in fr}
    check("데모 친구 (수락 · 받은 요청 · 보낸 요청)", rel.get("Aria") == "friend" and rel.get("Zephyr") == "incoming" and rel.get("Draco") == "outgoing")
    st, r, _ = call("GET", "/api/friends", token=tok)
    check("예전 /api/friends 는 수락된 친구만", all(f["status"] == "ACCEPTED" for f in r))
    call("POST", "/api/presence/heartbeat", {}, tok)
    st, on, _ = call("GET", "/api/presence/online")
    aria = next(f for f in fr if f["nickname"] == "Aria")
    check("접속 중: 나 + NPC", demo["playerId"] in on["online"] and aria["friendPlayerId"] in on["online"])
    st, mails, _ = call("GET", "/api/mailbox", token=tok)
    notice = next(m for m in mails if m["kind"] == "notice")
    check("우편함에 공지 포함", st == 200 and notice["id"].startswith("n"))
    call("POST", "/api/mailbox/read", {"id": notice["id"]}, tok)
    st, mails, _ = call("GET", "/api/mailbox", token=tok)
    check("공지 읽음 저장", next(m for m in mails if m["id"] == notice["id"])["read"] is True)

    print("[새 가입자 2명: 친구 요청 → 수락 → 초대 → 친선전 룸]")
    a = register("A")
    b = register("B")
    ta, tb = a["accessToken"], b["accessToken"]
    pa = call("GET", f"/api/players/{a['playerId']}")[1]
    check("새 가입자 레이팅 1000", pa["rating"] == 1000)
    st, mails, _ = call("GET", "/api/mailbox", token=ta)
    check("환영 우편 (보상 없음 §23.1)", any(m["title"] == "가입을 환영합니다!" and m["reward"] is None for m in mails))
    st, r, _ = call("POST", "/api/auth/register", {"email": "x" + a["email"], "password": "pass1234", "nickname": a["nickname"].upper()})
    check("닉네임 중복 가입 거부", st == 400)
    st, r, _ = call("POST", "/api/friends/requests", {"nickname": b["nickname"]}, ta)
    check("친구 요청 → 대기", st == 200 and r["status"] == "PENDING")
    st, r, _ = call("POST", "/api/friends/requests", {"nickname": b["nickname"]}, ta)
    check("같은 요청 두 번은 거부", st == 400)
    st, fr, _ = call("GET", "/api/friends?all=1", token=tb)
    check("받는 쪽은 incoming", any(f["nickname"] == a["nickname"] and f["relation"] == "incoming" for f in fr))
    st, r, _ = call("POST", "/api/mails/invite", {"friendId": b["playerId"], "roomCode": "ABC123"}, ta)
    check("친구 아니면 초대 거부", st == 400)
    st, r, _ = call("POST", "/api/friends/respond", {"playerId": a["playerId"], "accept": True}, tb)
    check("친구 수락", st == 200)
    st, room, _ = call("POST", "/api/rooms", {}, ta)
    code = room["code"]
    check("룸 생성 (6자리 코드)", st == 200 and len(code) == 6)
    st, r, _ = call("POST", "/api/mails/invite", {"friendId": b["playerId"], "roomCode": code}, ta)
    check("친구에게 초대 우편", st == 200)
    st, mails, _ = call("GET", "/api/mailbox", token=tb)
    inv = next(m for m in mails if m["kind"] == "invite")
    check("초대 우편에 룸 코드 · 보낸 사람", inv["roomCode"] == code and inv["from"] == a["nickname"])
    st, r, _ = call("POST", f"/api/rooms/{code}/start", {}, tb)
    check("게스트가 아닌 사람은 시작 못 함", st == 403)
    st, r, _ = call("POST", f"/api/rooms/{code}/join", {}, tb)
    check("코드로 입장", st == 200)
    call("POST", f"/api/rooms/{code}/ready?ready=true", {}, tb)
    st, info, _ = call("GET", f"/api/rooms/{code}")
    check("호스트가 보는 룸: 게스트 이름 · 준비", info["guestNickname"] == b["nickname"] and info["guest_ready"] == 1)
    st, r, _ = call("POST", f"/api/rooms/{code}/start", {}, ta)
    st, info, _ = call("GET", f"/api/rooms/{code}")
    check("호스트 시작 → PLAYING", st == 200 and info["status"] == "PLAYING")
    st, room2, _ = call("POST", "/api/rooms", {}, ta)
    call("POST", f"/api/rooms/{room2['code']}/join", {}, tb)
    call("POST", f"/api/rooms/{room2['code']}/kick", {}, ta)
    check("내보내기", call("GET", f"/api/rooms/{room2['code']}")[1]["guest_player_id"] is None)
    call("POST", f"/api/rooms/{room2['code']}/leave", {}, ta)
    check("호스트 나가기 → CLOSED", call("GET", f"/api/rooms/{room2['code']}")[1]["status"] == "CLOSED")
    wanted = "".join(random.choice("ABCDEFGHJKLMNPQRSTUVWXYZ23456789") for _ in range(6))
    st, room3, _ = call("POST", "/api/rooms", {"code": wanted}, ta)
    check("Unity 가 제안한 방 코드 사용", st == 200 and room3["code"] == wanted)
    st, room4, _ = call("POST", "/api/rooms", {"code": wanted}, tb)
    check("이미 있는 코드면 서버가 새 코드", st == 200 and room4["code"] != wanted and len(room4["code"]) == 6)
    call("POST", f"/api/rooms/{room3['code']}/leave", {}, ta)
    call("POST", f"/api/rooms/{room4['code']}/leave", {}, tb)
    st, r, _ = call("DELETE", f"/api/friends/player/{b['playerId']}", token=ta)
    st, fr, _ = call("GET", "/api/friends?all=1", token=ta)
    check("친구 삭제", all(f["nickname"] != b["nickname"] for f in fr))

    print("[랭킹전 매칭: 사람끼리 · 봇]")
    call("POST", "/api/matchmaking/queue", {}, ta)
    call("POST", "/api/matchmaking/queue", {}, tb)
    st, ma, _ = call("GET", "/api/matchmaking/status", token=ta)
    st, mb, _ = call("GET", "/api/matchmaking/status", token=tb)
    check("두 사람 서로 매칭", ma["state"] == "matched" and mb["state"] == "matched"
          and ma["opponentNickname"] == b["nickname"] and mb["opponentNickname"] == a["nickname"]
          and ma["roomCode"] == mb["roomCode"] and ma["bot"] is False)
    _, raw, _ = call("GET", "/api/matchmaking/status", token=ta, raw=True)
    check("JSON 공백 없음 (Unity 문자열 검사 호환)", b'"state":"' in raw)
    admin = login("admin@wow.dev", "admin1234")
    call("PUT", "/api/admin/configs/matchmaking.bot_timeout", {"value": "1"}, admin["accessToken"])
    call("POST", "/api/matchmaking/queue", {}, ta)
    time.sleep(1.2)
    st, ma, _ = call("GET", "/api/matchmaking/status", token=ta)
    check("혼자면 봇 매칭 (Bot (레이팅))", ma["state"] == "matched" and ma["bot"] is True and ma["opponentNickname"].startswith("Bot ("))
    st, _, _ = call("GET", "/api/admin/login-history")
    check("관리자 기록은 토큰 없이 못 봄", st == 403)

    print("[룬 그림 인식 · Unity 시전 신호]")
    rng = random.Random(5)
    for code in ("S01", "S06", "H05"):
        strokes = wow_runes.fake_drawing(code, rng)
        canvas = [[{"x": 100 + 80 * x, "y": 100 - 80 * y} for x, y in s] for s in strokes]   # y 아래로 + (웹 캔버스)
        st, r, _ = call("POST", "/api/runes/recognize", {"strokes": canvas, "candidates": deck + ["S01", "S06", "H05"], "yDown": True}, tok)
        check(f"룬 {code} 인식 ({wow_runes.SPELLS[code][1]})", st == 200 and r["result"]["spellId"] == code and r["result"]["recognized"],
              str(r))
    st, r, _ = call("POST", "/api/spells/cast", {"phase": "cast", "spellId": "S06", "column": 2, "slot": 3, "source": "test"}, tok)
    check("시전 신호 응답", st == 200 and r["message"]["circleImage"] == "/assets/skills/circle/S06.png")
    data, _ = unity.recvfrom(65535)
    msg = json.loads(data.decode("utf-8"))
    check("Unity UDP 수신: S06 번개 · 열 2 · 마법진 그림", msg["spellId"] == "S06" and msg["name"] == "번개"
          and msg["column"] == 2 and msg["spellNumber"] == 6 and msg["caster"] == demo["nickname"])
    st, r, _ = call("POST", "/api/spells/cast", {"phase": "cast", "spellId": "Z99"}, tok)
    check("없는 스킬 시전 거부", st == 400)
    st, recent, _ = call("GET", "/api/spells/recent")
    check("최근 시전 기록", recent["recent"][0]["spellId"] == "S06")


if __name__ == "__main__":
    main()
