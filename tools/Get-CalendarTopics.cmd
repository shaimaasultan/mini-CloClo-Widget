@echo off
rem Double-click to download this year's and next year's Islamic dates and
rem public holidays into a file for Mini CloClo's Questions and answers
rem (Ctrl+Alt+I, Import...). This goes online; Mini CloClo itself doesn't.
rem With arguments, they are passed to the script as they are, for example:
rem   Get-CalendarTopics.cmd -Country CA -Region CA-ON
setlocal
if not "%~1"=="" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-CalendarTopics.ps1" %*
    goto done
)
echo Islamic dates and public holidays for Mini CloClo
echo.
set "country="
set "region="
set /p "country=Country code for public holidays (for example CA, EG, US, GB, SA - or Enter to skip): "
if not "%country%"=="" set /p "region=Province or state (for example CA-ON - or Enter for the whole country): "
echo.
if "%country%"=="" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-CalendarTopics.ps1"
) else if "%region%"=="" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-CalendarTopics.ps1" -Country "%country%"
) else (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-CalendarTopics.ps1" -Country "%country%" -Region "%region%"
)
:done
echo.
pause
