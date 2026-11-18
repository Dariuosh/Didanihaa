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

    $identityRoot = Join-Path $fixtureRoot 'هویت اجرا با فاصله'
    $identityDirectory = Get-LauncherDirectory -Root $identityRoot -Mode Runtime
    [IO.Directory]::CreateDirectory($identityDirectory) | Out-Null
    $identity = [PSCustomObject]@{
        protocol = 1; rootId = (Get-RuntimeRootId -Root $identityRoot); instanceId = [Guid]::NewGuid().ToString('N')
        role = 'Media'; processId = $currentProcess.Id; executablePath = $currentProcess.Path; startedUtc = $controlRecord.startedUtc
    }
    $descriptorPath = Join-Path $identityDirectory 'media.identity.json'
    Write-AtomicRuntimeText -Path $descriptorPath -Text ($identity | ConvertTo-Json) -Overwrite
    Assert-LauncherCheck (Test-RuntimeIdentity -Identity $identity -Root $identityRoot -Role Media -Executable $currentProcess.Path) 'A current identity must match its root, role, descriptor, executable and process start.'
    $mediaResponse = [PSCustomObject]@{ identity = $identity; catalogReady = $true }
    Assert-LauncherCheck (Test-BackendResponse -Response $mediaResponse -Root $identityRoot -Role Media -Executable $currentProcess.Path -ExpectedProcessId $currentProcess.Id) 'A fresh Media start must prove the started PID.'
    Assert-LauncherCheck (-not (Test-BackendResponse -Response $mediaResponse -Root $identityRoot -Role Media -Executable $currentProcess.Path -ExpectedProcessId ([int]::MaxValue))) 'A different ready process must not satisfy a fresh start.'
    Assert-LauncherCheck (-not (Test-BackendResponse -Response ([PSCustomObject]@{ service = 'Didanihaa.Media'; catalogReady = $true }) -Root $identityRoot -Role Media -Executable $currentProcess.Path)) 'Legacy health alone must not prove ownership.'
    Assert-LauncherCheck (-not (Test-RuntimeIdentity -Identity $identity -Root $freshRoot -Role Media -Executable $currentProcess.Path)) 'A different root must not reuse the same response.'
    Assert-LauncherCheck (-not (Test-RuntimeIdentity -Identity $identity -Root $identityRoot -Role Control -Executable $currentProcess.Path)) 'A different role must not reuse the same response.'
    Assert-LauncherCheck (-not (Test-RuntimeIdentity -Identity $identity -Root $identityRoot -Role Media -Executable (Join-Path $identityRoot 'other.exe'))) 'A different executable must not be accepted.'
    $mediaResponse.catalogReady = $false
    Assert-LauncherCheck (-not (Test-BackendResponse -Response $mediaResponse -Root $identityRoot -Role Media -Executable $currentProcess.Path)) 'Identity alone must not mean the catalog is ready.'
    $identity.instanceId = [Guid]::NewGuid().ToString('N')
    Assert-LauncherCheck (-not (Test-RuntimeIdentity -Identity $identity -Root $identityRoot -Role Media -Executable $currentProcess.Path)) 'A stale response must not match a new descriptor.'
    $identity.startedUtc = $currentProcess.StartTime.ToUniversalTime().AddSeconds(-5).ToString('O')
    Write-AtomicRuntimeText -Path $descriptorPath -Text ($identity | ConvertTo-Json) -Overwrite
    Assert-LauncherCheck (-not (Test-RuntimeIdentity -Identity $identity -Root $identityRoot -Role Media -Executable $currentProcess.Path)) 'A matching descriptor must not bypass the real process start time.'

    $lockPath = Join-Path $identityDirectory 'startup.lock'
    $firstLock = Open-RuntimeFileLock -Path $lockPath
    try { Assert-StateRejected { Open-RuntimeFileLock -Path $lockPath } 'Concurrent launcher operations must be serialized.' }
    finally { $firstLock.Dispose() }
    $nextLock = Open-RuntimeFileLock -Path $lockPath
    $nextLock.Dispose()
    Assert-LauncherCheck ($true) 'Releasing the lock must allow the next operation.'

    $exampleRoot = Join-Path $fixtureRoot 'configuration'
    [IO.Directory]::CreateDirectory($exampleRoot) | Out-Null
    $examplePath = Join-Path $exampleRoot 'didanihaa.example.json'
    [IO.File]::WriteAllText($examplePath, '{"publicPort":24510,"futureSetting":true}')
    Initialize-LocalConfiguration -Root $exampleRoot
    [IO.File]::WriteAllText($examplePath, '{"publicPort":24511}')
    Initialize-LocalConfiguration -Root $exampleRoot
    $actualSettings = Get-Content -Raw -LiteralPath (Join-Path $exampleRoot 'didanihaa.json') | ConvertFrom-Json
    Assert-LauncherCheck ($actualSettings.publicPort -eq 24510 -and $actualSettings.futureSetting) 'Configuration initialization must preserve an existing file and unknown settings.'
    Assert-StateRejected { Resolve-OwnedRuntimePath -Root $exampleRoot -RelativePath '..\outside.exe' } 'Binary paths must stay within their runtime root.'
    Assert-LauncherCheck ((Get-RuntimeRootId -Root $identityRoot) -ceq (Get-RuntimeRootId -Root (Join-Path $identityRoot '.'))) 'Root identity must ignore redundant path segments.'

    Write-Output ('Launcher state checks passed: ' + $script:assertionCount + ' assertions.')
} finally {
    $cleanupTarget = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $cleanupTarget.StartsWith($fixtureParent + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped its workspace.' }
    if (Test-Path -LiteralPath $cleanupTarget) { Remove-Item -LiteralPath $cleanupTarget -Recurse -Force }
}
