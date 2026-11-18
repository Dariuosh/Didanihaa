[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\Runtime.Common.ps1')
$runtimeRoot = Get-PublishedRuntimeRoot
$remaining = [Collections.Generic.List[object]]::new()
foreach ($role in @('Media', 'Control')) {
    foreach ($record in @(Get-LauncherState -Root $runtimeRoot -Mode Runtime) | Where-Object role -eq $role) {
        $trackedProcess = Get-TrackedProcess -Record $record
        if ($null -eq $trackedProcess) { continue }
        try {
            Stop-Process -Id $trackedProcess.Id -ErrorAction Stop
            $trackedProcess.WaitForExit(10000) | Out-Null
            Write-Output ('Stopped ' + $role + ' (' + $record.id + ').')
        } catch { $remaining.Add($record); Write-Warning $_.Exception.Message }
    }
}
Save-LauncherState -Root $runtimeRoot -Mode Runtime -Processes $remaining.ToArray()
Write-Output 'Only launcher-owned processes were targeted. A registered Windows Service is controlled from its dashboard.'
