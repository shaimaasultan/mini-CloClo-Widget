<#
.SYNOPSIS
  Removes Mini CloClo for the current Windows user.

.DESCRIPTION
  Closes it, removes it from startup, deletes its shortcuts and the
  installed files in %LOCALAPPDATA%\Programs\MiniCloClo. Your settings,
  reminders and Quran progress (%APPDATA%\MiniCloClo) are kept unless you
  add -RemoveSettings.
#>
param(
    [switch]$RemoveSettings
)

$env:PSModulePath = $PSHOME + '\Modules'
$ErrorActionPreference = 'Stop'

Get-Process -Name 'MiniCloClo' -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(3000)) { Stop-Process -Id $_.Id -Force }
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MiniCloClo' -ErrorAction SilentlyContinue
foreach ($path in @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Mini CloClo.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Mini CloClo.lnk'))) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
$target = Join-Path $env:LOCALAPPDATA 'Programs\MiniCloClo'
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
if ($RemoveSettings) {
    foreach ($folder in @((Join-Path $env:APPDATA 'MiniCloClo'), (Join-Path $env:LOCALAPPDATA 'MiniCloClo'))) {
        if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    }
    Write-Host 'Mini CloClo and its settings were removed.'
} else {
    Write-Host 'Mini CloClo was removed. Your settings, reminders and Quran progress are kept in %APPDATA%\MiniCloClo.'
}
