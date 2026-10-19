@echo off
chcp 65001 > nul
setlocal
cd /d "%~dp0"

rem ================================================================
rem  War of Wizards 자체 호스트 서버 실행
rem   - 웹 UI      : http://이PC주소:8080/
rem   - API        : http://이PC주소:8080/api/...
rem   - Unity WebGL: http://이PC주소:8080/game/
rem  처음 한 번: Database\oracle\setup-oracle.bat  (Oracle 계정 WOW / WOW_MOTION 생성)
rem  사용법: run-server.bat            (Oracle XE — 통합 DB + 모션 DB)
rem          run-server.bat h2         (설치 없는 파일 DB)
rem          run-server.bat postgres   (PostgreSQL / Supabase)
rem ================================================================

set PROFILE=%1
if "%PROFILE%"=="" set PROFILE=oracle

rem 코드가 바뀌었으면 다시 빌드 (바뀐 게 없으면 몇 초 만에 끝남)
echo [1/2] 서버 빌드 중...
call gradlew.bat bootJar -q
if errorlevel 1 goto :fail

echo.
echo [2/2] 서버 시작 (DB 프로필: %PROFILE%)
echo  같은 와이파이/네트워크의 다른 PC, 휴대폰, Quest 에서 접속할 주소:
set SHOWPORT=8080
if defined PORT set SHOWPORT=%PORT%
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do for /f "tokens=*" %%b in ("%%a") do echo     http://%%b:%SHOWPORT%/
echo  (접속이 안 되면 관리자 권한으로 open-firewall.bat 실행)
echo.

java -Dfile.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dstdout.encoding=UTF-8 -jar build\libs\war-of-wizards-server-0.2.0.jar --spring.profiles.active=%PROFILE%
goto :eof

:fail
echo 빌드에 실패했습니다. JDK 17 이상이 설치되어 있는지 확인하세요.
pause
