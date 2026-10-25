<#
  The "news" pull tool (#35). Mini CloClo runs it with two arguments: the
  settings file it wrote ({ "language": "en" or "ar", "settings": { ... } })
  and the result file to write. It downloads one RSS (or Atom) news feed -
  the English or the Arabic one, by the voice commands' language - and
  writes its newest headlines - and each story's summary, read when the
  person says its number - for Mini CloClo to read aloud:
  { "title": "...", "items": [ { "text": "...", "details": "..." } ] }.
  A non-zero exit code means it failed.
#>
param(
    [Parameter(Mandatory)][string]$SettingsFile,
    [Parameter(Mandatory)][string]$ResultFile
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$file = Get-Content -Raw -Encoding UTF8 -LiteralPath $SettingsFile | ConvertFrom-Json
$settings = $file.settings
$feed = if ($file.language -eq 'ar') { "$($settings.arabicFeed)".Trim() } else { "$($settings.englishFeed)".Trim() }
if ($feed -notmatch '^https://[^\s"]+$') { throw "The feed must be an https:// address." }
$count = 5
if (-not [int]::TryParse("$($settings.count)", [ref]$count) -or $count -lt 1 -or $count -gt 10) { $count = 5 }

# The feed's bytes as UTF-8 (Windows PowerShell may guess the wrong encoding).
$response = Invoke-WebRequest -Uri $feed -UseBasicParsing -TimeoutSec 30 -Headers @{ 'User-Agent' = 'MiniCloClo-news-tool' }
$bytes = $response.RawContentStream.ToArray()
if ($bytes.Length -gt 5MB) { throw 'The feed is too large.' }
$xml = [xml][Text.Encoding]::UTF8.GetString($bytes)

# A feed's text may be plain, or an element (CDATA, or with attributes).
function Text($node) { if ($null -eq $node) { '' } elseif ($node -is [string]) { $node } else { $node.InnerText } }

function Clean([string]$text) {
    # Feed titles may carry HTML entities or tags.
    $text = [Net.WebUtility]::HtmlDecode(($text -replace '<[^>]+>', ' '))
    ($text -replace '\s+', ' ').Trim()
}

# Each story: its headline and its summary (RSS description, Atom summary).
if ($xml.rss) {
    $title = Clean (Text $xml.rss.channel.title)
    $stories = @($xml.rss.channel.item | ForEach-Object { @{ Title = Text $_.title; Details = Text $_.description } })
}
elseif ($xml.feed) {
    $title = Clean (Text $xml.feed.title)
    $stories = @($xml.feed.entry | ForEach-Object { @{ Title = Text $_.title; Details = $(if ($_.summary) { Text $_.summary } else { Text $_.content }) } })
}
else { throw 'That address is not an RSS or Atom feed.' }

$items = @($stories | ForEach-Object { @{ Title = Clean $_.Title; Details = Clean $_.Details } } | Where-Object { $_.Title } |
    Select-Object -First $count | ForEach-Object {
        $details = $_.Details
        if ($details.Length -gt 900) { $details = $details.Substring(0, 900) + '...' }
        [ordered]@{ text = $_.Title; otherText = $_.Title; details = $details; otherDetails = $details }
    })
if (-not $items.Count) { throw 'The feed had no headlines.' }

$result = [ordered]@{ title = $title; otherTitle = $title; items = $items }
[IO.File]::WriteAllText($ResultFile, (ConvertTo-Json -InputObject $result -Depth 5), [Text.UTF8Encoding]::new($false))
