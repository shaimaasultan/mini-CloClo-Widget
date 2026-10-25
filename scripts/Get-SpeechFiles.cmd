@echo off
rem Double-click once before installing: fetches the speech model and NVIDIA's
rem cuBLAS (for the graphics card), each checked by its fingerprint. This goes
rem online; Mini CloClo itself doesn't. Add -NoCuda to skip cuBLAS.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-SpeechFiles.ps1" %*
echo.
pause
