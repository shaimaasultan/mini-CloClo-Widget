# Shared by install.ps1 and uninstall.ps1 (dot-sourced).

# Closes a running Mini CloClo and waits until it has really gone, so its
# files are no longer in use. First it asks it to exit normally (the named
# event the app listens for - see SingleInstance.cs), which saves its place
# and unloads the speech model; only a copy that doesn't respond is ended.
function Stop-MiniCloClo {
    $running = @(Get-Process -Name 'MiniCloClo' -ErrorAction SilentlyContinue)
    if (-not $running.Count) { return }
    Write-Host 'Closing the running Mini CloClo...'
    $signal = $null
    if ([Threading.EventWaitHandle]::TryOpenExisting('Local\MiniCloClo.Exit', [ref]$signal)) {
        try { [void]$signal.Set() } finally { $signal.Dispose() }
    }
    foreach ($process in $running) {
        if (-not $process.WaitForExit(10000)) {
            Write-Host 'It did not close by itself; ending it.'
            try { $process.Kill() } catch { }
            [void]$process.WaitForExit(10000)
        }
    }
    if (Get-Process -Name 'MiniCloClo' -ErrorAction SilentlyContinue) {
        throw 'Mini CloClo is still running (perhaps for another user or as administrator). Close it from its menu (Exit) and run this again.'
    }
}

# Deletes a folder, retrying for a few seconds: Windows can keep a file
# locked for a moment after the program that used it has ended.
function Remove-FolderWithRetry([string]$Path) {
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) { return }
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -eq 10) { throw "Couldn't remove $Path - a file in it is still in use. Restart Windows and run this again. ($($_.Exception.Message))" }
            Start-Sleep -Milliseconds 500
        }
    }
}
