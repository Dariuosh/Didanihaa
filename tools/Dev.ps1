[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$WebOnly,
    [ValidateRange(1024, 65535)][int]$VitePort = 5211
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\Runtime.Common.ps1')
$workspaceRoot = Get-DidanihaaRoot
Initialize-LocalConfiguration -Root $workspaceRoot
$configuration = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'didanihaa.json') | ConvertFrom-Json
if ($VitePort -in @($configuration.publicPort, $configuration.mediaPort)) { throw 'Vite and backend ports must be different.' }
$node = (Get-Command node.exe -ErrorAction Stop).Source
$logDirectory = Get-LauncherDirectory -Root $workspaceRoot -Mode Development
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

$tracked = [Collections.Generic.List[object]]::new()
$controlUrl = 'http://localhost:{0}' -f $configuration.publicPort
$mediaUrl = 'http://localhost:{0}' -f $configuration.mediaPort
$viteUrl = 'http://127.0.0.1:{0}' -f $VitePort
$expectedControlRoot = $workspaceRoot
$controlExecutable = Join-Path $workspaceRoot 'src\Didanihaa.Control\bin\Debug\net11.0\Didanihaa.Control.exe'
$mediaExecutable = Join-Path $workspaceRoot 'src\Didanihaa.Media\bin\Debug\net11.0\Didanihaa.Host.exe'
$expectedControlExecutable = $controlExecutable
$expectedMediaExecutable = $mediaExecutable
if ($WebOnly) {
    $activeSession = Get-LocalJson ($controlUrl + '/admin/api/session')
    $allowedRoots = @($workspaceRoot, (Join-Path $workspaceRoot 'publish'))
    if ($null -eq $activeSession -or $activeSession.root -notin $allowedRoots) {
        throw 'WebOnly needs this workspace or its published Control already running. Start tools\Start-Didanihaa.ps1 first.'
    }
    $expectedControlRoot = $activeSession.root
    if ($expectedControlRoot -ne $workspaceRoot) {
        $publishedConfiguration = Get-Content -Raw -LiteralPath (Join-Path $expectedControlRoot 'didanihaa.json') | ConvertFrom-Json
        $expectedControlExecutable = Join-Path $expectedControlRoot 'apps\control\Didanihaa.Control.exe'
        $expectedMediaExecutable = Resolve-OwnedRuntimePath -Root $expectedControlRoot -RelativePath $publishedConfiguration.hostExecutable
    }
}

function Test-ControlReady {
    param([int]$ExpectedProcessId = 0)
    $session = Get-LocalJson ($controlUrl + '/admin/api/session')
    return Test-BackendResponse -Response $session -Root $expectedControlRoot -Role Control -Executable $expectedControlExecutable -ExpectedProcessId $ExpectedProcessId
}
function Test-MediaReady {
    param([int]$ExpectedProcessId = 0)
    $health = Get-LocalJson ($mediaUrl + '/_internal/runtime')
    return Test-BackendResponse -Response $health -Root $expectedControlRoot -Role Media -Executable $expectedMediaExecutable -ExpectedProcessId $ExpectedProcessId
}
function Test-ViteReady {
    try {
        $page = Invoke-WebRequest -Uri ($viteUrl + '/admin/') -UseBasicParsing -TimeoutSec 2
        $css = Invoke-WebRequest -Uri ($viteUrl + '/admin/admin.css?direct') -UseBasicParsing -TimeoutSec 2
        $session = Get-LocalJson ($viteUrl + '/admin/api/session')
        return $page.StatusCode -eq 200 -and $css.StatusCode -eq 200 -and
            ($css.Headers['Content-Type'] -join ';') -like 'text/css*' -and
            (Test-BackendResponse -Response $session -Root $expectedControlRoot -Role Control -Executable $expectedControlExecutable)
    } catch { return $false }
}
function Start-DevelopmentProcess {
    param([string]$Role, [string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory)
    $stdout = Join-Path $logDirectory ($Role + '.out.log')
    $stderr = Join-Path $logDirectory ($Role + '.err.log')
    $startedProcess = Start-Process -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $startedProcess.Refresh()
    $tracked.Add([PSCustomObject]@{ role = $Role; id = $startedProcess.Id; executable = [IO.Path]::GetFullPath($Executable); startedUtc = $startedProcess.StartTime.ToUniversalTime().ToString('O') })
    Save-LauncherState -Root $workspaceRoot -Mode Development -Processes $tracked.ToArray()
    return $startedProcess
}
function Wait-DevelopmentReady {
    param([string]$Role, [scriptblock]$Check, $Process)
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    do {
        if (& $Check) { Write-Output ($Role + ' is ready.'); return }
        if ($null -ne $Process) {
            $Process.Refresh()
            if ($Process.HasExited) { break }
        }
        Start-Sleep -Milliseconds 400
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ($Role + ' did not become ready. See logs in ' + $logDirectory + '. Run tools\Stop-Dev.ps1 before retrying.')
}

$startupLock = Open-RuntimeFileLock -Path (Join-Path $logDirectory 'startup.lock') -TimeoutMilliseconds 2000
try {
    foreach ($record in @(Get-LauncherState -Root $workspaceRoot -Mode Development)) {
        if ($null -ne (Get-TrackedProcess -Record $record)) { $tracked.Add($record) }
    }
    $controlReady = Test-ControlReady
    $mediaReady = Test-MediaReady
    if ($WebOnly -and (-not $controlReady -or -not $mediaReady)) { throw 'WebOnly requires ready backends with matching runtime identities. Update older published backends before using this preview.' }
    $webRoot = Join-Path $workspaceRoot 'src\Didanihaa.WebUI'
    $viteEntry = Join-Path $webRoot 'node_modules\vite\bin\vite.js'
    $outputsReady = (Test-Path -LiteralPath $controlExecutable) -and (Test-Path -LiteralPath $mediaExecutable) -and
        (Test-Path -LiteralPath $viteEntry) -and (Test-Path -LiteralPath (Join-Path $webRoot 'dist\admin\index.html'))
    if ($WebOnly) { $outputsReady = Test-Path -LiteralPath $viteEntry }
    if (-not $outputsReady) { throw 'Development build outputs are missing. Run tools\Build.ps1 first.' }
    if (-not $controlReady) {
        if (Test-LocalPort $configuration.publicPort) { throw ('Control port is occupied by another instance: ' + $configuration.publicPort) }
        $controlProcess = Start-DevelopmentProcess -Role 'Control' -Executable $controlExecutable -Arguments @('--root', ('"{0}"' -f $workspaceRoot)) -WorkingDirectory $workspaceRoot
        Wait-DevelopmentReady -Role 'Control' -Check { Test-ControlReady -ExpectedProcessId $controlProcess.Id } -Process $controlProcess
    } else { Write-Output 'Reusing the running Control instance.' }
    if (-not $mediaReady) {
        if (Test-LocalPort $configuration.mediaPort) { throw ('Media port is occupied by another application: ' + $configuration.mediaPort) }
        $mediaProcess = Start-DevelopmentProcess -Role 'Media' -Executable $mediaExecutable -Arguments @('--root', ('"{0}"' -f $workspaceRoot)) -WorkingDirectory $workspaceRoot
        Wait-DevelopmentReady -Role 'Media' -Check { Test-MediaReady -ExpectedProcessId $mediaProcess.Id } -Process $mediaProcess
    } else { Write-Output 'Reusing the running Media instance.' }
    if (-not (Test-ViteReady)) {
        if (Test-LocalPort $VitePort) { throw ('Vite port is occupied or its page/CSS/API is unhealthy: ' + $VitePort) }
        $viteProcess = Start-DevelopmentProcess -Role 'Vite' -Executable $node -Arguments @(('"{0}"' -f $viteEntry), '--host', '127.0.0.1', '--port', [string]$VitePort) -WorkingDirectory $webRoot
        Wait-DevelopmentReady -Role 'Vite' -Check { Test-ViteReady } -Process $viteProcess
    } else { Write-Output 'Reusing the running Vite instance.' }
    Write-Output ('Dashboard: ' + $viteUrl + '/admin/')
    Write-Output ('TV preview: ' + $viteUrl + '/tv/')
    Write-Output ('Logs: ' + $logDirectory)
    Write-Output 'Stop script-owned servers with tools\Stop-Dev.ps1.'
    if (-not $NoBrowser) { Open-DefaultBrowser -Uri ($viteUrl + '/admin/') }
} finally { if ($null -ne $startupLock) { $startupLock.Dispose() } }
