@echo off
chcp 65001 > nul
title War of Wizards - 서버
rem 더블클릭 한 번으로 전부 켠다: 백엔드(로그인 · 매칭) + 로컬 VLM(있으면) + 4인 · 1:1 게임 서버 (창 없이)
rem 게임을 PC 에서 켜면 서버가 꺼져 있을 때 자동으로 이 파일을 실행한다. 끄려면 이 창을 닫는다 (게임 서버도 같이 꺼진다).
curl -s -m 2 http://127.0.0.1:8080/api/health > nul
if errorlevel 1 goto start
echo [*] 서버가 이미 켜져 있습니다.
"%SystemRoot%\System32\timeout.exe" /t 3 > nul
goto :eof
:start
call "%~dp0Backend\run_server_vlm.bat" --game-servers
