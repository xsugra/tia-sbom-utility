@echo off
rem Installs the newest TiaSbom release from Gitea. Run as administrator with TIA Portal closed.
rem Arguments are passed on: update.cmd -Check, update.cmd -Version 0.2.12, update.cmd -Force
rem The script runs from a copy in %TEMP%, because the update replaces update.ps1 in this folder.
setlocal
set "CONFIG=%~dp0"
set "CONFIG=%CONFIG:~0,-1%"
set "SCRIPT=%TEMP%\tiasbom-update-%RANDOM%.ps1"
copy /y "%~dp0update.ps1" "%SCRIPT%" >nul || (echo FAILED: cannot copy update.ps1 to %TEMP% & exit /b 1)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -ConfigDir "%CONFIG%" %*
set "RESULT=%ERRORLEVEL%"
del "%SCRIPT%" >nul 2>&1
exit /b %RESULT%
