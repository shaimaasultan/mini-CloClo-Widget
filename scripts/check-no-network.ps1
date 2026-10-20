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
# The one exception: BluetoothDialer.cs talks to a paired phone over a
# Bluetooth socket (StreamSocket), so the socket types are allowed there -
# but nothing else that reaches a network, and no address typed into the
# code (new HostName): it may only connect to a Bluetooth service's host.
$bluetoothTypes = 'HttpClient|SocketsHttpHandler|HttpClientHandler|WebClient|TcpClient|UdpClient|ClientWebSocket|DatagramSocket|MessageWebSocket|StreamWebSocket|StreamSocketListener'
$bluetoothPattern = "new\s+([\w.]+\.)?($bluetoothTypes)\s*\(|\b($bluetoothTypes)\??\s+\w+\s*=\s*new\s*\(|WebRequest\.Create|HttpWebRequest|Windows\.Web\.Http|System\.Net\.Http|System\.Net\.Sockets|Geolocator|Windows\.Devices\.Geolocation|new\s+HostName\s*\("
$found = @()
foreach ($path in [IO.File]::ReadAllLines($ListFile)) {
    if (-not $path) { continue }
    if (-not [IO.File]::Exists($path)) { continue }
    $rule = if ([IO.Path]::GetFileName($path) -eq 'BluetoothDialer.cs') { $bluetoothPattern } else { $pattern }
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        $lineNumber++
        if ($line -match $rule) { $found += "${path}:${lineNumber}: $($line.Trim())" }
    }
}
if ($found.Count) {
    [Console]::Error.WriteLine('Mini CloClo must stay offline, but these lines go online or read the location:')
    $found | ForEach-Object { [Console]::Error.WriteLine("  $_") }
    exit 1
}
