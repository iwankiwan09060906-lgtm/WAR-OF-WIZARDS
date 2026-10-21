@echo off
chcp 65001 > nul
title War of Wizards — Backend Server
cd /d "%~dp0"
echo ==============================================================
echo  War of Wizards (워 오브 위자드) 올인원 통합 백엔드 서버 실행기
echo ==============================================================
echo.
echo [*] 파이썬 백엔드 서버를 시작합니다... (포트: 8080)
echo [*] 웹 브라우저 접속: http://localhost:8080/
echo [*] 관리자 콘솔:     http://localhost:8080/admin.html
echo [*] 종료하려면 이 창에서 Ctrl+C 를 누르세요.
echo.
python wow_server.py 8080
if %errorlevel% neq 0 (
    echo.
    echo [경고] python 명령을 찾을 수 없거나 오류가 발생했습니다.
    echo Python 3.10 이상이 설치되어 있고 PATH에 등록되어 있는지 확인해주세요.
    pause
)
