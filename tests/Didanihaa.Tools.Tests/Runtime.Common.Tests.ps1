$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repositoryRoot 'tools\internal\Runtime.Common.ps1')

# File-only fixtures: never start/stop an application, listener or Windows service.
$fixtureParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'temp\automation\launcher-state-tests'))
$fixtureRoot = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$script:assertionCount = 0

function Assert-LauncherCheck {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:assertionCount++
}

function Assert-StateRejected {
    param([scriptblock]$Action, [string]$Message)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-LauncherCheck $rejected $Message
}

function Write-LegacyFixture {
    param([string]$Root, [string]$RecordedRoot, [object[]]$Processes)
    $directory = Join-Path $Root 'temp\dev'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $json = @{ root = $RecordedRoot; processes = @($Processes) } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $directory 'processes.json'), $json, [Text.UTF8Encoding]::new($false))
}

try {
    $freshRoot = Join-Path $fixtureRoot 'fresh'
    Assert-LauncherCheck (@(Get-LauncherState -Root $freshRoot -Mode Runtime).Count -eq 0) 'A new runtime must have no tracked processes.'
    Assert-LauncherCheck (@(Get-LauncherState -Root $freshRoot -Mode Development).Count -eq 0) 'A new development session must have no tracked processes.'
    Assert-LauncherCheck (-not (Test-Path -LiteralPath $freshRoot)) 'Reading absent state must not create files.'

    $currentProcess = Get-Process -Id $PID
    $controlRecord = [PSCustomObject]@{ role = 'Control'; id = $currentProcess.Id; executable = $currentProcess.Path; startedUtc = $currentProcess.StartTime.ToUniversalTime().ToString('O') }
    $mediaRecord = [PSCustomObject]@{ role = 'Media'; id = $currentProcess.Id; executable = $currentProcess.Path; startedUtc = $controlRecord.startedUtc }
    $viteRecord = [PSCustomObject]@{ role = 'Vite'; id = $currentProcess.Id; executable = $currentProcess.Path; startedUtc = $controlRecord.startedUtc }

    Save-LauncherState -Root $freshRoot -Mode Development -Processes @($viteRecord)
    Assert-LauncherCheck (Test-Path -LiteralPath (Join-Path $freshRoot 'temp\dev\processes.json')) 'Development must retain its own state directory.'
    Assert-LauncherCheck (-not (Test-Path -LiteralPath (Join-Path $freshRoot 'temp\runtime'))) 'Development must not create runtime state.'
    # The mode marker prevents development state from becoming a runtime fallback.
    Assert-StateRejected { Get-LauncherState -Root $freshRoot -Mode Runtime } 'A marked development state must not be imported as runtime state.'
    Save-LauncherState -Root $freshRoot -Mode Runtime -Processes @($mediaRecord)
    Assert-LauncherCheck ((@(Get-LauncherState -Root $freshRoot -Mode Runtime)[0]).role -eq 'Media') 'Runtime must read its own saved state.'
    Assert-LauncherCheck ((@(Get-LauncherState -Root $freshRoot -Mode Development)[0]).role -eq 'Vite') 'Runtime saves must not change development ownership.'

    $legacyRoot = Join-Path $fixtureRoot 'legacy-runtime'
    Write-LegacyFixture -Root $legacyRoot -RecordedRoot $legacyRoot -Processes @($controlRecord, $mediaRecord)
    $legacyPath = Join-Path $legacyRoot 'temp\dev\processes.json'
    $legacyHash = (Get-FileHash -LiteralPath $legacyPath).Hash
    $imported = @(Get-LauncherState -Root $legacyRoot -Mode Runtime)
    Assert-LauncherCheck ($imported.Count -eq 2) 'Both legacy backend records must remain available for stopping.'
    Assert-LauncherCheck ((Get-TrackedProcess -Record $imported[0]).Id -eq $currentProcess.Id) 'Legacy PID, executable and start time must preserve process identity.'
    Save-LauncherState -Root $legacyRoot -Mode Runtime -Processes $imported
    Assert-LauncherCheck (Test-Path -LiteralPath (Join-Path $legacyRoot 'temp\runtime\processes.json')) 'The first runtime save must use temp/runtime.'
    Assert-LauncherCheck ((Get-FileHash -LiteralPath $legacyPath).Hash -eq $legacyHash) 'Migration must preserve the legacy file.'
    Save-LauncherState -Root $legacyRoot -Mode Runtime -Processes @()
    Assert-LauncherCheck (@(Get-LauncherState -Root $legacyRoot -Mode Runtime).Count -eq 0) 'Empty current state must not resurrect legacy process records.'

    $runtimePath = Join-Path $legacyRoot 'temp\runtime\processes.json'
    [IO.File]::WriteAllText($runtimePath, '{invalid json')
    Assert-StateRejected { Get-LauncherState -Root $legacyRoot -Mode Runtime } 'Corrupt current state must not silently fall back to old records.'
    Assert-LauncherCheck ((Get-FileHash -LiteralPath $legacyPath).Hash -eq $legacyHash) 'A failed read must not alter legacy data.'

    $foreignRoot = Join-Path $fixtureRoot 'foreign-state'
    Write-LegacyFixture -Root $foreignRoot -RecordedRoot $legacyRoot -Processes @($controlRecord)
    Assert-StateRejected { Get-LauncherState -Root $foreignRoot -Mode Runtime } 'State from another portable root must be rejected.'
    Assert-LauncherCheck (-not (Test-Path -LiteralPath (Join-Path $foreignRoot 'temp\runtime'))) 'Rejected legacy state must not be migrated.'

    Save-LauncherState -Root $legacyRoot -Mode Runtime -Processes @($controlRecord)
    $invalidState = Get-Content -Raw -LiteralPath $runtimePath | ConvertFrom-Json
    $invalidState.processes = $null
    [IO.File]::WriteAllText($runtimePath, ($invalidState | ConvertTo-Json -Depth 4))
    Assert-StateRejected { Get-LauncherState -Root $legacyRoot -Mode Runtime } 'A missing process array must not be treated as empty ownership.'

    $reusedPid = [PSCustomObject]@{ role = 'Control'; id = $currentProcess.Id; executable = $currentProcess.Path; startedUtc = $currentProcess.StartTime.ToUniversalTime().AddSeconds(-5).ToString('O') }
    Assert-LauncherCheck ($null -eq (Get-TrackedProcess -Record $reusedPid)) 'Matching PID alone must never establish ownership.'
    $wrongPath = [PSCustomObject]@{ role = 'Control'; id = $currentProcess.Id; executable = (Join-Path $fixtureRoot 'other.exe'); startedUtc = $controlRecord.startedUtc }
    Assert-LauncherCheck ($null -eq (Get-TrackedProcess -Record $wrongPath)) 'Matching PID and start time must not accept a different executable.'

    Write-Output ('Launcher state checks passed: ' + $script:assertionCount + ' assertions.')
} finally {
    $cleanupTarget = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $cleanupTarget.StartsWith($fixtureParent + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped its workspace.' }
    if (Test-Path -LiteralPath $cleanupTarget) { Remove-Item -LiteralPath $cleanupTarget -Recurse -Force }
}
