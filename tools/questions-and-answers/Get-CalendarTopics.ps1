<#
.SYNOPSIS
  Downloads this year's (and next year's) Islamic dates and public holidays
  into a file that Mini CloClo's Questions and answers window can import.

.DESCRIPTION
  Mini CloClo itself never goes online. This script is separate: you run it
  yourself when you want published dates, and it only writes a JSON file.
  Open Questions and answers (Ctrl+Alt+I), choose Import..., and pick the
  file. A topic with the same name ("Islamic dates") is replaced.

  Sources (free, no account):
    - Islamic dates: api.aladhan.com (Hijri to Gregorian conversion).
    - Public holidays: date.nager.at.

.PARAMETER Country
  Two-letter country code for public holidays, e.g. CA, EG, US, GB, SA.
  Leave empty to skip public holidays.

.PARAMETER Region
  Optional province or state, e.g. CA-ON for Ontario, so regional holidays
  such as the Civic Holiday are included.

.PARAMETER IslamicMethod
  HJCoSA (High Judicial Council of Saudi Arabia, the default), UAQ (Umm
  al-Qura), DIYANET (Turkey) or MATHEMATICAL. Use None to skip Islamic dates.

.PARAMETER Year
  The first Gregorian year (default: this year).

.PARAMETER Years
  How many years from Year (default 2, so dates later this year and next
  year are both there).

.PARAMETER OutFile
  Where to write the file (default: CloClo calendar <year>.json in your
  Downloads folder - Documents may be protected from scripts by Windows'
  Controlled folder access).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\questions-and-answers\Get-CalendarTopics.ps1 -Country CA -Region CA-ON
#>
param(
    [string]$Country = '',
    [string]$Region = '',
    [ValidateSet('HJCoSA', 'UAQ', 'DIYANET', 'MATHEMATICAL', 'None')][string]$IslamicMethod = 'HJCoSA',
    [int]$Year = (Get-Date).Year,
    [ValidateRange(1, 5)][int]$Years = 2,
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if (-not $OutFile) { $OutFile = Join-Path (Join-Path $env:USERPROFILE 'Downloads') "CloClo calendar $Year.json" }
$lastYear = $Year + $Years - 1

# A short pause between requests, and a few tries with longer waits when the
# server says "too many requests" (429).
function Get-Json([string]$Uri) {
    for ($try = 1; ; $try++) {
        Start-Sleep -Milliseconds 400
        try { return Invoke-RestMethod -Uri $Uri -TimeoutSec 30 -Headers @{ 'User-Agent' = 'MiniCloClo-calendar-script' } }
        catch {
            $status = $_.Exception.Response.StatusCode.value__
            if ($status -ne 429 -or $try -ge 5) { throw }
            Start-Sleep -Seconds (3 * $try)
        }
    }
}

# Same words as Mini CloClo's ready-made Islamic dates, so they replace them.
$islamicEvents = @(
    @{ Word = 'Isra and Miraj'; Other = 'الإسراء والمعراج'; Month = 7; Day = 27 },
    @{ Word = 'Shab-e-Barat'; Other = 'نص شعبان'; Month = 8; Day = 15 },
    @{ Word = 'Ramadan'; Other = 'رمضان'; Month = 9; Day = 1 },
    @{ Word = 'Laylat al-Qadr'; Other = 'ليلة القدر'; Month = 9; Day = 27 },
    @{ Word = 'Eid al-Fitr'; Other = 'عيد الفطر'; Month = 10; Day = 1 },
    @{ Word = 'Dhul Hijjah'; Other = 'ذو الحجة'; Month = 12; Day = 1 },
    @{ Word = 'Hajj'; Other = 'الحج'; Month = 12; Day = 8 },
    @{ Word = 'Arafah'; Other = 'عرفة'; Month = 12; Day = 9 },
    @{ Word = 'Eid al-Adha'; Other = 'عيد الأضحى'; Month = 12; Day = 10 },
    @{ Word = 'Islamic new year'; Other = 'رأس السنة الهجرية'; Month = 1; Day = 1 },
    @{ Word = 'Ashura'; Other = 'عاشوراء'; Month = 1; Day = 10 }
)

$topics = @()

if ($IslamicMethod -ne 'None') {
    # The Hijri years that overlap the Gregorian years asked for.
    $hijri = [Globalization.UmAlQuraCalendar]::new()
    $firstHijri = $hijri.GetYear([datetime]::new($Year, 1, 1))
    $lastHijri = $hijri.GetYear([datetime]::new($lastYear, 12, 31))
    $actions = @()
    foreach ($event in $islamicEvents) {
        $dates = @()
        foreach ($hijriYear in $firstHijri..$lastHijri) {
            $uri = 'https://api.aladhan.com/v1/hToG/{0:00}-{1:00}-{2}?calendarMethod={3}' -f $event.Day, $event.Month, $hijriYear, $IslamicMethod
            try {
                $reply = Get-Json $uri
                $date = [datetime]::ParseExact($reply.data.gregorian.date, 'dd-MM-yyyy', [Globalization.CultureInfo]::InvariantCulture)
                if ($date.Year -ge $Year -and $date.Year -le $lastYear) { $dates += $date.ToString('yyyy-MM-dd') }
            }
            catch { Write-Warning "Couldn't get $($event.Word) $hijriYear AH: $($_.Exception.Message)" }
        }
        if ($dates.Count) {
            # FromHijri: Mini CloClo then names the Hijri date (27 Ramadan) with each answer.
            $actions += [ordered]@{ Word = $event.Word; OtherWord = $event.Other; Kind = 'calendar'; Dates = @($dates | Sort-Object -Unique)
                FromHijri = $true; HijriMonth = $event.Month; HijriDay = $event.Day }
            Write-Host "$($event.Word): $($dates -join ', ')"
        }
    }
    if ($actions.Count) { $topics += [ordered]@{ Name = 'Islamic dates'; OtherName = 'المناسبات الإسلامية'; Actions = $actions } }
}

if ($Country) {
    $byName = [ordered]@{}
    foreach ($y in $Year..$lastYear) {
        try { $holidays = Get-Json ("https://date.nager.at/api/v3/PublicHolidays/{0}/{1}" -f $y, $Country.ToUpperInvariant()) }
        catch { Write-Warning "Couldn't get the $y holidays for ${Country}: $($_.Exception.Message)"; continue }
        foreach ($holiday in $holidays) {
            # Holidays for the whole country, or for the chosen region.
            if (-not $holiday.global -and -not ($Region -and $holiday.counties -contains $Region.ToUpperInvariant())) { continue }
            $name = ($holiday.name -replace '[^\p{L}\p{N} \-''.]', '').Trim()
            if (-not $name -or $name.Length -gt 40 -or ($name -split ' ').Count -gt 4) { continue }
            if (-not $byName.Contains($name)) {
                $other = if ($holiday.localName -and $holiday.localName -ne $holiday.name -and $holiday.localName.Length -le 40) { $holiday.localName } else { $null }
                $byName[$name] = [ordered]@{ Word = $name; OtherWord = $other; Kind = 'calendar'; Dates = @() }
            }
            $byName[$name].Dates += $holiday.date
        }
    }
    $actions = @($byName.Values | Select-Object -First 30)
    foreach ($action in $actions) { Write-Host "$($action.Word): $($action.Dates -join ', ')" }
    if ($actions.Count) {
        $label = if ($Region) { "Public holidays ($Region)" } else { "Public holidays ($($Country.ToUpperInvariant()))" }
        $topics += [ordered]@{ Name = $label; OtherName = 'الإجازات الرسمية'; Actions = $actions }
    }
}

if (-not $topics.Count) { throw 'Nothing was downloaded, so no file was written.' }
# Mini CloClo reads the file as UTF-8 JSON.
try { [IO.File]::WriteAllText($OutFile, (ConvertTo-Json -InputObject @($topics) -Depth 6), [Text.UTF8Encoding]::new($false)) }
catch { throw "Couldn't save to $OutFile ($($_.Exception.Message)). Choose another place with -OutFile." }
Write-Host ''
Write-Host "Saved $($topics.Count) topic(s) to: $OutFile"
Write-Host 'In Mini CloClo: Ctrl+Alt+I, Import..., choose this file, then Save and close.'
