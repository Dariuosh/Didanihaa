[CmdletBinding()]
param([switch]$FrameworkDependent, [switch]$NoRestore, [switch]$ContinuousIntegration, [string]$OutputDirectory = 'publish')
$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'internal\Runtime.Common.ps1')
Initialize-LocalConfiguration -Root $workspaceRoot
$publishRoot = Resolve-OwnedRuntimePath -Root $workspaceRoot -RelativePath $OutputDirectory
$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot 'temp\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
Push-Location -LiteralPath $workspaceRoot
try {
    # Loaded executables cannot safely be replaced on Windows. Keep build and run separate.
    $loadedApplications = @(Get-Process -Name 'Didanihaa', 'Didanihaa.Host', 'Didanihaa.Control' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path.StartsWith($publishRoot + '\', [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    if ($loadedApplications.Count -gt 0) { throw 'Published applications are running. Stop them before Build-Publish; stop a registered Media service from the dashboard first.' }
    foreach ($application in @(@('Didanihaa.Media','host'), @('Didanihaa.Control','control'))) {
        $projectPath = Join-Path $workspaceRoot ('src\{0}\{0}.csproj' -f $application[0])
        $arguments = @('publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '--self-contained', $selfContained, '-p:RestoreLockedMode=true',
            '-o', (Join-Path $publishRoot ('apps\' + $application[1])))
        if ($NoRestore) { $arguments += '--no-restore' }
        if ($ContinuousIntegration) { $arguments += '-p:ContinuousIntegrationBuild=true' }
        dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw ('Publish failed: ' + $application[0]) }
    }
    $launcherArguments = @('publish', 'src/Didanihaa.Launcher/Didanihaa.Launcher.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', $selfContained, '-p:RestoreLockedMode=true', '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableSingleFileAnalyzer=false', '-p:DebugType=None', '-o', $publishRoot)
    if ($NoRestore) { $launcherArguments += '--no-restore' }
    if ($ContinuousIntegration) { $launcherArguments += '-p:ContinuousIntegrationBuild=true' }
    dotnet @launcherArguments
    if ($LASTEXITCODE -ne 0) { throw 'Publishing Didanihaa.exe failed.' }
    New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot 'web'), (Join-Path $publishRoot 'data'), (Join-Path $publishRoot 'temp'), (Join-Path $publishRoot 'tools') | Out-Null
    Copy-Item -Path (Join-Path $workspaceRoot 'src\Didanihaa.WebUI\dist\*') -Destination (Join-Path $publishRoot 'web') -Recurse -Force
    $configPath = Join-Path $publishRoot 'didanihaa.json'
    $sample = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'didanihaa.example.json') | ConvertFrom-Json
    $sample.hostExecutable = 'apps/host/Didanihaa.Host.exe'
    $sample.webDirectory = 'web'
    [IO.File]::WriteAllText((Join-Path $publishRoot 'didanihaa.example.json'), ($sample | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if (-not (Test-Path -LiteralPath $configPath)) {
        $configuration = Get-Content -Raw -LiteralPath (Join-Path $workspaceRoot 'didanihaa.json') | ConvertFrom-Json
        $configuration.hostExecutable = 'apps/host/Didanihaa.Host.exe'
        $configuration.webDirectory = 'web'
        [IO.File]::WriteAllText($configPath, ($configuration | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $publishRoot 'tools\internal') | Out-Null
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'tools\Start-Didanihaa.ps1'), (Join-Path $workspaceRoot 'tools\Stop-Didanihaa.ps1'), (Join-Path $workspaceRoot 'tools\Enable-LanAccess.ps1') -Destination (Join-Path $publishRoot 'tools') -Force
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'tools\internal\Runtime.Common.ps1') -Destination (Join-Path $publishRoot 'tools\internal') -Force
    foreach ($obsoleteLauncher in @('Start-Didanihaa.ps1','Stop-Didanihaa.ps1','Launcher.Common.ps1')) {
        $obsoletePath = Join-Path $publishRoot $obsoleteLauncher
        if (Test-Path -LiteralPath $obsoletePath) { Remove-Item -LiteralPath $obsoletePath }
    }
    Copy-Item -LiteralPath (Join-Path $workspaceRoot 'tools\README.md') -Destination (Join-Path $publishRoot 'tools') -Force
    foreach ($notice in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) {
        $noticePath = Join-Path $workspaceRoot $notice
        if (Test-Path -LiteralPath $noticePath) { Copy-Item -LiteralPath $noticePath -Destination $publishRoot -Force }
    }
    $thirdPartyRoot = Join-Path $publishRoot 'third-party'
    New-Item -ItemType Directory -Force -Path $thirdPartyRoot | Out-Null
    Copy-Item -Path (Join-Path $workspaceRoot 'docs\licenses\*') -Destination $thirdPartyRoot -Force
    # NuGet packages and self-contained runtimes can carry additional notices.
    # Resolve the actual restored versions and package folders; never copy a machine path into the package.
    foreach ($application in @('Didanihaa.Media', 'Didanihaa.Control', 'Didanihaa.Launcher')) {
        $assetsPath = Join-Path $workspaceRoot ('src\' + $application + '\obj\project.assets.json')
        $assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json
        $packageKeys = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
        foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
            foreach ($dependency in $framework.Value.downloadDependencies) {
                if ($dependency.version -match '^\[([^,]+),\s*\1\]$') { $packageKeys += ($dependency.name + '/' + $Matches[1]) }
            }
        }
        foreach ($package in ($packageKeys | Sort-Object -Unique)) {
            foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
                $packageDirectory = Join-Path $folder $package.ToLowerInvariant()
                if (-not (Test-Path -LiteralPath $packageDirectory -PathType Container)) { continue }
                $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(?:LICENSE(?:\.[a-z]+)?|(?:THIRD[-_]PARTY[-_])?NOTICES?(?:\.[a-z]+)?)$' })
                if ($notices.Count) {
                    $destination = Join-Path $thirdPartyRoot ('nuget/' + $package)
                    New-Item -ItemType Directory -Force -Path $destination | Out-Null
                    $notices | Copy-Item -Destination $destination -Force
                }
                break
            }
        }
    }
    Write-Output ('Portable output: ' + $publishRoot)
    Write-Output 'Open Didanihaa.exe to start the published site.'
} finally { Pop-Location }
