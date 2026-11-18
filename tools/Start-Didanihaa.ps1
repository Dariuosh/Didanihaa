[CmdletBinding()]
param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\Runtime.Common.ps1')
$runtimeRoot = Get-PublishedRuntimeRoot
Initialize-LocalConfiguration -Root $runtimeRoot
$configuration = Get-Content -Raw -LiteralPath (Join-Path $runtimeRoot 'didanihaa.json') | ConvertFrom-Json
$controlExecutable = Join-Path $runtimeRoot 'apps\control\Didanihaa.Control.exe'
$mediaExecutable = Resolve-OwnedRuntimePath -Root $runtimeRoot -RelativePath $configuration.hostExecutable
$adminPage = Join-Path $runtimeRoot 'web\admin\index.html'
foreach ($requiredFile in @($controlExecutable, $mediaExecutable, $adminPage)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) { throw ('Published file is missing: ' + $requiredFile + '. Build-publish the application first.') }
}
$controlUrl = 'http://127.0.0.1:{0}' -f $configuration.publicPort
$mediaUrl = 'http://127.0.0.1:{0}' -f $configuration.mediaPort
$logDirectory = Get-LauncherDirectory -Root $runtimeRoot -Mode Runtime
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$tracked = [Collections.Generic.List[object]]::new()

function Start-PublishedProcess {
    param([string]$Role, [string]$Executable, [string]$ReadyUrl)
    $process = Start-Process -FilePath $Executable -ArgumentList @('--root', ('"{0}"' -f $runtimeRoot)) -WorkingDirectory $runtimeRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory ($Role + '.out.log')) -RedirectStandardError (Join-Path $logDirectory ($Role + '.err.log'))
    $process.Refresh()
    $tracked.Add([PSCustomObject]@{ role = $Role; id = $process.Id; executable = [IO.Path]::GetFullPath($Executable); startedUtc = $process.StartTime.ToUniversalTime().ToString('O') })
    Save-LauncherState -Root $runtimeRoot -Mode Runtime -Processes $tracked.ToArray()
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    do {
        $response = Get-LocalJson $ReadyUrl
        if (Test-BackendResponse -Response $response -Root $runtimeRoot -Role $Role -Executable $Executable -ExpectedProcessId $process.Id) {
            Write-Output ($Role + ' is ready.'); return
        }
        $process.Refresh()
        if ($process.HasExited) { break }
        Start-Sleep -Milliseconds 400
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ($Role + ' did not become ready. See ' + $logDirectory + '. Stop the partial startup before retrying.')
}

$startupLock = Open-RuntimeFileLock -Path (Join-Path $logDirectory 'startup.lock') -TimeoutMilliseconds 2000
try {
    foreach ($record in @(Get-LauncherState -Root $runtimeRoot -Mode Runtime)) {
        if ($null -ne (Get-TrackedProcess -Record $record)) { $tracked.Add($record) }
    }
    Save-LauncherState -Root $runtimeRoot -Mode Runtime -Processes $tracked.ToArray()
    $session = Get-LocalJson ($controlUrl + '/admin/api/session')
    if ($null -ne $session -and -not (Test-BackendResponse -Response $session -Root $runtimeRoot -Role Control -Executable $controlExecutable)) {
        throw 'The running Control identity does not match this installation or is from an older version. Stop that instance before updating; it was not reused.'
    }
    if ($null -eq $session) {
        if (Test-LocalPort $configuration.publicPort) { throw ('Control port is occupied: ' + $configuration.publicPort) }
        Start-PublishedProcess -Role 'Control' -Executable $controlExecutable -ReadyUrl ($controlUrl + '/admin/api/session')
    } else { Write-Output 'Reusing the running Control instance.' }

    $health = Get-LocalJson ($mediaUrl + '/_internal/runtime')
    $status = Get-LocalJson ($controlUrl + '/admin/api/status')
    if (Test-BackendResponse -Response $health -Root $runtimeRoot -Role Media -Executable $mediaExecutable) {
        Write-Output 'Reusing the running Media instance.'
    } elseif (Test-LocalPort $configuration.mediaPort) {
        throw 'The Media port is occupied by an instance whose identity could not be verified; it was not reused.'
    } elseif ($status.service.installed -and $status.service.owned) {
        Write-Output 'Windows Service is installed. Start Media from the dashboard if it is stopped.'
    } else {
        if (Test-LocalPort $configuration.mediaPort) { throw ('Media port is occupied: ' + $configuration.mediaPort) }
        Start-PublishedProcess -Role 'Media' -Executable $mediaExecutable -ReadyUrl ($mediaUrl + '/_internal/runtime')
    }
    $page = Invoke-WebRequest -Uri ($controlUrl + '/admin/') -TimeoutSec 5 -UseBasicParsing
    if ($page.StatusCode -ne 200) { throw 'The dashboard page could not be loaded.' }
    foreach ($stylesheet in [regex]::Matches($page.Content, 'href="([^"]+\.css)"')) {
        $stylesheetPath = $stylesheet.Groups[1].Value
        if (-not $stylesheetPath.StartsWith('/') -or $stylesheetPath.StartsWith('//')) { throw 'Unexpected stylesheet URL in the published dashboard.' }
        $css = Invoke-WebRequest -Uri ($controlUrl + $stylesheetPath) -TimeoutSec 5 -UseBasicParsing
        if ($css.StatusCode -ne 200 -or ($css.Headers['Content-Type'] -join ';') -notlike 'text/css*') { throw 'The dashboard stylesheet could not be loaded.' }
    }
    $session = Get-LocalJson ($controlUrl + '/admin/api/session')
    $siteUrl = if (@($session.viewerUrls).Count -gt 0) { ([Uri]$session.viewerUrls[0]).GetLeftPart([UriPartial]::Authority) } else { $controlUrl }
    Write-Output ('Dashboard: ' + $siteUrl + '/admin/')
    Write-Output ('TV: ' + $siteUrl + '/tv/')
    Write-Output ('Local dashboard: ' + $controlUrl + '/admin/')
    Write-Output ('Logs: ' + $logDirectory)
    Write-Output 'This command runs the existing published output; it does not build or install a Windows Service.'
    if (-not $NoBrowser) { Open-DefaultBrowser -Uri ($controlUrl + '/admin/') }
} finally { if ($null -ne $startupLock) { $startupLock.Dispose() } }
