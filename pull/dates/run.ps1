<#
  The "dates" pull tool (#35). Mini CloClo runs it with two arguments: the
  settings file it wrote ({ "language": ..., "place": { "city", "country",
  "region" }, "settings": { "country": ..., ... } }) and the result file to
  write. It downloads Islamic dates and public holidays with
  Get-CalendarTopics.ps1 (beside it) into the result file, which Mini
  CloClo imports into Questions and answers. A non-zero exit code means it
  failed.
#>
param(
    [Parameter(Mandatory)][string]$SettingsFile,
    [Parameter(Mandatory)][string]$ResultFile
)
$ErrorActionPreference = 'Stop'
$file = Get-Content -Raw -Encoding UTF8 -LiteralPath $SettingsFile | ConvertFrom-Json
$settings = $file.settings
$place = $file.place

$country = "$($settings.country)".Trim()
$region = "$($settings.region)".Trim()
$method = "$($settings.islamicMethod)".Trim()
if ($method -notin 'HJCoSA', 'UAQ', 'DIYANET', 'MATHEMATICAL', 'None') { $method = 'HJCoSA' }
$years = 2
if (-not [int]::TryParse("$($settings.years)", [ref]$years) -or $years -lt 1 -or $years -gt 5) { $years = 2 }
# Empty: Mini CloClo's prayer-times city's country - and its province, when
# the province is empty too; then Windows' "Country or region" setting.
# "none": no public holidays.
if (($country -eq '' -or $country -eq 'auto') -and "$($place.country)" -match '^[A-Za-z]{2}$') {
    $country = "$($place.country)"
    if ($region -eq '' -and "$($place.region)" -like "$country-*") { $region = "$($place.region)" }
}
if ($country -eq '' -or $country -eq 'auto') {
    # Windows' "Country or region" setting is kept in the registry (Geo\Name);
    # the region of the language format (often United States) is the fallback.
    $country = "$((Get-ItemProperty 'HKCU:\Control Panel\International\Geo' -ErrorAction SilentlyContinue).Name)"
    if ($country -notmatch '^[A-Za-z]{2}$') {
        try { $country = [Globalization.RegionInfo]::CurrentRegion.TwoLetterISORegionName } catch { $country = '' }
    }
}
if ($country -notmatch '^[A-Za-z]{2}$') { $country = '' }
if ($region -notmatch '^[A-Za-z]{2}-[A-Za-z0-9]{1,3}$') { $region = '' }

& (Join-Path $PSScriptRoot 'Get-CalendarTopics.ps1') -Country $country -Region $region -IslamicMethod $method -Years $years -OutFile $ResultFile
