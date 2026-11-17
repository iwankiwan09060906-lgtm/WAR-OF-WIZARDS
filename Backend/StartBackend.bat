@echo off
chcp 65001 > nul
rem 예전 통합 백엔드(로그인 · 매칭) + 4인 · 1:1 · 다인전 게임 서버 (메타 UI 로그인 화면이 서버를 못 찾으면 이것을 켠다). 전시 시연 서버는 ..\StartServer.bat
curl -s -m 2 http://127.0.0.1:8080/api/health > nul
if not errorlevel 1 goto :eof
call "%~dp0run_server.bat" --game-servers
