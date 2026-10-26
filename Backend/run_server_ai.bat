@echo off
chcp 65001 > nul
title War of Wizards - Backend Server (AI: VLM 전략 코치 + 서버 MediaPipe)
cd /d "%~dp0"
rem 서버 본체는 표준 라이브러리만 쓴다. 이 실행기는 MediaPipe 손 추적기와 같은 가상환경(.venv)에서 서버를 띄워
rem  - /api/mediapipe/frame (카메라 프레임 → 손 추적)
rem  - /api/ai/strategy     (Claude VLM, ANTHROPIC_API_KEY 가 있을 때)
rem 을 켠다. 전역 파이썬에 mediapipe 를 깔면 numpy 2.x 로 올라가 다른 도구와 부딪히므로 가상환경을 쓴다.
set "VENV=%~dp0..\GestureTools\MediaPipe\.venv"
if exist "%VENV%\Scripts\python.exe" goto deps
echo [*] 처음 실행: AI 전용 가상환경을 만듭니다... (1~3분)
python -m venv "%VENV%"
if errorlevel 1 goto nopython
:deps
"%VENV%\Scripts\python.exe" -c "import mediapipe, anthropic" 2> nul
if not errorlevel 1 goto run
echo [*] MediaPipe · anthropic 패키지를 설치합니다...
"%VENV%\Scripts\python.exe" -m pip install --disable-pip-version-check -r "%~dp0..\GestureTools\MediaPipe\requirements.txt" -r "%~dp0requirements-ai.txt"
if errorlevel 1 goto fail
:run
echo ==============================================================
echo  War of Wizards 백엔드 서버 + AI (VLM 전략 코치 · 서버 MediaPipe)
echo ==============================================================
echo [*] 웹 브라우저 접속: http://localhost:8080/
if "%ANTHROPIC_API_KEY%"=="" echo [i] ANTHROPIC_API_KEY 가 없어 전략 코치는 규칙 AI 로 답합니다. (Claude: set ANTHROPIC_API_KEY=키 후 다시 실행)
echo [*] 종료하려면 이 창에서 Ctrl+C 를 누르세요.
echo.
"%VENV%\Scripts\python.exe" wow_server.py 8080
if errorlevel 1 pause
goto :eof
:nopython
echo [경고] python 명령을 찾을 수 없습니다. Python 3.10 ~ 3.12 를 설치하세요.
pause
goto :eof
:fail
echo [경고] 패키지 설치에 실패했습니다. 인터넷 연결을 확인한 뒤 다시 실행하세요. (AI 없이 쓰려면 run_server.bat)
pause
