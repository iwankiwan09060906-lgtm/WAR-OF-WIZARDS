@echo off
chcp 65001 > nul
title War of Wizards - 가짜 Unity (UDP 수신 확인)
cd /d "%~dp0"
echo [*] Unity 없이 손 추적기 출력을 확인합니다. (UDP 5065 수신)
echo [*] 먼저 run_hand_tracker.bat 을 켜 두세요. 종료: Ctrl+C
echo.
if exist ".venv\Scripts\python.exe" (set PY=.venv\Scripts\python.exe) else (set PY=python)
"%PY%" udp_monitor.py --deck S03,S06,S07,S08,S11,S13,H01,H02 %*
pause
