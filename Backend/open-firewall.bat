@echo off
chcp 65001 > nul
rem ================================================================
rem  다른 PC / Quest 에서 이 서버(8080)에 접속할 수 있도록 Windows 방화벽 열기
rem  반드시 "관리자 권한으로 실행" 하세요. (한 번만 하면 됨)
rem  되돌리기: netsh advfirewall firewall delete rule name="War of Wizards Server 8080"
rem            netsh advfirewall firewall delete rule name="War of Wizards Discovery UDP 48080"
rem  (UDP 48080 = Quest가 서버를 자동으로 찾는 신호)
rem ================================================================
netsh advfirewall firewall add rule name="War of Wizards Server 8080" dir=in action=allow protocol=TCP localport=8080 profile=private,domain
netsh advfirewall firewall add rule name="War of Wizards Discovery UDP 48080" dir=in action=allow protocol=UDP localport=48080 profile=private,domain
if errorlevel 1 (
    echo 실패: 이 파일을 마우스 오른쪽 버튼 - "관리자 권한으로 실행" 하세요.
) else (
    echo 완료: 개인/도메인 네트워크에서 8080 포트가 열렸습니다.
)
pause
