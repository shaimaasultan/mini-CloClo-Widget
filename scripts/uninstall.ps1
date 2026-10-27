<#
.SYNOPSIS
  Removes Mini CloClo for the current Windows user.

.DESCRIPTION
  Closes it, removes it from startup, deletes its shortcuts and the
  installed files in %LOCALAPPDATA%\Programs\MiniCloClo. Your settings,
  reminders and Quran progress (%LOCALAPPDATA%\MiniCloClo) are kept unless you
  add -RemoveSettings.
#>
param(
    [switch]$RemoveSettings
)

$env:PSModulePath = $PSHOME + '\Modules'
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')
Stop-MiniCloClo
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MiniCloClo' -ErrorAction SilentlyContinue
foreach ($path in @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Mini CloClo.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Mini CloClo.lnk'))) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
$target = Join-Path $env:LOCALAPPDATA 'Programs\MiniCloClo'
Remove-FolderWithRetry $target
if ($RemoveSettings) {
    # %APPDATA% too: where versions before 1.10 kept them.
    foreach ($folder in @((Join-Path $env:LOCALAPPDATA 'MiniCloClo'), (Join-Path $env:APPDATA 'MiniCloClo'))) {
        Remove-FolderWithRetry $folder
    }
    Write-Host 'Mini CloClo and its settings were removed.'
} else {
    Write-Host 'Mini CloClo was removed. Your settings, reminders and Quran progress are kept in %LOCALAPPDATA%\MiniCloClo.'
}
