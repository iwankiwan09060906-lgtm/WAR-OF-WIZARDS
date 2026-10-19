@echo off
chcp 65001 > nul
title War of Wizards - MediaPipe Hand Tracker
cd /d "%~dp0"
if exist ".venv\Scripts\python.exe" goto run
echo [*] 처음 실행: MediaPipe 전용 가상환경(.venv)을 만듭니다... (1~3분)
python -m venv .venv
if errorlevel 1 goto nopython
".venv\Scripts\python.exe" -m pip install --disable-pip-version-check -r requirements.txt
if errorlevel 1 goto fail
:run
echo ==============================================================
echo  War of Wizards - MediaPipe 손 추적기 (6단계 AI 손동작 인식)
echo ==============================================================
echo [*] 웹캠 손 추적 결과를 Unity 로 보냅니다: UDP 127.0.0.1:5065
echo [*] 종료: 미리보기 창에서 q   /   옵션 예: run_hand_tracker.bat --camera 1
echo.
".venv\Scripts\python.exe" wow_hand_tracker.py %*
if errorlevel 1 pause
goto :eof
:nopython
echo [경고] python 명령을 찾을 수 없습니다. Python 3.10 ~ 3.12 를 설치하세요.
pause
goto :eof
:fail
echo [경고] MediaPipe 설치에 실패했습니다. 인터넷 연결을 확인한 뒤 .venv 폴더를 지우고 다시 실행하세요.
pause
