# -*- coding: utf-8 -*-
"""
War of Wizards 단일 파일 백엔드 서버 기능 검증 테스트 스크립트
"""
import urllib.request
import json
import time
import sys

try:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_URL = "http://localhost:8080"

def test_api():
    print("[1] 헬스 체크 테스트...")
    req = urllib.request.urlopen(f"{BASE_URL}/api/health")
    res = json.loads(req.read().decode("utf-8"))
    assert res["status"] == "UP"
    print("    -> 헬스 체크 정상:", res["server"])

    print("[2] 데모 계정 로그인 테스트...")
    login_data = json.dumps({"email": "demo@wow.dev", "password": "demo1234"}).encode("utf-8")
    req = urllib.request.Request(f"{BASE_URL}/api/auth/login", data=login_data, headers={"Content-Type": "application/json"})
    res = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
    token = res["accessToken"]
    print(f"    -> 로그인 성공! 닉네임: {res['nickname']}, 토큰 획득 완료")

    print("[3] 19종 스킬 목록 조회 테스트...")
    req = urllib.request.urlopen(f"{BASE_URL}/api/skills")
    skills = json.loads(req.read().decode("utf-8"))
    assert len(skills) == 19
    print(f"    -> 19종 스킬 정상 로드됨! (첫 번째: {skills[0]['name']}, 마지막: {skills[-1]['name']})")

    print("[4] 8슬롯 덱 조회 테스트...")
    req = urllib.request.Request(f"{BASE_URL}/api/decks/me", headers={"Authorization": f"Bearer {token}"})
    deck = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
    assert len(deck["skills"]) == 8
    print(f"    -> 8슬롯 덱 확인: {deck['skills']}")

    print("[5] 관리자 대시보드 테스트...")
    admin_login = json.dumps({"email": "admin@wow.dev", "password": "admin1234"}).encode("utf-8")
    req = urllib.request.Request(f"{BASE_URL}/api/auth/login", data=admin_login, headers={"Content-Type": "application/json"})
    admin_res = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
    admin_token = admin_res["accessToken"]

    req = urllib.request.Request(f"{BASE_URL}/api/admin/dashboard", headers={"Authorization": f"Bearer {admin_token}"})
    dash = json.loads(urllib.request.urlopen(req).read().decode("utf-8"))
    print(f"    -> 대시보드 조회 성공: 총 유저 {dash['totalUsers']}명, DB 상태: {dash['dbStatus']}")

    print("\n[V] 모든 핵심 API 테스트 통과! 백엔드 서버가 완벽하게 작동합니다!")

if __name__ == "__main__":
    test_api()
