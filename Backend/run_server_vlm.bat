@echo off
chcp 65001 > nul
setlocal
title War of Wizards - Backend Server + 로컬 VLM (하이브리드 봇)
cd /d "%~dp0"
rem ================================================================
rem  게임 서버 + 로컬 VLM(llama.cpp · Qwen2.5-VL-3B)을 같이 켠다.
rem   - VLM: http://127.0.0.1:8790/v1 (이 PC 에서만). 게임 서버의 ai.vlm_provider = auto 면 켜진 것을 보고 자동으로 쓴다
rem     (Unity 봇 · BotLab 봇의 작전 · 한 수, 웹 AI 코치). 꺼져 있으면 규칙 AI 로 답한다.
rem   - 봇 3개(4인 개인전)까지 동시에 답한다: --parallel 3
rem  다른 곳에 받았으면: set WOW_LLAMA_DIR=llama.cpp 폴더 · set WOW_VLM_DIR=GGUF 폴더 후 실행
rem  받는 법 · 구조: Docs/Backend/AiLearning.md 4-1, Docs/Bot/AiStrategyBot.md "하이브리드 봇"
rem ================================================================
if "%WOW_LLAMA_DIR%"=="" set "WOW_LLAMA_DIR=%USERPROFILE%\models\llama.cpp-b11642"
if "%WOW_VLM_DIR%"=="" set "WOW_VLM_DIR=%USERPROFILE%\models\Qwen2.5-VL-3B-Instruct-GGUF"
set "VLM_URL=http://127.0.0.1:8790"

curl -s -m 2 %VLM_URL%/health | find "ok" > nul
if errorlevel 1 goto start_vlm
echo [*] 로컬 VLM 이 이미 켜져 있습니다: %VLM_URL%/v1
goto server

:start_vlm
if not exist "%WOW_LLAMA_DIR%\llama-server.exe" goto nollama
if not exist "%WOW_VLM_DIR%\Qwen2.5-VL-3B-Instruct-Q4_K_M.gguf" goto nollama
echo [*] 로컬 VLM 켜는 중 - 새 창 'WoW 로컬 VLM', 모델 올리는 데 10초쯤...
start "WoW 로컬 VLM (llama-server)" /min "%WOW_LLAMA_DIR%\llama-server.exe" -m "%WOW_VLM_DIR%\Qwen2.5-VL-3B-Instruct-Q4_K_M.gguf" --mmproj "%WOW_VLM_DIR%\mmproj-Qwen2.5-VL-3B-Instruct-f16.gguf" --host 127.0.0.1 --port 8790 -ngl 99 -c 12288 --parallel 3 --alias wow-vlm
set /a TRIES=0
:wait
timeout /t 1 > nul
curl -s -m 2 %VLM_URL%/health | find "ok" > nul
if not errorlevel 1 goto ready
set /a TRIES+=1
if %TRIES% lss 90 goto wait
echo [경고] 로컬 VLM 이 90초 안에 켜지지 않았습니다. 'WoW 로컬 VLM' 창의 메시지를 확인하세요. 게임 서버는 규칙 AI 로 켭니다.
goto server

:ready
echo [*] 로컬 VLM 켜짐: %VLM_URL%/v1 - Qwen2.5-VL-3B, 한 번 답하는 데 2~3초
goto server

:nollama
echo [경고] llama.cpp 또는 모델 파일이 없습니다:
echo        %WOW_LLAMA_DIR%\llama-server.exe
echo        %WOW_VLM_DIR%\Qwen2.5-VL-3B-Instruct-Q4_K_M.gguf + mmproj-Qwen2.5-VL-3B-Instruct-f16.gguf
echo        받는 법: Docs/Backend/AiLearning.md 4-1. 게임 서버는 규칙 AI 로 켭니다.

:server
set "PY=python"
if exist "%~dp0..\GestureTools\MediaPipe\.venv\Scripts\python.exe" set "PY=%~dp0..\GestureTools\MediaPipe\.venv\Scripts\python.exe"
echo ==============================================================
echo  War of Wizards 백엔드 서버 + 로컬 VLM 하이브리드 봇
echo ==============================================================
echo [*] 웹 브라우저 접속: http://localhost:8080/
echo [*] 종료: 이 창에서 Ctrl+C - 로컬 VLM 창은 따로 닫으세요
echo.
"%PY%" wow_server.py 8080
if errorlevel 1 pause
