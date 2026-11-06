@echo off
setlocal
cd /d "%~dp0"
set "PWSH="
set "P1=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe"
if exist "%P1%" set "PWSH=%P1%"
if not defined PWSH set "PWSH=pwsh.exe"
"%PWSH%" -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0CodeSnap.ps1" -TestOcr
echo.
pause
