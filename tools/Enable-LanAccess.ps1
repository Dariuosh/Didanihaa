[CmdletBinding(SupportsShouldProcess)]
param([string]$InterfaceAlias)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\Launcher.Common.ps1')
$runtimeRoot = Get-PublishedRuntimeRoot
$configuration = Get-Content -Raw -LiteralPath (Join-Path $runtimeRoot 'didanihaa.json') | ConvertFrom-Json
$program = Join-Path $runtimeRoot 'apps\control\Didanihaa.Control.exe'
if (-not (Test-Path -LiteralPath $program)) { throw 'Build the published application first.' }
$interfaces = @(Get-NetIPConfiguration | Where-Object {
    $_.IPv4DefaultGateway -and (-not $InterfaceAlias -or $_.InterfaceAlias -eq $InterfaceAlias)
})
if ($interfaces.Count -ne 1) { throw 'Choose one connected LAN interface with -InterfaceAlias.' }
$interface = $interfaces[0]
$addresses = @($interface.IPv4Address.IPAddress | Where-Object { $_ -match '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)' })
if ($addresses.Count -eq 0) { throw 'The selected interface has no private LAN IPv4 address.' }
$port = [int]$configuration.publicPort
$ruleName = 'Didanihaa.LAN.TCP.' + $port
$description = 'Allow ' + $program + ' TCP ' + $port + ' on ' + $interface.InterfaceAlias + ' (' + ($addresses -join ', ') + ') from LocalSubnet only.'
if (-not $PSCmdlet.ShouldProcess($description, 'Create or update Windows Firewall rule')) { return }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $shellPath = (Get-Process -Id $PID).Path
    $escapedScript = $PSCommandPath.Replace('"','\"')
    $escapedInterface = $interface.InterfaceAlias.Replace('"','\"')
    try {
        $helper = Start-Process -FilePath $shellPath -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $escapedScript),'-InterfaceAlias',('"{0}"' -f $escapedInterface))
    } catch {
        $resultDirectory = Join-Path $runtimeRoot 'temp'
        New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
        $failure = @{ success = $false; phase = 'elevation'; error = $_.Exception.Message }
        [IO.File]::WriteAllText((Join-Path $resultDirectory 'lan-access-result.json'),($failure | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        throw
    }
    if ($helper.ExitCode -ne 0) { throw ('Firewall helper failed. See ' + (Join-Path $runtimeRoot 'temp\lan-access-result.json')) }
    Write-Output $description
    return
}
try {
    $parameters = @{
        Direction = 'Inbound'; Action = 'Allow'; Enabled = 'True'; Profile = 'Any'
        Protocol = 'TCP'; LocalPort = $port; Program = $program
        LocalAddress = $addresses; RemoteAddress = 'LocalSubnet'; InterfaceAlias = $interface.InterfaceAlias
        EdgeTraversalPolicy = 'Block'
    }
    $displayName = 'Didanihaa LAN (TCP ' + $port + ')'
    if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) {
        Set-NetFirewallRule -Name $ruleName -NewDisplayName $displayName @parameters
    } else {
        New-NetFirewallRule -Name $ruleName -DisplayName $displayName -Group 'Didanihaa LAN' @parameters | Out-Null
    }
    $result = @{ success = $true; ruleName = $ruleName; program = $program; port = $port; localAddresses = $addresses; interface = $interface.InterfaceAlias; remoteAddresses = 'LocalSubnet' }
} catch {
    $result = @{ success = $false; error = $_.Exception.Message }
    throw
} finally {
    $resultDirectory = Join-Path $runtimeRoot 'temp'
    New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
    [IO.File]::WriteAllText((Join-Path $resultDirectory 'lan-access-result.json'),($result | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
}
Write-Output $description
