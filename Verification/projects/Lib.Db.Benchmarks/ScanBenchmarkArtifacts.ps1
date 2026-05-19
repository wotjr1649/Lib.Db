param(
    [string[]]$Paths
)

$ErrorActionPreference = "Stop"

$verificationRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$repoRoot = Split-Path $verificationRoot -Parent

if (-not $PSBoundParameters.ContainsKey("Paths") -or $null -eq $Paths -or $Paths.Count -eq 0) {
    $Paths = @(
        (Join-Path $verificationRoot "artifacts\benchmarks"),
        (Join-Path $repoRoot "BenchmarkDotNet.Artifacts"),
        (Join-Path $PSScriptRoot "BenchmarkDotNet.Artifacts")
    )
}

$patterns = @(
    "(?i)\b(connection\s*string|connectionstrings)\b\s*[:=]",
    "(?i)\b(server|data\s+source|initial\s+catalog|database|user\s+id|uid|password|pwd)\s*=",
    "(?i)\b(access[_-]?token|refresh[_-]?token|api[_-]?key|secret)\b\s*[:=]"
)

$textExtensions = @(
    ".txt", ".md", ".csv", ".json", ".xml", ".html", ".log",
    ".out", ".err", ".config", ".props", ".targets"
)

[string[]]$roots = @(
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $resolved = Resolve-Path -LiteralPath $path -ErrorAction SilentlyContinue
        if ($null -ne $resolved) {
            $resolved.Path
        }
    }
)
$roots = $roots | Sort-Object -Unique

if ($roots.Count -eq 0) {
    Write-Output "No benchmark artifact paths found. Run benchmarks or pass -Paths explicitly."
    exit 1
}

$hits = New-Object System.Collections.Generic.HashSet[string]

foreach ($root in $roots) {
    Write-Output "Scanning benchmark artifact path: $root"

    Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction Stop |
        Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() } |
        ForEach-Object {
            $file = $_.FullName
            $content = [System.IO.File]::ReadAllText($file)

            foreach ($pattern in $patterns) {
                if ([System.Text.RegularExpressions.Regex]::IsMatch($content, $pattern)) {
                    [void]$hits.Add($file)
                    break
                }
            }
        }
}

if ($hits.Count -gt 0) {
    Write-Output "Potential benchmark artifact secret pattern paths:"
    $hits | Sort-Object | ForEach-Object { Write-Output $_ }
    exit 1
}

Write-Output "No benchmark artifact secret pattern paths found."
