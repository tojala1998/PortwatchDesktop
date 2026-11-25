$ErrorActionPreference = 'Stop'
$target = $env:PORTWATCH_UPDATE_TARGET
$asset = $env:PORTWATCH_UPDATE_ASSET
$directory = [IO.Path]::GetDirectoryName($target)
$stage = [IO.Path]::GetDirectoryName($asset)
$mutex = $null
$ownsMutex = $false
$replacement = $null
$backup = $null
$failure = $null
$originalExited = $false

try {
    # Keep the single-instance gate alive while the original process and the updater overlap.
    $mutex = [Threading.Mutex]::new($false, 'Local\PortwatchDesktop.Instance')
    $owner = Get-Process -Id ([int]$env:PORTWATCH_UPDATE_PARENT_ID) -ErrorAction SilentlyContinue
    if ($null -ne $owner) {
        try {
            if (!$owner.HasExited -and $owner.StartTime.ToUniversalTime().Ticks -eq [long]$env:PORTWATCH_UPDATE_PARENT_START) {
                if (!$owner.WaitForExit(60000)) { throw 'Portwatch did not exit in time.' }
            }
        } catch [InvalidOperationException] {
            if (!$owner.HasExited) { throw }
        } finally {
            $owner.Dispose()
        }
    }
    $originalExited = $true
    try { $ownsMutex = $mutex.WaitOne(3000) }
    catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (!$ownsMutex) { throw 'Another Portwatch instance is running.' }

    if ((Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash -ne $env:PORTWATCH_UPDATE_HASH) {
        throw 'The downloaded update failed its SHA-256 check.'
    }

    if ($env:PORTWATCH_UPDATE_INSTALLER -eq '1') {
        $start = [Diagnostics.ProcessStartInfo]::new($asset)
        $start.UseShellExecute = $false
        $start.WorkingDirectory = $stage
        $quotedDirectory = $directory
        if ($quotedDirectory.EndsWith('\')) { $quotedDirectory += '\' }
        $start.Arguments = '/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /DIR="' + $quotedDirectory + '"'
        $setup = [Diagnostics.Process]::Start($start)
        try {
            $setup.WaitForExit()
            if ($setup.ExitCode -ne 0) { throw "Windows Setup exited with code $($setup.ExitCode)." }
        } finally {
            $setup.Dispose()
        }
    } else {
        $id = [Guid]::NewGuid().ToString('N')
        $replacement = [IO.Path]::Combine($directory, ".Portwatch-$id.new")
        $backup = [IO.Path]::Combine($directory, ".Portwatch-$id.backup")
        [IO.File]::Copy($asset, $replacement)
        [IO.File]::Replace($replacement, $target, $backup, $true)
    }
} catch {
    $failure = $_.Exception.Message
    if ($backup -and [IO.File]::Exists($backup) -and ![IO.File]::Exists($target)) {
        try { [IO.File]::Move($backup, $target) }
        catch { $failure += " The previous executable is saved at $backup." }
    }
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
    if ($replacement) {
        try { [IO.File]::Delete($replacement) } catch {}
    }
    try {
        [IO.File]::Delete($asset)
        [IO.Directory]::Delete($stage)
    } catch {}
}

if ($originalExited -and $env:PORTWATCH_UPDATE_RESTART -eq '1' -and [IO.File]::Exists($target)) {
    try {
        $start = [Diagnostics.ProcessStartInfo]::new($target)
        $start.UseShellExecute = $false
        $start.WorkingDirectory = $directory
        $restarted = [Diagnostics.Process]::Start($start)
        $restarted.Dispose()
    } catch {
        $failure = "$failure Open Portwatch again to continue. $($_.Exception.Message)".Trim()
    }
}

if ($failure) {
    [Console]::Error.WriteLine($failure)
    if ($env:PORTWATCH_UPDATE_INTERACTIVE -eq '1') {
        Add-Type -AssemblyName System.Windows.Forms
        [void][Windows.Forms.MessageBox]::Show($failure, 'Portwatch update', 'OK', 'Warning')
    }
    exit 1
}
if ($backup) {
    try { [IO.File]::Delete($backup) } catch {}
}
