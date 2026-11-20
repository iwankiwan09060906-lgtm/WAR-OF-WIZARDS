@echo off
chcp 65001 > nul
title War of Wizards - 전시 시연 서버
rem 더블클릭 한 번으로 전시 시연 서버를 켠다: 6인 고정 다인전 · 끝나지 않는 경기 · 사람이 없어도 AI 6명으로 계속 돈다.
rem Quest 가 접속하면 AI 자리를 넘겨받는다 (최대 6명). 서버 창 = 옵저버: 1~6 플레이어 · Tab 다음 · O 자동 순환 · V 전체 보기.
rem 서버가 꺼지면(오류 등) 3초 뒤 저절로 다시 켠다. 완전히 끄려면 이 창을 닫고 서버 창도 닫는다.
rem 예전 백엔드(로그인 · 매칭 · 4인 / 1:1 / 다인전 서버)는 Backend\run_server.bat --game-servers
set "DIR=%~dp0Builds\Server"
set "EXE=%DIR%\SpellboundServer.exe"
if exist "%EXE%" goto loop
echo [오류] 서버 빌드가 없습니다: %EXE%
echo        Unity 메뉴 Spellbound ^> Build ^> PC Server (Windows) 로 먼저 빌드하세요.
pause
goto :eof

:loop
echo [*] 시연 서버 켜는 중... (로그: %DIR%\server_demo.log)
"%EXE%" -server -demo -screen-fullscreen 1 -logFile "%DIR%\server_demo.log"

:wait
if not exist "%DIR%\.building" goto restart
echo [*] 서버 빌드 중 - 끝나면 다시 켭니다
"%SystemRoot%\System32\timeout.exe" /t 3 > nul
goto wait

:restart
echo [*] 서버가 꺼졌습니다 - 3초 뒤 다시 켭니다 (그만두려면 이 창을 닫으세요)
"%SystemRoot%\System32\timeout.exe" /t 3 > nul
goto loop
