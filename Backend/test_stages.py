# -*- coding: utf-8 -*-
"""
War of Wizards 8단계 로드맵 전용 파이썬 서버 순차 검증 테스트 스크립트
1단계부터 8단계까지 모든 엔드포인트를 단계별로 테스트합니다.
"""
import urllib.request
import json
import time
import sys
import math

try:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_URL = "http://localhost:8080"

def request_json(url, method="GET", data=None):
    body = json.dumps(data).encode("utf-8") if data else None
    headers = {"Content-Type": "application/json"} if data else {}
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as res:
            return res.status, json.loads(res.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read().decode("utf-8"))

def test_all_8_stages():
    print("=================================================================")
    print(" [8단계 순차 개발 로드맵] 파이썬 서버 단계별 전수 검증 시작")
    print("=================================================================")

    # 1단계 — Unity 기본 전투
    print("\n[1단계 검증] Unity 기본 전투 — 키보드 입력 및 스킬 설정")
    status, d = request_json(f"{BASE_URL}/api/stage1/combat-config")
    assert status == 200
    print(f" -> 1단계 통과: 조작키 매핑({d['inputControls']['moveLeft']}, {d['inputControls']['castSlots']}), 스킬 수 {len(d['skills'])}개")

    # 2단계 — Photon 방 연결
    print("\n[2단계 검증] Photon 방 연결 — 2인 접속 및 커스텀 인증")
    status, d = request_json(f"{BASE_URL}/api/stage2/photon/auth", method="POST", data={"userId": "vr_tester"})
    assert status == 200 and d["ResultCode"] == 1
    print(f" -> 2단계 통과: Photon 토큰 발급 완료 ({d['PhotonToken']})")

    # 3단계 — 백엔드 연동
    print("\n[3단계 검증] 백엔드 연동 — 회원가입, 로그인, 방 생성, 코드 입장")
    email = f"tester_{int(time.time())}@wow.dev"
    status, reg = request_json(f"{BASE_URL}/api/stage3/auth/register", method="POST", data={"email": email, "password": "pass", "nickname": "테스터1"})
    assert status == 200
    print(f" -> 3-1단계 통과: 회원가입 성공 (UserId: {reg['userId']}, Nickname: {reg['nickname']})")

    status, login = request_json(f"{BASE_URL}/api/stage3/auth/login", method="POST", data={"email": "demo@wow.dev", "password": "demo1234"})
    assert status == 200
    print(f" -> 3-2단계 통과: 데모 계정 로그인 성공 (Token: {login['accessToken']})")

    status, room = request_json(f"{BASE_URL}/api/stage3/rooms/create", method="POST", data={"nickname": "방장마법사"})
    assert status == 200
    room_code = room["roomCode"]
    print(f" -> 3-3단계 통과: 친선전 방 생성 성공 (RoomCode: {room_code})")

    status, join = request_json(f"{BASE_URL}/api/stage3/rooms/join", method="POST", data={"roomCode": room_code, "nickname": "도전자마법사"})
    assert status == 200
    print(f" -> 3-4단계 통과: 방 코드 입장 성공 ({join['message']})")

    # 4단계 — 대기실과 매칭
    print("\n[4단계 검증] 대기실과 매칭 — 참가자 목록, 준비 버튼, 퇴장")
    status, parts = request_json(f"{BASE_URL}/api/stage4/rooms/{room_code}/participants")
    assert status == 200 and len(parts["participants"]) == 2
    print(f" -> 4-1단계 통과: 대기실 참가자 2명 확인 (Host: {parts['participants'][0]['nickname']}, Guest: {parts['participants'][1]['nickname']})")

    status, ready = request_json(f"{BASE_URL}/api/stage4/rooms/ready", method="POST", data={"roomCode": room_code, "isReady": True})
    assert status == 200 and ready["isReady"] is True
    print(" -> 4-2단계 통과: 게스트 준비 완료 상태 동기화 성공")

    # 5단계 — 마법 이벤트 동기화
    print("\n[5단계 검증] 마법 이벤트 동기화 — 마법 시전 및 상대방 릴레이")
    cast_payload = {"roomCode": room_code, "spellId": "S03", "slotIndex": 2, "targetColumn": "Center", "targetDepth": 0.5, "caster": "방장마법사"}
    status, cast = request_json(f"{BASE_URL}/api/stage5/spells/cast", method="POST", data=cast_payload)
    assert status == 200
    print(f" -> 5단계 통과: 마법 시전 이벤트 브로드캐스트 성공 ({cast['event']['spellId']} -> 열 {cast['event']['targetColumn']})")

    # 6단계 — AI 손동작 인식
    print("\n[6단계 검증] AI 손동작 인식 — $P+ 룬 궤적 분류 추론")
    # 원형 궤적 (S01 / S03 형태)
    circle_pts = [{"x": math.cos(i*0.4), "y": math.sin(i*0.4), "z": 0} for i in range(16)]
    status, ai = request_json(f"{BASE_URL}/api/stage6/ai/recognize", method="POST", data={"points": circle_pts})
    assert status == 200 and "spellId" in ai["result"]
    print(f" -> 6단계 통과: AI 추론 성공! 인식 결과: [{ai['result']['spellId']}], 신뢰도: {ai['result']['confidence']*100:.1f}%")

    # 7단계 — 전투 판정과 전적 저장
    print("\n[7단계 검증] 전투 판정과 전적 저장 — 체력/승패 판정 및 랭킹 DB 기록")
    status, match = request_json(f"{BASE_URL}/api/stage7/combat/record-match", method="POST", data={"roomCode": room_code, "winner": "데모마법사", "loser": "도전자마법사", "durationSec": 145.0})
    assert status == 200
    print(f" -> 7-1단계 통과: 승패 판정 전적 저장 완료 (승: {match['match']['winner']}, 패: {match['match']['loser']})")

    status, ranks = request_json(f"{BASE_URL}/api/stage7/rankings")
    assert status == 200 and len(ranks["rankings"]) > 0
    print(f" -> 7-2단계 통과: 랭킹 리더보드 갱신 확인 (1위: {ranks['rankings'][0]['nickname']}, 점수: {ranks['rankings'][0]['rating']})")

    # 8단계 — 보안 및 배포
    print("\n[8단계 검증] 보안 및 배포 — 쿨타임 치트 거절 및 세션 복구")
    status, cheat = request_json(f"{BASE_URL}/api/stage8/security/validate", method="POST", data={"spellId": "H03", "cooldownLeft": 18.2})
    assert status == 400 and cheat["valid"] is False
    print(" -> 8-1단계 통과: 쿨타임 미경과 치트 시전 시도 정상 차단 성공!")

    status, normal = request_json(f"{BASE_URL}/api/stage8/security/validate", method="POST", data={"spellId": "S02", "cooldownLeft": 0.0})
    assert status == 200 and normal["valid"] is True
    print(" -> 8-2단계 통과: 정상 요청 서버 권한 통과 승인")

    status, rec = request_json(f"{BASE_URL}/api/stage8/session/recover", method="POST", data={"userId": 1})
    assert status == 200 and rec["recovered"] is True
    print(" -> 8-3단계 통과: 네트워크 단절 세션 복구 정상 처리")

    print("\n=================================================================")
    print(" [V] 축하합니다! 1단계부터 8단계까지 모든 로드맵 기능이 100% 정상 작동합니다!")
    print("=================================================================")

if __name__ == "__main__":
    test_all_8_stages()
