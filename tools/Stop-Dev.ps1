[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\Launcher.Common.ps1')
$workspaceRoot = Get-DevelopmentRoot
$records = @(Get-DevelopmentState -Root $workspaceRoot)
$remaining = [Collections.Generic.List[object]]::new()
foreach ($role in @('Vite', 'Media', 'Control')) {
    foreach ($record in $records | Where-Object role -eq $role) {
        $trackedProcess = Get-TrackedDevelopmentProcess -Record $record
        if ($null -eq $trackedProcess) { continue }
        try {
            Stop-Process -Id $trackedProcess.Id -ErrorAction Stop
            $trackedProcess.WaitForExit(10000) | Out-Null
            Write-Output ('Stopped ' + $role + ' (' + $record.id + ').')
        } catch {
            $remaining.Add($record)
            Write-Warning ('Could not stop ' + $role + ': ' + $_.Exception.Message)
        }
    }
}
Save-DevelopmentState -Root $workspaceRoot -Processes $remaining.ToArray()
Write-Output 'Only processes recorded by this development launcher were targeted; catalog files were preserved.'
