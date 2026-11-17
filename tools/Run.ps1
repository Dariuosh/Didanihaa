[CmdletBinding()]
param([ValidateSet('Control','Media')][string]$Mode = 'Control')
$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$projectName = if ($Mode -eq 'Control') { 'Didanihaa.Control' } else { 'Didanihaa.Host' }
$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot 'temp\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project (Join-Path $workspaceRoot ('src\{0}\{0}.csproj' -f $projectName)) --no-launch-profile -- --root $workspaceRoot
