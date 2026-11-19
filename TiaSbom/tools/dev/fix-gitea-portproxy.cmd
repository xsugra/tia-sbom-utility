@echo off
rem Asks for administrator rights, then runs fix-gitea-portproxy.ps1 next to this file and keeps the window open.
net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fix-gitea-portproxy.ps1"
pause
