@echo off
chcp 65001 > nul
rem PC 를 켜고 로그인하면 서버가 저절로 켜지게 한다 — 한 번 실행하면 켜지고, 다시 실행하면 꺼진다.
set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\War of Wizards Server.lnk"
if exist "%LNK%" goto off
set "TARGET=%~dp0StartServer.bat"
powershell -NoProfile -Command "$s = (New-Object -ComObject WScript.Shell).CreateShortcut($env:LNK); $s.TargetPath = $env:TARGET; $s.WorkingDirectory = (Split-Path $env:TARGET); $s.WindowStyle = 7; $s.Save()"
if errorlevel 1 goto fail
echo [*] 자동 시작을 켰습니다. 다음 로그인부터 서버가 최소화된 창으로 켜집니다.
goto done
:off
del "%LNK%"
echo [*] 자동 시작을 껐습니다.
goto done
:fail
echo [경고] 바로 가기를 만들지 못했습니다.
:done
pause
