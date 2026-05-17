param(
    [string]$SkillRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message) | Out-Null
}

function Test-Contains {
    param(
        [string]$Path,
        [string[]]$Terms,
        [string]$Label
    )

    $text = Get-Content -LiteralPath $Path -Raw
    foreach ($term in $Terms) {
        if (-not $text.Contains($term)) {
            Add-Failure "$Label missing required term: $term"
        }
    }
}

if (-not (Test-Path -LiteralPath $SkillRoot)) {
    throw "Skill root not found: $SkillRoot"
}

$skill = Join-Path $SkillRoot 'SKILL.md'
if (-not (Test-Path -LiteralPath $skill)) {
    Add-Failure 'SKILL.md is missing.'
}

$references = Join-Path $SkillRoot 'references'
$tests = Join-Path $SkillRoot 'tests'
foreach ($dir in @($references, $tests)) {
    if (-not (Test-Path -LiteralPath $dir)) {
        Add-Failure "Required directory missing: $dir"
    }
}

$requiredRefs = @(
    'security-guardrails.md',
    'runtime-api.md',
    'mapping-contracts.md',
    'tvpgen-guide.md',
    'examples.md',
    'verification.md'
)

foreach ($ref in $requiredRefs) {
    $path = Join-Path $references $ref
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Failure "Required reference missing: $ref"
    }
}

$mdFiles = Get-ChildItem -LiteralPath $SkillRoot -Recurse -File -Filter '*.md'
foreach ($file in $mdFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $text = [System.IO.File]::ReadAllText($file.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Add-Failure "BOM found: $($file.FullName)"
    }
    if ($text.Contains("`r")) {
        Add-Failure "CRLF/CR found: $($file.FullName)"
    }
    $trailing = Select-String -LiteralPath $file.FullName -Pattern '[ \t]+$'
    if ($trailing) {
        Add-Failure "Trailing whitespace found: $($file.FullName)"
    }
}

if (Test-Path -LiteralPath $skill) {
    $skillText = Get-Content -LiteralPath $skill -Raw
    $skillLines = (Get-Content -LiteralPath $skill).Count
    if ($skillLines -ge 500) {
        Add-Failure "SKILL.md should stay under 500 lines, actual: $skillLines"
    }

    $nameMatch = [regex]::Match($skillText, '(?m)^name:\s*(.+)$')
    if (-not $nameMatch.Success -or $nameMatch.Groups[1].Value.Trim() -notmatch '^[a-z0-9-]{1,64}$') {
        Add-Failure 'Invalid skill name frontmatter.'
    }

    $descriptionMatch = [regex]::Match($skillText, '(?m)^description:\s*(.+)$')
    if (-not $descriptionMatch.Success) {
        Add-Failure 'Missing description frontmatter.'
    }
    else {
        $description = $descriptionMatch.Groups[1].Value.Trim()
        if ($description.Length -eq 0 -or $description.Length -gt 1024 -or $description -match '<[^>]+>') {
            Add-Failure 'Invalid description frontmatter.'
        }
    }

    foreach ($ref in $requiredRefs) {
        if ($skillText -notlike "*references/$ref*" -and $skillText -notlike "*references\$ref*") {
            Add-Failure "SKILL.md does not route to reference: $ref"
        }
    }

    if ($skillText -match '(?m)^\s*-\s*(Bash|Edit|Write)\s*$') {
        Add-Failure 'Broad side-effect tools should not be pre-approved in allowed-tools.'
    }
}

$contentFiles = $mdFiles | Where-Object { $_.FullName -notlike (Join-Path $tests '*') }
$contentText = ($contentFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"

$forbiddenPatterns = @(
    'Lib\.Db v2\.1',
    'User\s+Id\s*=\s*sa',
    'Password\s*=',
    'TrustServerCertificate\s*=\s*True',
    'Server\s*=\s*localhost,1433;Database='
)

foreach ($pattern in $forbiddenPatterns) {
    if ($contentText -match $pattern) {
        Add-Failure "Forbidden content pattern found: $pattern"
    }
}

if ($contentText -notmatch 'allowed-tools.+security boundary' -and $contentText -notmatch 'allowed-tools`?\s+is not a security boundary') {
    Add-Failure 'Skill should state that allowed-tools is not a security boundary.'
}

if (Test-Path -LiteralPath (Join-Path $references 'security-guardrails.md')) {
    Test-Contains (Join-Path $references 'security-guardrails.md') @(
        'RawSqlPolicy',
        'ConnectionSecurityProfile',
        'DenyAllText',
        'DenyWriteText',
        'UseProductionSecurityDefaults',
        'allowed-tools'
    ) 'security-guardrails.md'
}

if (Test-Path -LiteralPath (Join-Path $references 'runtime-api.md')) {
    Test-Contains (Join-Path $references 'runtime-api.md') @(
        'AddLibDb',
        'AddHighPerformanceDb',
        'UseProductionSecurityDefaults',
        'MarsPolicy',
        'EnableObservability'
    ) 'runtime-api.md'
}

if (Test-Path -LiteralPath (Join-Path $references 'mapping-contracts.md')) {
    Test-Contains (Join-Path $references 'mapping-contracts.md') @(
        'CELL_NO',
        'DbDataReader',
        'MonitoredSqlDataReader',
        'DateOnly',
        'TimeOnly'
    ) 'mapping-contracts.md'
}

if (Test-Path -LiteralPath (Join-Path $references 'tvpgen-guide.md')) {
    Test-Contains (Join-Path $references 'tvpgen-guide.md') @(
        '[TvpRow]',
        '[DbResult]',
        'Map(DbDataReader)',
        'Map(SqlDataReader)'
    ) 'tvpgen-guide.md'
}

if (Test-Path -LiteralPath (Join-Path $tests 'scenarios.md')) {
    Test-Contains (Join-Path $tests 'scenarios.md') @(
        'S01',
        'S02',
        'S03',
        'S04',
        'S05',
        'S06'
    ) 'scenarios.md'
}

if ($failures.Count -gt 0) {
    Write-Output 'FAIL'
    foreach ($failure in $failures) {
        Write-Output "- $failure"
    }
    exit 1
}

Write-Output 'PASS'
Write-Output "SkillRoot=$SkillRoot"
Write-Output "MarkdownFiles=$($mdFiles.Count)"
Write-Output "SkillLines=$((Get-Content -LiteralPath $skill).Count)"
Write-Output "References=$($requiredRefs.Count)"
Write-Output 'UnsafeExamples=0'
