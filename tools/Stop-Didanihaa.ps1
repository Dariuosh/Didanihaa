[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\Runtime.Common.ps1')
$runtimeRoot = Get-PublishedRuntimeRoot
$startupLock = Open-RuntimeFileLock -Path (Join-Path (Get-LauncherDirectory -Root $runtimeRoot -Mode Runtime) 'startup.lock') -TimeoutMilliseconds 2000
try {
    $remaining = [Collections.Generic.List[object]]::new()
    $records = @(Get-LauncherState -Root $runtimeRoot -Mode Runtime)
    foreach ($role in @('Media', 'Control')) {
        foreach ($record in $records | Where-Object role -eq $role) {
            $trackedProcess = Get-TrackedProcess -Record $record
            if ($null -eq $trackedProcess) { continue }
            try {
                Stop-Process -Id $trackedProcess.Id -ErrorAction Stop
                if (-not $trackedProcess.WaitForExit(10000)) { throw 'Process did not exit; its ownership record was kept.' }
                Write-Output ('Stopped ' + $role + ' (' + $record.id + ').')
            } catch { $remaining.Add($record); Write-Warning $_.Exception.Message }
        }
    }
    Save-LauncherState -Root $runtimeRoot -Mode Runtime -Processes $remaining.ToArray()
    Write-Output 'Only launcher-owned processes were targeted. A registered Windows Service is controlled from its dashboard.'
} finally { $startupLock.Dispose() }
