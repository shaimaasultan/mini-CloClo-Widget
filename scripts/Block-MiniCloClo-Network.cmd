@echo off
rem Double-click to block all of Mini CloClo's network traffic with Windows
rem Firewall. Windows asks for administrator approval first.
rem The exe path is worked out here, as you, before that prompt: an
rem administrator account may have a different profile folder.
rem To take the rules away again, run Set-MiniCloCloFirewall.ps1 -Remove
rem from an administrator PowerShell.
set "EXE=%LOCALAPPDATA%\Programs\MiniCloClo\MiniCloClo.exe"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command ^
  "Start-Process -FilePath '%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe' -Verb RunAs -Wait -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','\"%~dp0Set-MiniCloCloFirewall.ps1\"','-ExePath','\"%EXE%\"')"
