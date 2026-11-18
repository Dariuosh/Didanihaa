$ErrorActionPreference = 'Stop'

function Get-DidanihaaRoot {
    $directory = [IO.DirectoryInfo]::new($PSScriptRoot)
    while ($null -ne $directory) {
        if ((Test-Path -LiteralPath (Join-Path $directory.FullName 'didanihaa.json')) -or
            (Test-Path -LiteralPath (Join-Path $directory.FullName 'didanihaa.example.json'))) {
            return $directory.FullName.TrimEnd('\')
        }
        $directory = $directory.Parent
    }
    throw 'Could not find the Didanihaa root configuration.'
}

function Initialize-LocalConfiguration {
    param([string]$Root)
    $configurationPath = Join-Path $Root 'didanihaa.json'
    if (-not (Test-Path -LiteralPath $configurationPath)) {
        Copy-Item -LiteralPath (Join-Path $Root 'didanihaa.example.json') -Destination $configurationPath
        Write-Host 'Created local didanihaa.json from the portable example.'
    }
}

function Get-PublishedRuntimeRoot {
    $root = Get-DidanihaaRoot
    if (Test-Path -LiteralPath (Join-Path $root 'Didanihaa.slnx')) {
        $root = Join-Path $root 'publish'
        if (-not (Test-Path -LiteralPath (Join-Path $root 'didanihaa.json'))) {
            throw 'Published output is missing. Run tools\Build-Publish.ps1 first.'
        }
    }
    return [IO.Path]::GetFullPath($root).TrimEnd('\')
}

function Get-LauncherDirectory {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][ValidateSet('Development', 'Runtime')][string]$Mode
    )
    $relativePath = if ($Mode -eq 'Development') { 'temp\dev' } else { 'temp\runtime' }
    return Join-Path $Root $relativePath
}

function Get-LauncherState {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][ValidateSet('Development', 'Runtime')][string]$Mode
    )
    $statePath = Join-Path (Get-LauncherDirectory -Root $Root -Mode $Mode) 'processes.json'
    # Old published launchers stored their state in temp/dev. Read it only until
    # the first save to temp/runtime; even an empty new state is authoritative.
    if ($Mode -eq 'Runtime' -and -not (Test-Path -LiteralPath $statePath)) {
        $statePath = Join-Path $Root 'temp\dev\processes.json'
    }
    if (-not (Test-Path -LiteralPath $statePath)) { return @() }
    try {
        $state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
        $expectedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
        if (-not [string]::Equals($expectedRoot, [IO.Path]::GetFullPath($state.root).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The process state belongs to another root.'
        }
        $storedMode = $state.PSObject.Properties['mode']
        if ($storedMode -and $storedMode.Value -ne $Mode) { throw 'The process state belongs to another launcher mode.' }
        if ($state.processes -isnot [array]) { throw 'The process list is missing or invalid.' }
        return @($state.processes)
    } catch { throw ($Mode + ' launcher process state could not be read: ' + $statePath + '. ' + $_.Exception.Message) }
}

function Get-TrackedProcess {
    param($Record)
    if ($Record.role -notin @('Control', 'Media', 'Vite')) { return $null }
    $trackedProcess = Get-Process -Id $Record.id -ErrorAction SilentlyContinue
    if ($null -eq $trackedProcess) { return $null }
    try {
        $samePath = [string]::Equals($trackedProcess.Path, $Record.executable, [StringComparison]::OrdinalIgnoreCase)
        $expectedStart = if ($Record.startedUtc -is [DateTime]) { $Record.startedUtc.ToUniversalTime() }
            else { [DateTime]::Parse($Record.startedUtc, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime() }
        $sameStart = [Math]::Abs(($trackedProcess.StartTime.ToUniversalTime() - $expectedStart).TotalMilliseconds) -lt 10
        if ($samePath -and $sameStart) { return $trackedProcess }
    } catch { }
    return $null
}

function Save-LauncherState {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][ValidateSet('Development', 'Runtime')][string]$Mode,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Processes
    )
    $directory = Get-LauncherDirectory -Root $Root -Mode $Mode
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $statePath = Join-Path $directory 'processes.json'
    $json = @{ root = [IO.Path]::GetFullPath($Root).TrimEnd('\'); mode = $Mode; processes = @($Processes) } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($statePath + '.new', $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath ($statePath + '.new') -Destination $statePath -Force
}

function Get-LocalJson {
    param([string]$Uri)
    try { return Invoke-RestMethod -Uri $Uri -TimeoutSec 2 } catch { return $null }
}

function Test-LocalPort {
    param([int]$Port)
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $attempt = $client.ConnectAsync('127.0.0.1', $Port)
        return $attempt.Wait(500) -and $client.Connected
    } catch { return $false } finally { $client.Dispose() }
}

function Open-DefaultBrowser {
    param([string]$Uri)
    Start-Process -FilePath $Uri
}
