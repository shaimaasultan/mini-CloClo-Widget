<#
.SYNOPSIS
  Run by the build (CheckNoNetwork in MiniCloClo.csproj). Fails it when any
  C# file creates an HTTP client, socket or web request.

.DESCRIPTION
  Mini CloClo works entirely offline: no weather, no place search, no
  downloads. This keeps it that way - a change that tries to go online
  doesn't build.

  -ListFile  one full path per line: the C# files being compiled
#>
param(
    [Parameter(Mandatory)][string]$ListFile
)
$ErrorActionPreference = 'Stop'

# Matches "new HttpClient(", "new System.Net.Http.HttpClient(" and
# "HttpClient c = new(" alike, plus WinRT's own HTTP and socket classes.
$types = 'HttpClient|SocketsHttpHandler|HttpClientHandler|WebClient|TcpClient|UdpClient|ClientWebSocket|Socket|StreamSocket|DatagramSocket|MessageWebSocket|StreamWebSocket'
$pattern = "new\s+([\w.]+\.)?($types)\s*\(|\b($types)\??\s+\w+\s*=\s*new\s*\(|WebRequest\.Create|HttpWebRequest|Windows\.Web\.Http|Windows\.Networking\.Sockets|System\.Net\.Http|System\.Net\.Sockets|Geolocator|Windows\.Devices\.Geolocation"
$found = @()
foreach ($path in [IO.File]::ReadAllLines($ListFile)) {
    if (-not $path) { continue }
    if (-not [IO.File]::Exists($path)) { continue }
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        $lineNumber++
        if ($line -match $pattern) { $found += "${path}:${lineNumber}: $($line.Trim())" }
    }
}
if ($found.Count) {
    [Console]::Error.WriteLine('Mini CloClo must stay offline, but these lines go online or read the location:')
    $found | ForEach-Object { [Console]::Error.WriteLine("  $_") }
    exit 1
}
