@echo off
chcp 65001 > nul
setlocal
cd /d "%~dp0"

rem ================================================================
rem  War of Wizards - Oracle XE 준비 (한 번만 실행, 여러 번 실행해도 안전)
rem
rem   1) 계정 2개 생성   WOW (통합 DB) / WOW_MOTION (모션 DB)
rem   2) 예전 버전의 제스처 데이터가 있으면 모션 DB 로 이동
rem
rem  테이블 · 인덱스 · 한글 설명은 Spring 서버가 시작할 때 자동으로 만든다.
rem  "권한이 불충분합니다(ORA-01031)" 가 나오면 이 파일을 우클릭 > 관리자 권한으로 실행
rem ================================================================

set NLS_LANG=KOREAN_KOREA.AL32UTF8

where sqlplus > nul 2> nul
if errorlevel 1 (
    echo [오류] sqlplus 를 찾을 수 없습니다. Oracle XE 21c 가 설치되어 있는지 확인하세요.
    goto :end
)

echo [1/2] Oracle 계정 확인/생성 ...
sqlplus -S -L "/ as sysdba" @01_create_accounts.sql
if errorlevel 1 goto :fail

echo.
echo [2/2] 예전 데이터 이동 확인 ...
sqlplus -S -L "/ as sysdba" @02_move_old_gestures.sql

echo.
echo 완료! 이제 서버를 실행하세요:
echo    SpringServer\run-server.bat        (Oracle)
echo  서버가 시작되면 두 DB 의 테이블이 자동으로 만들어지고 연결 상태가 로그에 표시됩니다.
goto :end

:fail
echo.
echo [오류] Oracle 에 SYS 로 접속하지 못했습니다.
echo   - Windows 서비스에서 OracleServiceXE 가 실행 중인지 확인
echo   - 이 파일을 우클릭 ^> 관리자 권한으로 실행

:end
echo.
pause
