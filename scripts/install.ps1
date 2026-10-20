<#
.SYNOPSIS
  Builds Mini CloClo and installs it for the current Windows user.

.DESCRIPTION
  - Publishes a self-contained build (no .NET install needed to run it) into
    publish\.
  - Packs the Quran recitations in Assets\Recitations\<reciter>\ into one
    file per reciter beside the exe (see scripts\pack-recitations.ps1).
  - Copies everything to %LOCALAPPDATA%\Programs\MiniCloClo - no
    administrator rights needed.
  - Adds Start Menu and desktop shortcuts. Both open with Ctrl+Alt+C from
    anywhere (and bring Mini CloClo back if it is already running).
  - Starts Mini CloClo with Windows (unless -NoStartup), and starts it now.

  Run it from a normal (not administrator) PowerShell window, or double-click
  scripts\Install-MiniCloClo.cmd.

.PARAMETER NoStartup
  Don't start Mini CloClo when you sign in to Windows.
#>
param(
    [switch]$NoStartup
)

$env:PSModulePath = $PSHOME + '\Modules'
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$publish = Join-Path $root 'publish'
$target = Join-Path $env:LOCALAPPDATA 'Programs\MiniCloClo'
$exeName = 'MiniCloClo.exe'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ((New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from a normal PowerShell window, not as administrator: Mini CloClo installs for your own account.'
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "The .NET SDK wasn't found at $dotnet. Install the .NET 10 SDK from https://dotnet.microsoft.com/download and run this again."
}

Write-Host 'Building Mini CloClo...'
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
& $dotnet publish (Join-Path $root 'MiniCloClo.csproj') -c Release -r win-x64 --self-contained true -o $publish -nologo `
    --disable-build-servers -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "The build failed (dotnet publish exited with $LASTEXITCODE)." }

Write-Host 'Packing the Quran recitations...'
$packs = & (Join-Path $PSScriptRoot 'pack-recitations.ps1') -Root $root -OutDir $publish -DotnetExe $dotnet
if (-not $packs) { Write-Host 'No recitations found in Assets\Recitations - the Quran window will be text only until you choose a reciter folder.' }

. (Join-Path $PSScriptRoot 'common.ps1')
Stop-MiniCloClo

Write-Host "Installing to $target..."
New-Item -ItemType Directory -Force -Path $target | Out-Null
# Mirror the build: files left from an older version are removed.
& robocopy $publish $target /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the files failed (robocopy exited with $LASTEXITCODE)." }
$exe = Join-Path $target $exeName

Write-Host 'Adding shortcuts (Ctrl+Alt+C opens Mini CloClo)...'
$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Mini CloClo.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Mini CloClo.lnk'
foreach ($path in @($startMenu, $desktop)) {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $target
    $link.IconLocation = "$exe,0"
    $link.Description = 'Mini CloClo: a talking clock with prayer times, reminders and Quran memorization'
    # Windows honours a shortcut key on Start Menu and desktop shortcuts;
    # the second one is harmless if Windows keeps only one.
    $link.Hotkey = 'CTRL+ALT+C'
    $link.Save()
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($NoStartup) {
    Remove-ItemProperty -Path $runKey -Name 'MiniCloClo' -ErrorAction SilentlyContinue
} else {
    Set-ItemProperty -Path $runKey -Name 'MiniCloClo' -Value ('"{0}" --wait-for-security' -f $exe)
}

Write-Host 'Starting Mini CloClo...'
Start-Process -FilePath $exe -WorkingDirectory $target
Write-Host ''
Write-Host 'Done. Mini CloClo is in the bottom-right corner, just above the taskbar.'
Write-Host 'Press Ctrl+Alt+H at any time to hear the help.'
Write-Host 'To have Windows Firewall block all of its network traffic, double-click scripts\Block-MiniCloClo-Network.cmd.'
