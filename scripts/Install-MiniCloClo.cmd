@echo off
rem Double-click to build and install Mini CloClo for your account.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
echo.
pause
