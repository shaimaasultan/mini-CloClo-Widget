<#
.SYNOPSIS
  Adds (or with -Remove, removes) Windows Firewall rules that block every
  network connection of MiniCloClo.exe, in and out. Needs administrator
  rights; scripts\Block-MiniCloClo-Network.cmd asks for them.

.DESCRIPTION
  Mini CloClo has no code that goes online (the build checks it), and none
  of its libraries opens a connection. These rules make that a guarantee
  enforced by Windows itself, for everything inside the process - its code,
  the Whisper speech engine, .NET and the Windows components it loads:
    - all outbound traffic blocked, every protocol (TCP, UDP, ICMP...)
    - all inbound traffic blocked
  They are block rules, which Windows applies over any allow rule, and they
  match only this exe's path, so other programs are untouched. The rules
  stay if Mini CloClo is reinstalled to the same place; remove them with
  -Remove (an uninstall leaves them, where they do nothing).

    .\scripts\Set-MiniCloCloFirewall.ps1
    .\scripts\Set-MiniCloCloFirewall.ps1 -Remove
    .\scripts\Set-MiniCloCloFirewall.ps1 -Check     (no administrator needed)

  netsh.exe is run from System32 by full path, never found through PATH.
#>
param(
    [string]$ExePath = (Join-Path $env:LOCALAPPDATA 'Programs\MiniCloClo\MiniCloClo.exe'),
    [switch]$Remove,
    [switch]$Check
)
$env:PSModulePath = $PSHOME + '\Modules'
$ErrorActionPreference = 'Stop'

$ExePath = [IO.Path]::GetFullPath($ExePath)
if ([IO.Path]::GetFileName($ExePath) -ne 'MiniCloClo.exe') { throw 'ExePath must point at MiniCloClo.exe.' }
$netsh = [IO.Path]::Combine([Environment]::GetFolderPath('System'), 'netsh.exe')
$program = "program=$ExePath"

if ($Check) {
    # The rules are read with the same netsh, which a normal user may run.
    $text = (& $netsh advfirewall firewall show rule name=all dir=out verbose) + (& $netsh advfirewall firewall show rule name=all dir=in verbose)
    $found = @($text | Select-String -SimpleMatch 'Mini CloClo: block')
    if ($found.Count -ge 2) { "Mini CloClo's network is blocked by Windows Firewall ($($found.Count) rules)." }
    else { "No Mini CloClo firewall rules found. Run scripts\Block-MiniCloClo-Network.cmd to add them." }
    exit 0
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this as administrator (or double-click scripts\Block-MiniCloClo-Network.cmd): Windows Firewall rules need administrator rights.'
}

# Every rule for this exe, old or new, is removed first.
& $netsh advfirewall firewall delete rule name=all $program | Out-Null
if ($Remove) { "Mini CloClo firewall rules removed for $ExePath."; exit 0 }

$rules = @(
    @('name=Mini CloClo: block all outbound', 'dir=out'),
    @('name=Mini CloClo: block all inbound', 'dir=in')
)
foreach ($rule in $rules) {
    & $netsh advfirewall firewall add rule @rule action=block $program protocol=any enable=yes profile=any | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Windows Firewall refused the rule '$($rule[0])'. Is Windows Defender Firewall turned off, or managed by another firewall or a policy?"
    }
}
"Mini CloClo can no longer connect to anything: all its network traffic, in and out, is blocked for $ExePath."
