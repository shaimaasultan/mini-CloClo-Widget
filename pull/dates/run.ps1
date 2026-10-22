<#
  The "dates" pull tool (#35). Mini CloClo runs it with two arguments: the
  settings file it wrote ({ "settings": { "country": ..., ... } }) and the
  result file to write. It downloads Islamic dates and public holidays with
  Get-CalendarTopics.ps1 (beside it) into the result file, which Mini
  CloClo imports into Questions and answers. A non-zero exit code means it
  failed.
#>
param(
    [Parameter(Mandatory)][string]$SettingsFile,
    [Parameter(Mandatory)][string]$ResultFile
)
$ErrorActionPreference = 'Stop'
$settings = (Get-Content -Raw -Encoding UTF8 -LiteralPath $SettingsFile | ConvertFrom-Json).settings

$country = "$($settings.country)".Trim()
$region = "$($settings.region)".Trim()
$method = "$($settings.islamicMethod)".Trim()
if ($method -notin 'HJCoSA', 'UAQ', 'DIYANET', 'MATHEMATICAL', 'None') { $method = 'HJCoSA' }
$years = 2
if (-not [int]::TryParse("$($settings.years)", [ref]$years) -or $years -lt 1 -or $years -gt 5) { $years = 2 }
if ($country -notmatch '^[A-Za-z]{2}$') { $country = '' }
if ($region -notmatch '^[A-Za-z]{2}-[A-Za-z0-9]{1,3}$') { $region = '' }

& (Join-Path $PSScriptRoot 'Get-CalendarTopics.ps1') -Country $country -Region $region -IslamicMethod $method -Years $years -OutFile $ResultFile
