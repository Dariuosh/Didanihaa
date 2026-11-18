[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\Runtime.Common.ps1')
$workspaceRoot = Get-DidanihaaRoot
$startupLock = Open-RuntimeFileLock -Path (Join-Path (Get-LauncherDirectory -Root $workspaceRoot -Mode Development) 'startup.lock') -TimeoutMilliseconds 2000
try {
    $records = @(Get-LauncherState -Root $workspaceRoot -Mode Development)
    $remaining = [Collections.Generic.List[object]]::new()
    foreach ($role in @('Vite', 'Media', 'Control')) {
        foreach ($record in $records | Where-Object role -eq $role) {
            $trackedProcess = Get-TrackedProcess -Record $record
            if ($null -eq $trackedProcess) { continue }
            try {
                Stop-Process -Id $trackedProcess.Id -ErrorAction Stop
                if (-not $trackedProcess.WaitForExit(10000)) { throw 'Process did not exit; its ownership record was kept.' }
                Write-Output ('Stopped ' + $role + ' (' + $record.id + ').')
            } catch {
                $remaining.Add($record)
                Write-Warning ('Could not stop ' + $role + ': ' + $_.Exception.Message)
            }
        }
    }
    Save-LauncherState -Root $workspaceRoot -Mode Development -Processes $remaining.ToArray()
    Write-Output 'Only processes recorded by this development launcher were targeted; catalog files were preserved.'
} finally { $startupLock.Dispose() }
