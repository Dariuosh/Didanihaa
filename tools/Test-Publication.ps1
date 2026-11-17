[CmdletBinding()]
param(
    [string]$Revision,
    [switch]$CheckHistory
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$problems = [Collections.Generic.List[object]]::new()
$checked = 0

function Add-PublicationProblem {
    param([string]$File, [string]$Rule, [string]$Object)
    $problems.Add([PSCustomObject]@{ File = $File; Rule = $Rule; Object = $Object })
}
function Test-PublicationText {
    param([string]$Content, [string]$File, [string]$Object)
    # Report rule/file only. Never print matching secret values.
    $rules = @{
        'private-key' = '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
        'github-token' = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})'
        'aws-access-key' = '(?:AKIA|ASIA)[A-Z0-9]{16}'
        'credential-in-url' = 'https?://[^\s/"'']+:[^\s/"'']+@'
        'personal-windows-home' = '(?i)[a-z]:[\\/]Users[\\/](?!Public[\\/]|Default[\\/])[a-z0-9_.-]+[\\/]'
        'personal-workspace' = '(?i)[a-z]:[\\/]My Projects[\\/]'
        'local-session-notes' = '(?m)session_id\s*[=:]\s*\d+'
    }
    foreach ($rule in $rules.GetEnumerator()) {
        if ([regex]::IsMatch($Content, $rule.Value)) { Add-PublicationProblem -File $File -Rule $rule.Key -Object $Object }
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is required for publication checks.' }
    if ($Revision) {
        $commitId = git rev-parse --verify --end-of-options ($Revision + '^{commit}') 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'The requested revision does not exist.' }
        $Revision = $commitId
        $files = @(git ls-tree -r --name-only $Revision)
    } else { $files = @(git ls-files --cached --others --exclude-standard | Sort-Object -Unique) }
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked files.' }

    $forbiddenPaths = '^(?:data|publish|temp(?!/\.gitkeep$)|node_modules|\.agents|\.codex|\.aws)(?:/|$)|^docs/(?!licenses(?:/|$))|^AGENTS\.md$|(?:^|/)(?:bin|obj|node_modules|TestResults)/|(?:^|/)(?:didanihaa(?:\.local)?\.json|\.env(?!\.(?:example|sample)$)(?:\..*)?)$'
    $forbiddenExtensions = '\.(?:db(?:-[a-z]+)?|sqlite[0-9]?(?:-[a-z]+)?|pfx|p12|pem|key|jks|keystore|exe|dll|pdb|msi|log|dmp|mp[34]|m4[av]|mkv|webm|avi|mov|mp[e]?g|wm[av]|flac|wav|aac|ogg|opus|aiff?|mts|m2ts|vob|3gp|zip|7z|rar|nupkg)$'
    foreach ($file in $files) {
        if ($file -match $forbiddenPaths -or $file -match $forbiddenExtensions) {
            Add-PublicationProblem -File $file -Rule 'private-or-generated-file' -Object $Revision
            continue
        }
        if ($file -match '\.(?:woff2?|png|jpe?g|gif|ico)$') { continue }
        if ($Revision) { $content = (git show ($Revision + ':' + $file)) -join "`n"; if ($LASTEXITCODE -ne 0) { throw ('Unable to read tracked file: ' + $file) } }
        elseif (Test-Path -LiteralPath $file -PathType Leaf) { $content = [IO.File]::ReadAllText((Join-Path $repositoryRoot $file)) }
        else { continue }
        Test-PublicationText -Content $content -File $file -Object $Revision; $checked++
    }
    foreach ($privatePath in @('didanihaa.json', '.env', 'data/catalog.db', 'publish/data/catalog.db', 'temp/automation/private.txt', 'docs/local/session.md', 'docs/STATE.md', 'docs/PLAN.md', 'docs/DECISIONS.md', 'docs/design/COLORS.md', 'docs/new-note.md')) {
        git check-ignore --quiet --no-index -- $privatePath
        if ($LASTEXITCODE -ne 0) { Add-PublicationProblem -File $privatePath -Rule 'missing-ignore-protection' -Object '' }
    }
    git check-ignore --quiet --no-index -- docs/licenses/dotnet-MIT.txt
    if ($LASTEXITCODE -ne 1) { Add-PublicationProblem -File 'docs/licenses/dotnet-MIT.txt' -Rule 'required-license-ignored-or-ignore-check-failed' -Object '' }
    if ($CheckHistory) {
        $reference = if ($Revision) { $Revision } else { 'HEAD' }
        $objects = @(git rev-list --objects $reference)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate publication history.' }
        # A reused blob can have several historical names; rev-list reports only one.
        $historicalPaths = @(git log --format= --name-only $reference | Where-Object { $_ } | Sort-Object -Unique)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate historical paths.' }
        foreach ($file in $historicalPaths) {
            if ($file -match $forbiddenPaths -or $file -match $forbiddenExtensions) { Add-PublicationProblem -File $file -Rule 'private-path-in-history' -Object $reference }
        }
        foreach ($entry in $objects) {
            $parts = $entry -split ' ', 2; if ($parts.Count -ne 2) { continue }
            $objectId = $parts[0]; $file = $parts[1]
            $objectType = git cat-file -t $objectId
            if ($objectType -ne 'blob') { continue }
            if ($file -match $forbiddenPaths -or $file -match $forbiddenExtensions) { Add-PublicationProblem -File $file -Rule 'private-file-in-history' -Object $objectId; continue }
            if ($file -match '\.(?:woff2?|png|jpe?g|gif|ico)$') { continue }
            $content = (git cat-file blob $objectId) -join "`n"
            if ($LASTEXITCODE -ne 0) { throw ('Unable to inspect historical blob: ' + $objectId) }
            Test-PublicationText -Content $content -File $file -Object $objectId
        }
    }
    if ($problems.Count) {
        $problems | Format-Table -AutoSize | Out-String | Write-Output
        throw ('Publication check failed: ' + $problems.Count + ' finding(s). No matching values were printed.')
    }
    Write-Output ('Publication policy passed for ' + $checked + ' text files. Revision: ' + $(if ($Revision) { $Revision } else { 'working tree' }) + '.')
    Write-Output 'Also scan secrets with Gitleaks. This policy check does not replace a secret scanner, license review, or author-metadata review.'
} finally { Pop-Location }
