param(
    [string[]] $Paths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

if (-not $PSBoundParameters.ContainsKey('Paths') -or $null -eq $Paths -or $Paths.Count -eq 0) {
    $Paths = @(
        (Join-Path $repoRoot 'Verification\artifacts'),
        (Join-Path $repoRoot 'TestResults'),
        (Join-Path $repoRoot 'BenchmarkDotNet.Artifacts'),
        (Join-Path $repoRoot 'artifacts')
    )
}

$patterns = @(
    '(?i)\b(password|pwd)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^;,"''\s<>&]{4,}',
    '(?i)\b(access[_-]?token|refresh[_-]?token|api[_-]?key|client[_-]?secret)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^,"''\s;<>]{8,}',
    '(?i)\b(connection\s*string|connectionstrings|connectionstring)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)(?=[^"''\r\n<>]{0,512}\b(password|pwd)\s*=)[^"''\r\n<>]{8,}',
    '(?i)\b(connection\s*string|connectionstrings|connectionstring)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)(?=[^"''\r\n<>]{0,512}\b(server|data source|address|addr|network address|host)\s*=)(?![^"''\r\n<>]{0,512}\b(server|data source|address|addr|network address|host)\s*=\s*(localhost|127\.0\.0\.1|::1|\.|\(local\)|\(localdb\)(?:\\[^;,\s"''<>]+)?)(?=;|,|\s|$|\)|"|'')[^"''\r\n<>]{0,512})[^"''\r\n<>]{8,}',
    '(?i)\b([A-Z0-9_]*CONNECTION[A-Z0-9_]*|ConnectionStrings__[\w.-]+)\b\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)(?=[^"''\r\n<>]{0,512}\b(server|data source|address|addr|network address|host)\s*=)(?![^"''\r\n<>]{0,512}\b(server|data source|address|addr|network address|host)\s*=\s*(localhost|127\.0\.0\.1|::1|\.|\(local\)|\(localdb\)(?:\\[^;,\s"''<>]+)?)(?=;|,|\s|$|\)|"|'')[^"''\r\n<>]{0,512})[^"''\r\n<>]{8,}'
)

$connectionStringValuePattern = '(?i)(server|data source|address|addr|network address|host)\s*=\s*[^;,"''\r\n<>]+(?:;[^,"''\r\n<>]*)*'
$localServerPattern = '(?i)\b(server|data source|address|addr|network address|host)\s*=\s*(localhost|127\.0\.0\.1|::1|\.|\(local\)|\(localdb\)(?:\\[^;,\s"''<>]+)?)(?=;|,|\s|$|\)|"|'')'

$textExtensions = @(
    '.txt', '.md', '.csv', '.json', '.log',
    '.out', '.err', '.trx',
    '.html', '.htm', '.xml', '.nuspec', '.psmdcp', '.rels',
    '.props', '.targets', '.csproj', '.config',
    '.ps1', '.sql'
)

[string[]] $roots = @(
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
    Write-Output 'No verification artifact paths found. Run verification first or pass -Paths explicitly.'
    exit 1
}

$hits = [System.Collections.Generic.HashSet[string]]::new()

foreach ($root in $roots) {
    Write-Output "Scanning verification artifact path: $root"

    Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction Stop |
        Where-Object {
            $extension = $_.Extension.ToLowerInvariant()
            $name = $_.Name.ToLowerInvariant()
            $textExtensions -contains $extension -or $textExtensions -contains $name
        } |
        ForEach-Object {
            $file = $_.FullName
            $content = [System.IO.File]::ReadAllText($file)

            foreach ($pattern in $patterns) {
                if ([System.Text.RegularExpressions.Regex]::IsMatch($content, $pattern)) {
                    [void] $hits.Add($file)
                    break
                }
            }

            if (-not $hits.Contains($file)) {
                $connectionMatches = [System.Text.RegularExpressions.Regex]::Matches($content, $connectionStringValuePattern)
                foreach ($match in $connectionMatches) {
                    $candidate = $match.Value
                    if ([string]::IsNullOrWhiteSpace($candidate)) {
                        continue
                    }

                    if ([System.Text.RegularExpressions.Regex]::IsMatch($candidate, $localServerPattern)) {
                        continue
                    }

                    [void] $hits.Add($file)
                    break
                }
            }
        }
}

if ($hits.Count -gt 0) {
    Write-Output 'Potential verification artifact secret pattern paths:'
    $hits | Sort-Object | ForEach-Object { Write-Output $_ }
    exit 1
}

Write-Output 'No verification artifact secret pattern paths found.'
