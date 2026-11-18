[CmdletBinding()]
param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\Runtime.Common.ps1')
$runtimeRoot = Get-PublishedRuntimeRoot
Initialize-LocalConfiguration -Root $runtimeRoot
$configuration = Get-Content -Raw -LiteralPath (Join-Path $runtimeRoot 'didanihaa.json') | ConvertFrom-Json
$controlExecutable = Join-Path $runtimeRoot 'apps\control\Didanihaa.Control.exe'
$mediaExecutable = Join-Path $runtimeRoot 'apps\host\Didanihaa.Host.exe'
$adminPage = Join-Path $runtimeRoot 'web\admin\index.html'
foreach ($requiredFile in @($controlExecutable, $mediaExecutable, $adminPage)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) { throw ('Published file is missing: ' + $requiredFile + '. Build-publish the application first.') }
}
$controlUrl = 'http://localhost:{0}' -f $configuration.publicPort
$mediaUrl = 'http://localhost:{0}' -f $configuration.mediaPort
$logDirectory = Join-Path $runtimeRoot 'temp\dev'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$startupLock = $null
try { $startupLock = [IO.File]::Open((Join-Path $logDirectory 'startup.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
catch { throw 'Another startup is in progress. Try again after it finishes.' }
$tracked = [Collections.Generic.List[object]]::new()
foreach ($record in @(Get-DevelopmentState -Root $runtimeRoot)) {
    if ($null -ne (Get-TrackedDevelopmentProcess -Record $record)) { $tracked.Add($record) }
}

function Start-PublishedProcess {
    param([string]$Role, [string]$Executable, [string]$ReadyUrl)
    $process = Start-Process -FilePath $Executable -ArgumentList @('--root', ('"{0}"' -f $runtimeRoot)) -WorkingDirectory $runtimeRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory ($Role + '.out.log')) -RedirectStandardError (Join-Path $logDirectory ($Role + '.err.log'))
    $process.Refresh()
    $tracked.Add([PSCustomObject]@{ role = $Role; id = $process.Id; executable = [IO.Path]::GetFullPath($Executable); startedUtc = $process.StartTime.ToUniversalTime().ToString('O') })
    Save-DevelopmentState -Root $runtimeRoot -Processes $tracked.ToArray()
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    do {
        $response = Get-DevelopmentJson $ReadyUrl
        if ($null -ne $response) {
            if (($Role -eq 'Control' -and $response.root -eq $runtimeRoot) -or
                ($Role -eq 'Media' -and $response.service -eq 'Didanihaa.Media' -and $response.catalogReady)) {
                Write-Output ($Role + ' is ready.'); return
            }
        }
        $process.Refresh()
        if ($process.HasExited) { break }
        Start-Sleep -Milliseconds 400
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ($Role + ' did not become ready. See ' + $logDirectory + '. Stop the partial startup before retrying.')
}

try {
    $session = Get-DevelopmentJson ($controlUrl + '/admin/api/session')
    if ($null -ne $session -and $session.root -ne $runtimeRoot) {
        throw 'Control belongs to another runtime root. Stop development servers before starting the published site.'
    }
    if ($null -eq $session) {
        if (Test-LocalPort $configuration.publicPort) { throw ('Control port is occupied: ' + $configuration.publicPort) }
        Start-PublishedProcess -Role 'Control' -Executable $controlExecutable -ReadyUrl ($controlUrl + '/admin/api/session')
    } else { Write-Output 'Reusing the running Control instance.' }

    $health = Get-DevelopmentJson ($mediaUrl + '/health')
    $status = Get-DevelopmentJson ($controlUrl + '/admin/api/status')
    if ($null -ne $health -and $health.service -eq 'Didanihaa.Media' -and $health.catalogReady) {
        Write-Output 'Reusing the running Media instance.'
    } elseif ($status.service.installed) {
        Write-Output 'Windows Service is installed. Start Media from the dashboard if it is stopped.'
    } else {
        if (Test-LocalPort $configuration.mediaPort) { throw ('Media port is occupied: ' + $configuration.mediaPort) }
        Start-PublishedProcess -Role 'Media' -Executable $mediaExecutable -ReadyUrl ($mediaUrl + '/health')
    }
    $page = Invoke-WebRequest -Uri ($controlUrl + '/admin/') -TimeoutSec 5 -UseBasicParsing
    if ($page.StatusCode -ne 200) { throw 'The dashboard page could not be loaded.' }
    foreach ($stylesheet in [regex]::Matches($page.Content, 'href="([^"]+\.css)"')) {
        $stylesheetPath = $stylesheet.Groups[1].Value
        if (-not $stylesheetPath.StartsWith('/') -or $stylesheetPath.StartsWith('//')) { throw 'Unexpected stylesheet URL in the published dashboard.' }
        $css = Invoke-WebRequest -Uri ($controlUrl + $stylesheetPath) -TimeoutSec 5 -UseBasicParsing
        if ($css.StatusCode -ne 200 -or ($css.Headers['Content-Type'] -join ';') -notlike 'text/css*') { throw 'The dashboard stylesheet could not be loaded.' }
    }
    $session = Get-DevelopmentJson ($controlUrl + '/admin/api/session')
    $siteUrl = if (@($session.viewerUrls).Count -gt 0) { ([Uri]$session.viewerUrls[0]).GetLeftPart([UriPartial]::Authority) } else { $controlUrl }
    Write-Output ('Dashboard: ' + $siteUrl + '/admin/')
    Write-Output ('TV: ' + $siteUrl + '/tv/')
    Write-Output ('Local dashboard: ' + $controlUrl + '/admin/')
    Write-Output ('Logs: ' + $logDirectory)
    Write-Output 'This command runs the existing published output; it does not build or install a Windows Service.'
    if (-not $NoBrowser) { Open-DevelopmentBrowser -Uri ($siteUrl + '/admin/') }
} finally { if ($null -ne $startupLock) { $startupLock.Dispose() } }
