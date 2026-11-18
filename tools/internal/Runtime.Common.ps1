$ErrorActionPreference = 'Stop'

function Assert-RuntimePath {
    param([string]$Path)
    $entry = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Path))
    while ($null -ne $entry) {
        if ((Test-Path -LiteralPath $entry.FullName) -and
            ((Get-Item -LiteralPath $entry.FullName -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Runtime paths must not contain reparse points.'
        }
        $entry = $entry.Parent
    }
}

function Get-CanonicalRuntimeRoot {
    param([string]$Root)
    $canonical = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
    Assert-RuntimePath -Path $canonical
    return $canonical
}

function Get-RuntimeRootId {
    param([string]$Root)
    $canonical = Get-CanonicalRuntimeRoot -Root $Root
    if ([OperatingSystem]::IsWindows()) { $canonical = $canonical.ToUpperInvariant() }
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical)))
}

function Resolve-OwnedRuntimePath {
    param([string]$Root, [string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathFullyQualified($RelativePath)) { throw 'Expected a root-relative runtime path.' }
    $canonicalRoot = Get-CanonicalRuntimeRoot -Root $Root
    $path = [IO.Path]::GetFullPath((Join-Path $canonicalRoot $RelativePath))
    $prefix = if ([IO.Path]::EndsInDirectorySeparator($canonicalRoot)) { $canonicalRoot } else { $canonicalRoot + [IO.Path]::DirectorySeparatorChar }
    if ([string]::Equals($path, $canonicalRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime path escaped its root.' }
    Assert-RuntimePath -Path $path
    return $path
}

function Open-RuntimeFileLock {
    param([string]$Path, [ValidateRange(0, 10000)][int]$TimeoutMilliseconds = 0)
    Assert-RuntimePath -Path $Path
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try { return [IO.File]::Open($Path, 'OpenOrCreate', 'ReadWrite', 'None') }
        catch [IO.IOException] {
            if ($timer.ElapsedMilliseconds -ge $TimeoutMilliseconds) { throw }
            Start-Sleep -Milliseconds 20
        }
    }
}

function Write-AtomicRuntimeText {
    param([string]$Path, [string]$Text, [switch]$Overwrite)
    Assert-RuntimePath -Path $Path
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $output = [IO.File]::Open($temporary, 'CreateNew', 'Write', 'None')
        try { $output.Write([Text.Encoding]::UTF8.GetBytes($Text)); $output.Flush($true) } finally { $output.Dispose() }
        [IO.File]::Move($temporary, $Path, [bool]$Overwrite)
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}

function Get-DidanihaaRoot {
    $directory = [IO.DirectoryInfo]::new($PSScriptRoot)
    while ($null -ne $directory) {
        if ((Test-Path -LiteralPath (Join-Path $directory.FullName 'didanihaa.json')) -or
            (Test-Path -LiteralPath (Join-Path $directory.FullName 'didanihaa.example.json'))) {
            return Get-CanonicalRuntimeRoot -Root $directory.FullName
        }
        $directory = $directory.Parent
    }
    throw 'Could not find the Didanihaa root configuration.'
}

function Initialize-LocalConfiguration {
    param([string]$Root)
    $configurationPath = Join-Path $Root 'didanihaa.json'
    if (Test-Path -LiteralPath $configurationPath) { return }
    $configurationLock = Open-RuntimeFileLock -Path (Join-Path (Get-LauncherDirectory -Root $Root -Mode Runtime) 'configuration.lock') -TimeoutMilliseconds 2000
    try {
        if (Test-Path -LiteralPath $configurationPath) { return }
        $example = [IO.File]::ReadAllText((Join-Path $Root 'didanihaa.example.json'))
        if (($example | ConvertFrom-Json) -isnot [PSCustomObject]) { throw 'The example configuration must be a JSON object.' }
        Write-AtomicRuntimeText -Path $configurationPath -Text $example
        Write-Host 'Created local didanihaa.json from the portable example.'
    } finally { $configurationLock.Dispose() }
}

function Get-PublishedRuntimeRoot {
    $root = Get-DidanihaaRoot
    if (Test-Path -LiteralPath (Join-Path $root 'Didanihaa.slnx')) {
        $root = Join-Path $root 'publish'
        if (-not (Test-Path -LiteralPath (Join-Path $root 'didanihaa.json'))) {
            throw 'Published output is missing. Run tools\Build-Publish.ps1 first.'
        }
    }
    return Get-CanonicalRuntimeRoot -Root $root
}

function Get-LauncherDirectory {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][ValidateSet('Development', 'Runtime')][string]$Mode
    )
    $relativePath = if ($Mode -eq 'Development') { 'temp\dev' } else { 'temp\runtime' }
    $directory = Join-Path (Get-CanonicalRuntimeRoot -Root $Root) $relativePath
    Assert-RuntimePath -Path $directory
    return $directory
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
        $expectedRoot = Get-CanonicalRuntimeRoot -Root $Root
        if (-not [string]::Equals($expectedRoot, (Get-CanonicalRuntimeRoot -Root $state.root), [StringComparison]::OrdinalIgnoreCase)) {
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
            elseif ($Record.startedUtc -is [DateTimeOffset]) { $Record.startedUtc.UtcDateTime }
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
    $json = @{ root = (Get-CanonicalRuntimeRoot -Root $Root); mode = $Mode; processes = @($Processes) } | ConvertTo-Json -Depth 4
    Write-AtomicRuntimeText -Path $statePath -Text $json -Overwrite
}

function Test-RuntimeIdentity {
    param($Identity, [string]$Root, [ValidateSet('Control', 'Media')][string]$Role, [string]$Executable)
    try {
        $parsedInstance = [Guid]::Empty
        if ($null -eq $Identity -or $Identity.protocol -ne 1 -or $Identity.role -cne $Role -or
            $Identity.rootId -cne (Get-RuntimeRootId -Root $Root) -or
            -not [Guid]::TryParseExact($Identity.instanceId, 'N', [ref]$parsedInstance) -or
            -not [string]::Equals([IO.Path]::GetFullPath($Identity.executablePath), [IO.Path]::GetFullPath($Executable), [StringComparison]::OrdinalIgnoreCase)) { return $false }
        $descriptorPath = Join-Path (Get-LauncherDirectory -Root $Root -Mode Runtime) ($Role.ToLowerInvariant() + '.identity.json')
        Assert-RuntimePath -Path $descriptorPath
        $descriptor = Get-Content -Raw -LiteralPath $descriptorPath | ConvertFrom-Json
        if ($descriptor.protocol -ne $Identity.protocol -or $descriptor.instanceId -cne $Identity.instanceId -or $descriptor.rootId -cne $Identity.rootId -or
            $descriptor.role -cne $Identity.role -or $descriptor.processId -ne $Identity.processId -or
            $descriptor.executablePath -cne $Identity.executablePath -or
            [DateTimeOffset]$descriptor.startedUtc -ne [DateTimeOffset]$Identity.startedUtc) { return $false }
        $running = Get-Process -Id $Identity.processId -ErrorAction Stop
        $startedUtc = [DateTimeOffset]$Identity.startedUtc
        return [Math]::Abs(($running.StartTime.ToUniversalTime() - $startedUtc.UtcDateTime).TotalMilliseconds) -lt 10
    } catch { return $false }
}

function Test-BackendResponse {
    param($Response, [string]$Root, [ValidateSet('Control', 'Media')][string]$Role, [string]$Executable, [int]$ExpectedProcessId = 0)
    if ($null -eq $Response) { return $false }
    $identity = if ($Role -eq 'Control') { $Response.runtime } else { $Response.identity }
    if ($Role -eq 'Media' -and -not $Response.catalogReady) { return $false }
    if ($ExpectedProcessId -gt 0 -and $identity.processId -ne $ExpectedProcessId) { return $false }
    return Test-RuntimeIdentity -Identity $identity -Root $Root -Role $Role -Executable $Executable
}

function Get-LocalJson {
    param([string]$Uri)
    try { return Invoke-RestMethod -Uri $Uri -TimeoutSec 2 -NoProxy } catch { return $null }
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
