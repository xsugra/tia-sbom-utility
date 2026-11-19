@echo off
rem Checks that this PC and this Windows user can export SBOMs to Gitea. No administrator rights and no TIA Portal needed.
rem Double-click it, or run it from a command prompt. Send a screenshot of the result to the administrator when it fails.
setlocal
set "EXE=%~dp0tools\tiasbom.exe"
if not exist "%EXE%" set "EXE=%ProgramData%\TiaSbom\tools\tiasbom.exe"
if not exist "%EXE%" (
    echo FAILED: tiasbom.exe not found next to this file or in %ProgramData%\TiaSbom\tools.
    pause
    exit /b 1
)
"%EXE%" --check %*
set "RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %RESULT%
