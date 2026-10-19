@echo off
chcp 65001 > nul
title War of Wizards — 8-Stage Roadmap Python Server
echo ==============================================================
echo  War of Wizards (워 오브 위자드) 8단계 로드맵 전용 파이썬 서버
echo ==============================================================
echo.
echo [*] 8단계 개발 로드맵 서버를 시작합니다... (포트: 8080)
echo [*] 8단계 대시보드 웹 접속: http://localhost:8080/stages.html
echo [*] 종료하려면 이 창에서 Ctrl+C 를 누르세요.
echo.
python stage_server.py 8080
if %errorlevel% neq 0 (
    echo.
    echo [경고] python 명령을 찾을 수 없거나 오류가 발생했습니다.
    pause
)
