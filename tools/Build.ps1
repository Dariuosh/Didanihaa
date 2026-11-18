[CmdletBinding()]
param([switch]$NoRestore, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'internal\Runtime.Common.ps1')
Initialize-LocalConfiguration -Root $workspaceRoot
$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot 'temp\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:VSTEST_ARTIFACTS_DIRECTORY = Join-Path $workspaceRoot 'temp\test-results'
Push-Location -LiteralPath $workspaceRoot
try {
    if (-not $NoRestore) {
        dotnet restore Didanihaa.slnx --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
    }
    dotnet build Didanihaa.slnx --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipTests) {
        dotnet test Didanihaa.slnx --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
} finally { Pop-Location }
