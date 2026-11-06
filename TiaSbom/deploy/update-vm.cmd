@echo off
rem Starts update-vm.ps1 as administrator and keeps the window open. Arguments are passed on, e.g. update-vm.cmd -Report
net session >nul 2>&1
if not errorlevel 1 goto run
if "%~1"=="" (
    powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
) else (
    powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
)
exit /b

:run
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-vm.ps1" %*
pause
