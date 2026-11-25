param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$Portable
)

$ErrorActionPreference = 'Stop'
$scratch = Join-Path $env:RUNNER_TEMP "Portwatch update test $([Guid]::NewGuid().ToString('N'))"
$script = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../ApplyUpdate.ps1') -Raw
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
$windowsPowerShell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell/v1.0/powershell.exe'
$expectedHash = (Get-FileHash -LiteralPath $Portable).Hash

function Test-Update([string]$name, [string]$payload, [bool]$installed, [bool]$tamper) {
    $application = Join-Path $scratch "$name app"
    $stage = Join-Path $scratch "$name download"
    New-Item -ItemType Directory -Path $application,$stage | Out-Null
    $filename = if ($installed) { 'PortwatchDesktop.exe' } else { 'Portwatch renamed.exe' }
    $target = Join-Path $application $filename
    $asset = Join-Path $stage (Split-Path -Leaf $payload)
    Set-Content -LiteralPath $target -Value 'previous executable'
    Set-Content -LiteralPath (Join-Path $application 'keep.txt') -Value 'user file'
    Copy-Item -LiteralPath $payload -Destination $asset
    $assetHash = (Get-FileHash -LiteralPath $asset).Hash
    if ($tamper) { Add-Content -LiteralPath $asset -Value 'modified download' }

    $owner = Start-Process -FilePath $windowsPowerShell -WindowStyle Hidden -PassThru `
        -ArgumentList '-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 2"'
    $start = [Diagnostics.ProcessStartInfo]::new($windowsPowerShell)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardError = $true
    foreach ($item in @('-NoLogo','-NoProfile','-NonInteractive','-STA','-EncodedCommand',$encoded)) {
        $start.ArgumentList.Add($item)
    }
    $start.Environment['PORTWATCH_UPDATE_PARENT_ID'] = [string]$owner.Id
    $start.Environment['PORTWATCH_UPDATE_PARENT_START'] = [string]$owner.StartTime.ToUniversalTime().Ticks
    $start.Environment['PORTWATCH_UPDATE_TARGET'] = $target
    $start.Environment['PORTWATCH_UPDATE_ASSET'] = $asset
    $start.Environment['PORTWATCH_UPDATE_HASH'] = $assetHash
    $start.Environment['PORTWATCH_UPDATE_INSTALLER'] = if ($installed) { '1' } else { '0' }
    $start.Environment['PORTWATCH_UPDATE_RESTART'] = '0'
    $start.Environment['PORTWATCH_UPDATE_INTERACTIVE'] = '0'
    $owner.Dispose()

    $worker = [Diagnostics.Process]::Start($start)
    try {
        $stderr = $worker.StandardError.ReadToEndAsync()
        if (!$worker.WaitForExit(120000)) { throw "$name update timed out." }
        $details = $stderr.GetAwaiter().GetResult()
        if ($tamper) {
            if ($worker.ExitCode -eq 0 -or $details -notmatch 'SHA-256' -or
                (Get-Content -LiteralPath $target) -ne 'previous executable') {
                throw "$name update failed to reject a modified payload. $details"
            }
        } elseif ($worker.ExitCode -ne 0 -or (Get-FileHash -LiteralPath $target).Hash -ne $expectedHash) {
            throw "$name update did not replace the executable. $details"
        }
    } finally {
        $worker.Dispose()
    }
    if ((Get-Content -LiteralPath (Join-Path $application 'keep.txt')) -ne 'user file' -or
        (Test-Path -LiteralPath $stage) -or (Get-ChildItem -LiteralPath $application -Force -Filter '.Portwatch-*')) {
        throw "$name update did not preserve the user file or clean up staged files."
    }
    Write-Output "$name update verified."
}

Test-Update 'Portable' $Portable $false $false
Test-Update 'Installed' $Installer $true $false
Test-Update 'Tampered' $Portable $false $true
