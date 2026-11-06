@echo off
rem Starts install.ps1 regardless of the PowerShell execution policy. Arguments are passed on, e.g. install.cmd -Check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
exit /b %ERRORLEVEL%
