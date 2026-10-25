<#
.SYNOPSIS
  Fetches the large files Mini CloClo is built with that are too big for git
  (#36): the Whisper large-v3-turbo speech model, and NVIDIA's cuBLAS for
  running it on an NVIDIA graphics card. Run once before installing (and
  again only if they change).

.DESCRIPTION
  Each file is checked against its pinned SHA-256 before it is used, and a
  file already in place with the right fingerprint is not fetched again.

  - Assets\Models\ggml-large-v3-turbo-q5_0.bin (574 MB) - from this
    repository's "speech-model-1" release, or else from the whisper.cpp
    models on Hugging Face (the same file).
  - Assets\Cuda\cublas64_13.dll and cublasLt64_13.dll (548 MB) - from
    NVIDIA's cuBLAS 13.8.0.4 redistributable archive (422 MB download),
    under NVIDIA's CUDA Toolkit license. Skip with -NoCuda: Mini CloClo then
    runs the model on the processor.

  This script goes online; Mini CloClo itself never does. The build checks
  the same fingerprints again.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\Get-SpeechFiles.ps1
#>
param([switch]$NoCuda)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path -Parent $PSScriptRoot

function Test-Hash([string]$Path, [string]$Sha256) {
    (Test-Path -LiteralPath $Path) -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Sha256
}

function Get-File([string[]]$Urls, [string]$Path, [string]$Sha256) {
    foreach ($url in $Urls) {
        $partial = "$Path.download"
        try {
            Write-Host "Downloading $(Split-Path -Leaf $Path) from $([Uri]::new($url).Host) ..."
            # curl.exe (part of Windows) shows progress and handles large files well.
            & "$env:SystemRoot\System32\curl.exe" -L --fail --retry 3 -o $partial $url
            if ($LASTEXITCODE -ne 0) { throw "curl exit code $LASTEXITCODE" }
            if (-not (Test-Hash $partial $Sha256)) { throw 'the fingerprint does not match' }
            Move-Item -LiteralPath $partial -Destination $Path -Force
            return
        }
        catch {
            Write-Warning "Couldn't get it from ${url}: $($_.Exception.Message)"
            Remove-Item -LiteralPath $partial -ErrorAction SilentlyContinue
        }
    }
    throw "Couldn't download $(Split-Path -Leaf $Path)."
}

# The speech model.
$model = Join-Path $root 'Assets\Models\ggml-large-v3-turbo-q5_0.bin'
$modelHash = '394221709CD5AD1F40C46E6031CA61BCE88931E6E088C188294C6D5A55FFA7E2'
if (Test-Hash $model $modelHash) { Write-Host 'The speech model is already in place.' }
else {
    New-Item -ItemType Directory -Force (Split-Path $model) | Out-Null
    Get-File @(
        'https://github.com/shaimaasultan/mini-CloClo-Widget/releases/download/speech-model-1/ggml-large-v3-turbo-q5_0.bin',
        'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin'
    ) $model $modelHash
}

# cuBLAS, for the graphics card.
if ($NoCuda) { Write-Host 'Skipping cuBLAS (-NoCuda): the model will run on the processor.' }
else {
    $cuda = Join-Path $root 'Assets\Cuda'
    $dlls = [ordered]@{
        'cublas64_13.dll'   = '60BBBA8868290311E9C1657B2193DDEC667744EB555FF843F87ACB7C039F9EFA'
        'cublasLt64_13.dll' = 'CAD63434448E7141629E240EA093AD596A7EF6A0F67B468BA9BC1DF6E1EEEE33'
    }
    if (@($dlls.Keys | Where-Object { -not (Test-Hash (Join-Path $cuda $_) $dlls[$_]) }).Count -eq 0) {
        Write-Host 'cuBLAS is already in place.'
    }
    else {
        New-Item -ItemType Directory -Force $cuda | Out-Null
        $archive = Join-Path $env:TEMP 'libcublas-windows-x86_64-13.8.0.4-archive.zip'
        if (-not (Test-Hash $archive '0974318E9861A61CB9091CF7DE8D4C2880E9787FE311CB4BCBD08865663C5F43')) {
            Get-File @('https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/libcublas-windows-x86_64-13.8.0.4-archive.zip') `
                $archive '0974318E9861A61CB9091CF7DE8D4C2880E9787FE311CB4BCBD08865663C5F43'
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            foreach ($name in $dlls.Keys) {
                $entry = $zip.Entries | Where-Object { $_.FullName -like "*/bin/x64/$name" } | Select-Object -First 1
                if (-not $entry) { throw "$name is not in the archive." }
                $target = Join-Path $cuda $name
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
                if (-not (Test-Hash $target $dlls[$name])) { throw "$name doesn't match its fingerprint." }
            }
        }
        finally { $zip.Dispose() }
        Remove-Item -LiteralPath $archive -ErrorAction SilentlyContinue
        Write-Host 'cuBLAS is in place.'
    }
}
Write-Host ''
Write-Host 'Done. Now run scripts\Install-MiniCloClo.cmd.'
