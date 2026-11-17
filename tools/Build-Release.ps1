[CmdletBinding()]
param([string]$Revision = 'HEAD', [string]$OutputDirectory = 'temp/releases/artifacts')
$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$previousPackages = $env:NUGET_PACKAGES

function Invoke-ReleaseGit {
    param([string[]]$GitArguments)
    $result = @(git @GitArguments)
    if ($LASTEXITCODE -ne 0) { throw ('Git failed: ' + $GitArguments[0]) }
    return $result
}
function Copy-ReleaseDirectory {
    param([string]$Source, [string]$Destination)
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Release input contains a reparse point.' }
        if ($file.Extension -in @('.pdb', '.map')) { continue }
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
        $target = Join-Path $Destination $relative
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

Push-Location -LiteralPath $workspaceRoot
try {
    if (@(Invoke-ReleaseGit -GitArguments @('status', '--porcelain=v1')).Count) {
        throw 'Commit source changes before building a release. Local runtime data is not release input.'
    }
    $commit = [string](Invoke-ReleaseGit -GitArguments @('rev-parse', '--verify', '--end-of-options', ($Revision + '^{commit}')))
    $mainReference = 'refs/heads/main'
    git show-ref --verify --quiet $mainReference
    if ($LASTEXITCODE -ne 0) { $mainReference = 'refs/remotes/origin/main' }
    git merge-base --is-ancestor $commit $mainReference
    if ($LASTEXITCODE -ne 0) { throw 'The release revision must belong to public main.' }
    $projectXml = (Invoke-ReleaseGit -GitArguments @('show', ($commit + ':Directory.Build.props'))) -join "`n"
    $version = ([xml]$projectXml).Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'A semantic release Version is required in Directory.Build.props.' }
    $packageJson = ((Invoke-ReleaseGit -GitArguments @('show', ($commit + ':src/Didanihaa.Web/package.json'))) -join "`n") | ConvertFrom-Json
    if ($packageJson.version -ne $version) { throw '.NET and web package versions differ.' }
    $outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) }
        else { [IO.Path]::GetFullPath((Join-Path $workspaceRoot $OutputDirectory)) }
    $packageName = 'Didanihaa-v' + $version + '-win-x64'
    $archivePath = Join-Path $outputRoot ($packageName + '.zip')
    $checksumPath = Join-Path $outputRoot 'SHA256SUMS'
    if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) { throw 'Release output already exists; choose an empty output directory.' }
    $workRoot = Join-Path $workspaceRoot ('temp/releases/work/' + [guid]::NewGuid().ToString('N'))
    $sourceRoot = Join-Path $workRoot 'source'
    $stageRoot = Join-Path $workRoot $packageName
    [IO.Directory]::CreateDirectory($workRoot) | Out-Null
    [IO.Directory]::CreateDirectory($stageRoot) | Out-Null
    Invoke-ReleaseGit -GitArguments @('init', '--quiet', $sourceRoot) | Out-Null
    Invoke-ReleaseGit -GitArguments @('-C', $sourceRoot, 'remote', 'add', 'origin', 'https://github.com/Dariuosh/Didanihaa.git') | Out-Null
    # Fetch the selected public commit only; never copy the private local archive.
    Invoke-ReleaseGit -GitArguments @('-C', $sourceRoot, 'fetch', '--quiet', '--no-tags', $workspaceRoot, $commit) | Out-Null
    Invoke-ReleaseGit -GitArguments @('-C', $sourceRoot, 'checkout', '--quiet', '--detach', 'FETCH_HEAD') | Out-Null
    if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $workspaceRoot 'temp/nuget' }
    Push-Location -LiteralPath $sourceRoot
    try {
        & ./tools/Test-Publication.ps1 -Revision HEAD -CheckHistory
        Push-Location -LiteralPath (Join-Path $sourceRoot 'src/Didanihaa.Web')
        try {
            npm ci --ignore-scripts --no-audit --no-fund --cache (Join-Path $workspaceRoot 'temp/npm-cache')
            if ($LASTEXITCODE -ne 0) { throw 'Frontend locked restore failed.' }
        } finally { Pop-Location }
        & ./tools/Build.ps1
        & ./tools/Build-Publish.ps1 -ContinuousIntegration
    } finally { Pop-Location }
    $publishedRoot = Join-Path $sourceRoot 'publish'
    foreach ($directory in @('apps', 'web', 'tools', 'third-party')) {
        Copy-ReleaseDirectory -Source (Join-Path $publishedRoot $directory) -Destination (Join-Path $stageRoot $directory)
    }
    foreach ($notice in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $publishedRoot $notice) -Destination $stageRoot
    }
    $example = Get-Content -LiteralPath (Join-Path $sourceRoot 'didanihaa.example.json') -Raw | ConvertFrom-Json
    $example.hostExecutable = 'apps/host/Didanihaa.Host.exe'
    $example.webDirectory = 'web'
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $stageRoot 'didanihaa.example.json'), ($example | ConvertTo-Json), $utf8)
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'tools/Portable-README.md') -Destination (Join-Path $stageRoot 'README.md')
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'tools/Portable-README.md') -Destination (Join-Path $stageRoot 'tools/README.md') -Force
    $metadata = @{ version = $version; commit = $commit; runtime = 'win-x64'; selfContained = $true }
    [IO.File]::WriteAllText((Join-Path $stageRoot 'version.json'), ($metadata | ConvertTo-Json), $utf8)
    $forbiddenFiles = @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Where-Object {
        $_.Name -eq 'didanihaa.json' -or $_.Extension -match '^\.(?:db|sqlite[0-9]?|log|pdb|map|mp4|mp3|wav|mkv)$'
    })
    if ($forbiddenFiles.Count) { throw 'Private runtime data or debug output entered the release package.' }
    foreach ($required in @('apps/control/Didanihaa.Control.exe','apps/host/Didanihaa.Host.exe','web/assets/fonts/OFL.txt','tools/Start-Didanihaa.ps1','didanihaa.example.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stageRoot $required) -PathType Leaf)) { throw ('Required package file is missing: ' + $required) }
    }
    [IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    Compress-Archive -LiteralPath $stageRoot -DestinationPath $archivePath -CompressionLevel Optimal
    $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksumPath, ($checksum + '  ' + $packageName + ".zip`n"), $utf8)
    Write-Output ('Release archive: ' + $archivePath)
    Write-Output ('Checksum: ' + $checksumPath)
    Write-Output ('Source commit: ' + $commit)
} finally {
    $env:NUGET_PACKAGES = $previousPackages
    Pop-Location
}
