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
import sqlite3
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
        with sqlite3.connect(db) as c:           # 진짜 서버 DB 에서 가져오지 않을 것: AI 학습 데이터(ai_import 로 모은 판) · Unity UDP 주소(검사용 주소를 쓴다)
            for sql in ("DELETE FROM ai_experiences", "DELETE FROM game_configs WHERE config_key = 'unity.spell_udp'"):
                try:
                    c.execute(sql)
                except sqlite3.OperationalError:
                    pass
    env = dict(os.environ, WOW_DB_FILE=db, WOW_UNITY_SPELL_UDP=f"127.0.0.1:{UNITY_PORT}", PYTHONIOENCODING="utf-8")
    env.pop("GOOGLE_CLIENT_ID", None)
    env["WOW_AI_DATA"] = os.path.join(tmp, "ai_data")       # 학습 데이터 · 화면 사진은 임시 폴더에
    for k in ("ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_PROFILE"):   # 검사 중 유료 VLM 호출 금지 (규칙 AI 로)
        env.pop(k, None)
    # WOW_TEST_PYTHON=GestureTools/MediaPipe/.venv/Scripts/python.exe 로 주면 카메라 프레임(MediaPipe)까지 검사한다
    proc = subprocess.Popen([os.environ.get("WOW_TEST_PYTHON") or sys.executable, os.path.join(HERE, "wow_server.py"), str(PORT)], env=env,
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
    if body.startswith(b"version https://git-lfs"):   # GitHub ZIP 으로 받으면 그림이 LFS 포인터 파일이다 (git lfs pull 필요)
        print("  --  스킬 마법진 그림: Git LFS 포인터 파일이라 건너뜀 (git lfs pull 로 실제 그림을 받으세요)")
    else:
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
    # Unity 기기 로그인용 클라이언트 ID 가 서버에 늘 들어 있어 400(설정 없음)이 아니라 가짜 토큰 거부(401 · 502)가 나온다
    st, r, _ = call("POST", "/api/auth/google", {"accessToken": "x"})
    check("가짜 Google 토큰은 로그인 안 됨", st in (400, 401, 502) and "accessToken" not in r)
    demo = login("demo@wow.dev", "demo1234")
    tok = demo["accessToken"]
    st, me, _ = call("GET", "/api/auth/me", token=tok)
    check("세션 복구 /api/auth/me", st == 200 and me["playerId"] == demo["playerId"])
    groups = [call("POST", "/api/auth/login", {"email": f"{i}조", "password": str(i) * 4})[1] for i in range(1, 5)]
    check("공식 팀 계정 1조/1111 ~ 4조/4444 아이디로 로그인", all(g.get("nickname") == f"{i}조" and g.get("official") is True
                                                         for i, g in enumerate(groups, 1)), str(groups[0]))
    st, r, _ = call("POST", "/api/auth/login", {"email": "group3@wow.dev", "password": "3333"})
    check("Unity 로그인 화면용 이메일 group3@wow.dev 로도 로그인", st == 200 and r["nickname"] == "3조")
    st, r, _ = call("POST", "/api/auth/login", {"id": "2조", "password": "2222"})
    check("{id} 로도 로그인", st == 200 and r["nickname"] == "2조")
    st, r, _ = call("POST", "/api/auth/login", {"email": "1조", "password": "2222"})
    check("다른 조 비밀번호는 거부", st == 400)
    fr = call("GET", "/api/friends", token=groups[0]["accessToken"])[1]
    check("1조는 2조 · 3조 · 4조와 처음부터 친구", sorted(f["nickname"] for f in fr) == ["2조", "3조", "4조"])
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
    st, room, _ = call("POST", "/api/rooms", {"players": 2}, ta)
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
    st, room2, _ = call("POST", "/api/rooms", {"players": 2}, ta)
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
    st, room5, _ = call("POST", "/api/rooms", {}, ta)
    check("인원을 안 정하면 4인 개인전 방 (match.default_players)", st == 200 and room5["maxPlayers"] == 4 and room5["format"] == "ffa4", str(room5))
    call("POST", f"/api/rooms/{room5['code']}/leave", {}, ta)
    st, r, _ = call("DELETE", f"/api/friends/player/{b['playerId']}", token=ta)
    st, fr, _ = call("GET", "/api/friends?all=1", token=ta)
    check("친구 삭제", all(f["nickname"] != b["nickname"] for f in fr))

    print("[랭킹전 매칭: 사람끼리 · 봇]")
    call("POST", "/api/matchmaking/queue", {"players": 2}, ta)
    call("POST", "/api/matchmaking/queue", {"players": 2}, tb)
    st, ma, _ = call("GET", "/api/matchmaking/status", token=ta)
    st, mb, _ = call("GET", "/api/matchmaking/status", token=tb)
    check("두 사람 서로 매칭", ma["state"] == "matched" and mb["state"] == "matched"
          and ma["opponentNickname"] == b["nickname"] and mb["opponentNickname"] == a["nickname"]
          and ma["roomCode"] == mb["roomCode"] and ma["bot"] is False)
    _, raw, _ = call("GET", "/api/matchmaking/status", token=ta, raw=True)
    check("JSON 공백 없음 (Unity 문자열 검사 호환)", b'"state":"' in raw)
    admin = login("admin@wow.dev", "admin1234")
    call("PUT", "/api/admin/configs/matchmaking.bot_timeout", {"value": "1"}, admin["accessToken"])
    call("POST", "/api/matchmaking/queue", {"players": 2}, ta)
    time.sleep(1.2)
    st, ma, _ = call("GET", "/api/matchmaking/status", token=ta)
    check("혼자면 봇 매칭 (Bot (레이팅))", ma["state"] == "matched" and ma["bot"] is True and ma["opponentNickname"].startswith("Bot ("))
    st, _, _ = call("GET", "/api/admin/login-history")
    check("관리자 기록은 토큰 없이 못 봄", st == 403)

    ffa_checks(admin["accessToken"])
    # auto 는 로컬 VLM 서버(8790)가 켜져 있으면 그걸 쓴다 → 검사는 이 PC 의 llama-server 와 상관없게 닿지 않는 주소로
    call("PUT", "/api/admin/configs/ai.local_url", {"value": "http://127.0.0.1:9/v1"}, admin["accessToken"])
    ai_checks(tok)
    learn_checks(tok, admin["accessToken"])
    unity_link_checks()

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


def ffa_checks(admin_tok):
    print("[개인전 2~4명: 매칭 큐 · 게임 방 · 친선전 방]")
    ps = [register(f"개인{i}") for i in range(5)]
    tk = [p["accessToken"] for p in ps]
    call("PUT", "/api/admin/configs/matchmaking.bot_timeout", {"value": "30"}, admin_tok)
    st, r, _ = call("POST", "/api/matchmaking/queue", {"players": 4}, tk[0])
    check("4인 개인전 큐 등록", st == 200 and r["maxPlayers"] == 4 and r["playersNeeded"] == 4 and r["format"] == "ffa4")
    st, s0, _ = call("GET", "/api/matchmaking/status", token=tk[0])
    check("혼자면 4인전 탐색 중 (1/4명)", s0["state"] == "searching" and s0["playersFound"] == 1 and s0["playersNeeded"] == 4)
    call("POST", "/api/matchmaking/queue", {"players": 2}, tk[4])                       # 1:1 큐 사람은 4인전에 섞이지 않는다
    for t in tk[1:4]:
        call("POST", "/api/matchmaking/queue", {"players": 4}, t)
    ms = [call("GET", "/api/matchmaking/status", token=t)[1] for t in tk[:4]]
    check("네 사람 4인 개인전 매칭 (같은 게임 방 · 봇 없음)", all(m["state"] == "matched" and m["maxPlayers"] == 4 for m in ms)
          and len({m["matchId"] for m in ms}) == 1 and all(m["bot"] is False for m in ms))
    check("팀 없음: 각자 상대 3명 · 자리 0~3", all(len(m["opponents"]) == 3 and "allies" not in m for m in ms)
          and sorted(m["seat"] for m in ms) == [0, 1, 2, 3])
    check("1:1 큐 사람은 4인전에 안 들어감", call("GET", "/api/matchmaking/status", token=tk[4])[1]["state"] == "searching")
    call("DELETE", "/api/matchmaking/queue", token=tk[4])
    mid = ms[0]["matchId"]
    cur = call("GET", "/api/match/current", token=tk[0])[1]["match"]
    check("게임 방 보기: 4자리 · 상대 이름 'A · B · C'", cur["maxPlayers"] == 4 and len(cur["players"]) == 4
          and cur["opponent"]["nickname"].count(" · ") == 2 and len(cur["opponents"]) == 3)
    for t in tk[:3]:
        call("POST", f"/api/match/{mid}/join", {}, t)
    check("4명 중 3명 접속 → 아직 MATCHED", call("GET", "/api/match/current", token=tk[0])[1]["match"]["status"] == "MATCHED")
    call("POST", f"/api/match/{mid}/join", {}, tk[3])
    check("4명 모두 접속 → PLAYING", call("GET", "/api/match/current", token=tk[0])[1]["match"]["status"] == "PLAYING")
    deltas = []
    for place, t in enumerate(tk[:4], start=1):
        st, r, _ = call("POST", f"/api/match/{mid}/result", {"result": "WIN" if place == 1 else "LOSE", "place": place}, t)
        deltas.append(r["match"]["ratingDelta"])
        if place == 3:
            check("3명 보고 → 아직 PLAYING", r["match"]["status"] == "PLAYING")
    check("4명 보고 → ENDED, 등수로 레이팅 (+16 · +5 · -5 · -16)", r["match"]["status"] == "ENDED" and deltas == [16, 5, -5, -16], str(deltas))

    call("PUT", "/api/admin/configs/matchmaking.bot_timeout", {"value": "1"}, admin_tok)
    for t in tk[:2]:
        call("POST", "/api/matchmaking/queue", {"players": 3}, t)
    time.sleep(1.2)
    m0 = call("GET", "/api/matchmaking/status", token=tk[0])[1]
    m1 = call("GET", "/api/matchmaking/status", token=tk[1])[1]
    check("3인전에 2명뿐이면 시간 초과 뒤 빈자리 1개를 봇으로", m0["state"] == "matched" and m1["matchId"] == m0["matchId"]
          and m0["maxPlayers"] == 3 and sum(1 for x in m0["opponents"] if x["bot"]) == 1 and m0["bot"] is False)
    mid2 = m0["matchId"]
    for t in tk[:2]:
        call("POST", f"/api/match/{mid2}/join", {}, t)
    r = call("POST", f"/api/match/{mid2}/leave", {}, tk[0])[1]["match"]
    check("개인전 경기 중 한 명이 나가도 경기는 계속", r["status"] == "PLAYING")
    r = call("POST", f"/api/match/{mid2}/result", {"result": "WIN", "place": 1}, tk[1])[1]["match"]
    check("남은 사람이 보고하면 ENDED (3인전 1등 +16)", r["status"] == "ENDED" and r["ratingDelta"] == 16)

    st, room, _ = call("POST", "/api/rooms", {"players": 4}, tk[0])
    code = room["code"]
    check("4인 친선전 방", st == 200 and room["maxPlayers"] == 4)
    joined = [call("POST", f"/api/rooms/{code}/join", {}, t)[0] for t in tk[1:4]]
    st, r, _ = call("POST", f"/api/rooms/{code}/join", {}, tk[4])
    check("3명 입장 · 5번째 사람은 꽉 참", joined == [200, 200, 200] and st == 400)
    st, r, _ = call("POST", f"/api/rooms/{code}/start", {}, tk[0])
    check("준비 안 한 사람이 있으면 시작 못 함", st == 400)
    for t in tk[1:4]:
        call("POST", f"/api/rooms/{code}/ready", {"ready": True}, t)
    info = call("GET", f"/api/rooms/{code}")[1]
    check("방 자리 표 4명 (들어온 순서) · 첫 게스트 호환 칸", [m["seat"] for m in info["members"]] == [0, 1, 2, 3]
          and info["members"][0]["host"] and info["guestNickname"] == ps[1]["nickname"])
    st, r, _ = call("POST", f"/api/rooms/{code}/start", {}, tk[0])
    check("4인 친선전 시작 → 게임 방", st == 200 and r["matchId"] > 0)
    cur = call("GET", "/api/match/current", token=tk[3])[1]["match"]
    check("게스트도 같은 친선전 게임 방", cur["matchId"] == r["matchId"] and cur["mode"] == "friendly" and len(cur["players"]) == 4)

    st, room, _ = call("POST", "/api/rooms", {"players": 3}, tk[0])
    code = room["code"]
    call("POST", f"/api/rooms/{code}/join", {}, tk[1])
    st, r, _ = call("POST", f"/api/rooms/{code}/bot", {}, tk[0])
    check("방장이 빈자리에 봇", st == 200)
    st, r, _ = call("POST", f"/api/rooms/{code}/bot", {}, tk[1])
    check("게스트는 봇 못 채움", st == 403)
    st, r, _ = call("POST", f"/api/rooms/{code}/join", {}, tk[2])
    members = call("GET", f"/api/rooms/{code}")[1]["members"]
    check("꽉 찬 방에 사람이 오면 봇 자리를 내줌", st == 200 and len(members) == 3 and not any(m["bot"] for m in members))
    call("POST", f"/api/rooms/{code}/kick", {"playerId": ps[2]["playerId"]}, tk[0])
    check("사람 내보내기 (playerId)", len(call("GET", f"/api/rooms/{code}")[1]["members"]) == 2)
    call("POST", f"/api/rooms/{code}/ready", {"ready": True}, tk[1])
    st, r, _ = call("POST", f"/api/rooms/{code}/start", {}, tk[0])
    cur = call("GET", "/api/match/current", token=tk[0])[1]["match"]
    check("빈자리는 시작할 때 봇으로", len(cur["players"]) == 3 and sum(1 for m in cur["players"] if m["bot"]) == 1)


TINY_JPEG = ("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAA0JCgsKCA0LCgsODg0PEyAVExISEyccHhcgLikxMC4pLSwzOko+MzZGNywtQFdBRkxOUlNSMj5aYVpQYEpRUk//"
             "2wBDAQ4ODhMREyYVFSZPNS01T09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT0//wAARCAAwAEADASIAAhEBAxEB/8QAHwAA"
             "AQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRol"
             "JicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW"
             "19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdh"
             "cRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJma"
             "oqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDIooooAKKKKACiiigAooooAKKKKACiiigA"
             "ooooAKKKKACiiigAooooAKKKKACiiigD/9k=")


def ai_checks(tok):
    print("[AI 전략 코치 (VLM · 규칙) · 서버 MediaPipe 손 추적]")
    st, s, _ = call("GET", "/api/ai/status")
    check("AI 상태: 키 없으면 규칙 AI", st == 200 and s["vlm"]["provider"] == "rules" and s["mediapipe"]["landmarks"]["available"])
    obs = {"SelfHp": 820, "SelfColumn": 1, "MatchTimeSeconds": 75, "SelfSlotReadyMask": 0b1011, "OpponentTowerAlive": True,
           "deck": ["S03", "S04", "S08", "H01"], "IncomingProjectileCount": 1, "NearestIncomingProjectileColumn": 1,
           "NearestIncomingProjectileEta": 0.8,
           "lanes": [{"allies": 1, "enemies": 4}, {"allies": 2, "enemies": 1}, {"allies": 0, "enemies": 0}]}
    st, r, _ = call("POST", "/api/ai/strategy", {"state": obs})
    check("전략은 로그인 필요", st == 401)
    st, r, _ = call("POST", "/api/ai/strategy", {"state": obs}, tok)
    check("Unity BotObservation 그대로 → 피하고 적이 몰린 왼쪽에 화살 세례", st == 200 and r["source"] == "rules"
          and r["move"]["direction"] == -1 and r["cast"]["spellId"] == "S04" and r["cast"]["column"] == 0, str(r))
    check("못 쓰는 슬롯(2번 쉴드)은 고르지 않음", 2 not in r["state"]["readySlots"] and r["cast"]["slot"] != 2)
    ffa = dict(obs, players=4, opponents=[{"nickname": "Kael", "hp": 900, "column": 0}, {"nickname": "Nyx", "hp": 300, "column": 2, "towerAlive": False},
                                          {"nickname": "Bot (990)", "hp": 600, "column": 1}])
    req = urllib.request.Request(BASE + "/api/ai/strategy", data=json.dumps({"state": ffa, "botId": 7}).encode(),
                                 headers={"Content-Type": "application/json", "X-Server-Key": "wow-server-secret-key-2026"}, method="POST")
    with urllib.request.urlopen(req, timeout=10) as resp:
        r = json.loads(resp.read())
    check("Unity 서버 키로 봇 전략 · 4인 개인전은 타워가 무너진 상대를 노림", r["cast"]["slot"] >= 0 and r["focus"].startswith("Nyx"), r["focus"])
    st, r, _ = call("POST", "/api/ai/strategy", {"state": obs, "image": "data:image/png;base64,@@@"}, tok)
    check("잘못된 그림은 400", st == 400)

    st, hs, _ = call("GET", "/api/mediapipe/status")
    check("MediaPipe 상태 (랜드마크 입력은 항상)", st == 200 and hs["landmarks"]["available"] is True)
    st, r, _ = call("POST", "/api/mediapipe/frame", {"hands": {}})
    check("손 추적은 로그인 필요", st == 401)
    st, r, _ = call("POST", "/api/mediapipe/frame", {}, tok)
    check("image · hands 없으면 400", st == 400)
    from test_hand_pipeline import make_hand
    clock = [0.0]
    events = []

    def send(lm, n=1):
        for _ in range(n):
            body = {"hands": {"right": [list(p) for p in lm] if lm else None}, "t": clock[0], "session": "test",
                    "candidates": ["S03", "S06", "S07"]}
            st, out, _ = call("POST", "/api/mediapipe/frame", body, tok)
            assert st == 200, (st, out)
            events.extend(out["events"])
            clock[0] += 33.3
        return out

    call("POST", "/api/mediapipe/reset", {"session": "test"}, tok)
    send(make_hand("open"), 12)
    bolt = wow_runes.fake_drawing("S06", random.Random(5))[0]          # 번개 룬 한 획을 손(핀치)으로 따라 그린다
    cx, cy = sum(p[0] for p in bolt) / len(bolt), sum(p[1] for p in bolt) / len(bolt)
    for x, y in bolt:
        last = send(make_hand("pinch", (x - cx) * 0.25, -(y - cy) * 0.25))
    check("그리는 중 커서 · drawing 표시", last["right"]["drawing"] is True and len(last["right"]["landmarks"]) == 63)
    send(make_hand("open"), 10)
    send(make_hand("fist"), 6)
    send(make_hand("point", dx=0.30), 6)
    names = [e["name"] for e in events]
    end = next((e for e in events if e["name"] == "pinch_end"), {})
    check("서버 손 추적: 핀치로 번개 룬 → S06", end.get("rune", {}).get("spellId") == "S06" and end["rune"]["recognized"], str(names))
    check("주먹 → 가리키기 (오른쪽 열)", "fist" in names and next(e for e in events if e["name"] == "point")["column"] == 2)
    st, r, _ = call("POST", "/api/mediapipe/frame", {"image": "data:image/jpeg;base64," + TINY_JPEG, "session": "img"}, tok)
    if hs["image"]["available"]:
        check("카메라 프레임 → MediaPipe (손 없음)", st == 200 and r["source"] == "image" and r["right"]["tracked"] is False, str(r))
    else:
        check("mediapipe 없는 파이썬이면 프레임은 503 + 안내", st == 503 and "run_server_ai.bat" in r["message"], str(r))


def learn_checks(tok, admin_tok):
    print("[AI 학습: 경험 모으기 · 결과 이름표 · 학습 · 데이터셋 · 학습한 VLM]")
    st, L, _ = call("GET", "/api/ai/learning")
    check("전략 요청도 경험으로 저장 (advice)", st == 200 and L["experiences"] >= 1 and L["policy"]["trained"] is False)
    scene = {"deck": ["S03", "S04", "S06", "S08"], "ready": [True, True, True, True], "selfHp": 900, "selfColumn": 1, "time": 90,
             "lanes": [{"allies": 1, "enemies": 4}, {"allies": 1, "enemies": 1}, {"allies": 0, "enemies": 0}]}
    for g in range(12):                      # 번개(S06)를 쓴 판은 이기고, 화살 세례(S04)를 쓴 판은 진다
        win = g % 2 == 0
        game = f"learn{g}"
        for k in range(2):
            body = {"gameId": game, "source": "test", "state": scene,
                    "action": {"spellId": "S06" if win else "S04", "slot": 2 if win else 1, "column": 0}}
            if win and g == 0 and k == 0:
                body["image"] = "data:image/jpeg;base64," + TINY_JPEG
            st, r, _ = call("POST", "/api/ai/experience", body, tok)
            assert st == 200 and r["recorded"] == 1, (st, r)
        st, r, _ = call("POST", "/api/ai/experience", {"gameId": game, "outcome": "WIN" if win else "LOSE"}, tok)
        assert r["labeled"] == 2, r
    st, L, _ = call("GET", "/api/ai/learning")
    check("경험 · 결과 이름표 · 화면 사진 저장", L["labeled"] >= 24 and L["frames"] >= 1 and L["games"] >= 12, str(L))
    st, r, _ = call("POST", "/api/ai/train", {}, tok)
    check("학습은 관리자만", st == 403)
    st, r, _ = call("POST", "/api/ai/train", {}, admin_tok)
    check("학습: 12판 · 이긴 스킬이 위로", st == 200 and r["trained"] and r["games"] >= 12 and r["topSpells"][0]["spellId"] == "S06", str(r))
    st, r, _ = call("POST", "/api/ai/strategy", {"state": scene, "record": False}, tok)
    check("규칙 AI 가 학습 결과로 수를 바꿈 (화살 세례 → 번개)", r["cast"]["spellId"] == "S06" and r["learned"]["confident"]
          and "학습" in r["cast"]["reason"], str(r["cast"]))
    st, r, _ = call("POST", "/api/ai/dataset", {}, admin_tok)
    check("VLM 미세조정 데이터셋 (이긴 판만 · 그림 포함)", st == 200 and r["lines"] >= 12 and r["withImage"] >= 1, str(r))
    with open(os.path.join(r["dir"], "wow_vlm_sft.jsonl"), encoding="utf-8") as f:
        first = json.loads(f.readline())
    with open(os.path.join(r["dir"], "dataset_info.json"), encoding="utf-8") as f:
        info = json.load(f)
    check("데이터셋 형식 (system · user · assistant JSON · <image> · dataset_info)",
          [m["role"] for m in first["messages"]] == ["system", "user", "assistant"]
          and json.loads(first["messages"][2]["content"])["cast"]["spellId"] == "S06"
          and info["wow_vlm"]["formatting"] == "sharegpt" and (first.get("images") is None or first["messages"][1]["content"].startswith("<image>")),
          first["messages"][2]["content"][:200] + " | " + str(first.get("images")) + " | " + first["messages"][1]["content"][:60])
    call("POST", "/api/ai/experience", {"gameId": "legacy1", "state": scene, "action": {"spellId": "S03", "slot": 0, "column": 1}}, tok)
    before = call("GET", "/api/ai/learning")[1]["labeled"]
    call("POST", "/api/matches", {"mode": "bot", "result": "WIN", "opponent": "Bot", "gameId": "legacy1"}, tok)
    check("봇 연습 결과(/api/matches + gameId)도 그 판 경험에 이름표", call("GET", "/api/ai/learning")[1]["labeled"] == before + 1)

    # 이 게임으로 미세조정한 VLM 자리 (OpenAI 호환 서버) — 가짜 서버로 요청 · 응답 모양 확인
    import threading
    from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
    seen = {}

    class FakeVlm(BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def do_POST(self):
            seen["path"] = self.path
            seen["body"] = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
            answer = {"summary": "왼쪽 레인 정리", "threat": "medium", "plan": ["번개 왼쪽"], "focus": "", "observations": [],
                      "cast": {"slot": 2, "spellId": "S06", "column": 0, "depth": 0.4, "reason": "학습한 모델"},
                      "move": {"direction": 0, "reason": ""}}
            data = json.dumps({"model": "wow-vlm", "choices": [{"message": {"role": "assistant",
                                                                            "content": "```json\n" + json.dumps(answer, ensure_ascii=False) + "\n```"}}]}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

    fake = ThreadingHTTPServer(("127.0.0.1", 0), FakeVlm)
    threading.Thread(target=fake.serve_forever, daemon=True).start()
    call("PUT", "/api/admin/configs/ai.local_url", {"value": f"http://127.0.0.1:{fake.server_port}/v1"}, admin_tok)
    st, r, _ = call("POST", "/api/ai/strategy", {"state": scene, "provider": "local", "image": "data:image/jpeg;base64," + TINY_JPEG,
                                                 "record": False}, tok)
    user_content = seen.get("body", {}).get("messages", [{}, {}])[1].get("content", [])
    check("학습한 VLM(local): /v1/chat/completions · 그림 · 학습 결과를 프롬프트에", seen.get("path") == "/v1/chat/completions"
          and user_content[0]["type"] == "image_url" and "learned" in user_content[1]["text"], str(seen.get("path")))
    check("학습한 VLM 답 사용", st == 200 and r["source"] == "local" and r["cast"]["spellId"] == "S06" and r["usedImage"], str(r.get("fallbackReason")))
    # 하이브리드 봇: 사진 없이 → 전장 지도를 그려 VLM 에 · 짧은 답(brief) 스키마 · 답에 작전(orders)이 없으면 힌트 작전
    bot_scene = dict(scene, units=[{"c": 0, "d": 0.3, "e": 1}, {"c": 0, "d": 0.35, "e": 1}, {"c": 1, "d": 0.4, "e": 0}],
                     botPick=[{"slot": 2, "spellId": "S06", "column": 0, "depth": 0.3, "score": 3.2, "why": "S06 왼쪽 레인"}])
    call("PUT", "/api/admin/configs/ai.vlm_min_interval", {"value": "0"}, admin_tok)      # 바로 앞 요청과 3초 간격 제한 끄기
    st, r, _ = call("POST", "/api/ai/strategy", {"state": bot_scene, "provider": "local", "brief": True, "record": False}, tok)
    body = seen.get("body", {})
    img = body.get("messages", [{}, {}])[1].get("content", [{}])[0]
    fmt = body.get("response_format", {}).get("json_schema", {}).get("schema", {})
    check("봇 요청: 전장 지도(PNG) · brief 스키마(작전 포함) · botHint 를 프롬프트에",
          img.get("type") == "image_url" and img["image_url"]["url"].startswith("data:image/png;base64,") and r.get("imageKind") == "board"
          and set(fmt.get("required", [])) == {"threat", "orders", "cast", "move"} and "botHint" in body["messages"][1]["content"][1]["text"], str(fmt))
    check("작전(orders) 응답 — VLM 이 안 주면 힌트 작전", st == 200 and r["orders"]["stance"] in ("defend", "balanced", "push")
          and isinstance(r["orders"]["prefer"], list) and 3 <= r["orders"]["seconds"] <= 15, str(r.get("orders")))
    fake.shutdown()


def unity_link_checks():
    """UnityClient/Assets/_Project/Backend/AiStrategyLink.cs 가 JsonUtility 로 만드는 모양 그대로 보낸다."""
    print("[Unity AI 봇 연결 (AiStrategyLink 형식)]")
    key = {"X-Server-Key": "wow-server-secret-key-2026"}

    def post(path, body):
        req = urllib.request.Request(BASE + path, data=json.dumps(body).encode(), method="POST",
                                     headers=dict(key, **{"Content-Type": "application/json"}))
        with urllib.request.urlopen(req, timeout=10) as r:
            return json.loads(r.read())

    state = {"deck": ["S03", "S04", "S06", "S08", "S11", "S13", "H01", "H02"], "ready": [True, True, False, True, True, True, False, True],
             "selfHp": 640, "opponentHp": 900, "selfColumn": 1, "opponentColumn": 0, "time": 95.5,
             "selfTowerAlive": True, "selfNexusAlive": True, "opponentTowerAlive": True, "opponentNexusAlive": True,
             "incomingProjectileCount": 0, "nearestIncomingProjectileColumn": 1, "nearestIncomingProjectileEta": -1.0,
             "lanes": [{"allies": 1, "enemies": 5, "enemyFront": 0.3, "clusterDepth": 0.35},
                       {"allies": 2, "enemies": 1, "enemyFront": 0.6, "clusterDepth": 0.6},
                       {"allies": 0, "enemies": 0, "enemyFront": 1.0, "clusterDepth": 0.5}], "players": 2, "mode": "bot"}
    r = post("/api/ai/strategy", {"state": state, "botId": "a1b2c3d4-Home", "gameId": "bot-a1b2c3d4-1", "record": False})
    check("봇 전략 요청 → cast(slot · spellId · column) · move 정수", isinstance(r["cast"]["slot"], int) and isinstance(r["move"]["direction"], int)
          and (r["cast"]["slot"] < 0 or state["ready"][r["cast"]["slot"]]), str(r["cast"]))
    hybrid = dict(state, units=[{"c": 0, "d": 0.3, "e": 1}] * 5 + [{"c": 1, "d": 0.5, "e": 0}],
                  botPick=[{"slot": 4, "spellId": "S11", "column": 0, "depth": 0.2, "score": 3.8, "why": "S11 왼쪽 레인 보강"},
                           {"slot": 2, "spellId": "S06", "column": 0, "depth": 0.3, "score": 2.0, "why": "쿨타임 중"}])
    r = post("/api/ai/strategy", {"state": hybrid, "botId": "a1b2c3d4-Home", "gameId": "bot-a1b2c3d4-1", "record": False, "brief": True})
    check("하이브리드: VLM 없으면 Unity 두뇌 1순위가 한 수 · 작전에 먼저 쓸 스킬로", r["cast"]["spellId"] == "S11" and r["botHinted"]
          and r["orders"]["prefer"][:1] == ["S11"] and r["orders"]["lane"] == 0, str(r["cast"]) + str(r["orders"]))
    steps = [{"state": state, "action": {"spellId": "S11", "slot": 4, "column": 0, "depth": 0.2, "move": 0, "followedAdvice": False}, "reward": 0},
             {"state": state, "action": {"spellId": "", "slot": -1, "column": -1, "depth": 0, "move": -1, "followedAdvice": False}, "reward": 0}]
    r = post("/api/ai/experience", {"gameId": "bot-a1b2c3d4-1", "botId": "a1b2c3d4-Home", "source": "unity-bot", "steps": steps})
    check("봇 판단 묶음 기록 (시전 · 이동)", r["recorded"] == 2)
    r = post("/api/ai/experience", {"gameId": "bot-a1b2c3d4-1", "botId": "a1b2c3d4-Home", "outcome": "WIN", "place": 1, "players": 2})
    check("경기 결과 → 그 봇 판의 경험에 이름표", r["labeled"] == 2)


if __name__ == "__main__":
    main()
