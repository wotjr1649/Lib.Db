param(
    [string[]] $Paths,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

$textExtensions = @(
    '.txt', '.md', '.csv', '.json', '.log',
    '.out', '.err', '.trx',
    '.html', '.htm', '.xml', '.nuspec', '.psmdcp', '.rels',
    '.props', '.targets', '.csproj', '.config',
    '.ps1', '.sql'
)

$markerPatterns = @(
    [pscustomobject]@{
        Marker = 'Password'
        Pattern = '(?i)\b(password|pwd)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^;,"''\s<>&]{4,}'
    },
    [pscustomobject]@{
        Marker = 'Token'
        Pattern = '(?i)\b(access[_-]?token|refresh[_-]?token|tokenvalue)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^,"''\s;<>]{8,}'
    },
    [pscustomobject]@{
        Marker = 'ApiKey'
        Pattern = '(?i)\b(api[_-]?key)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^,"''\s;<>]{8,}'
    },
    [pscustomobject]@{
        Marker = 'ClientSecret'
        Pattern = '(?i)\b(client[_-]?secret|client secret)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^,"''\s;<>]{8,}'
    },
    [pscustomobject]@{
        Marker = 'Bearer'
        Pattern = '(?i)\bbearer\s+[a-z0-9._~+/=-]{12,}'
    },
    [pscustomobject]@{
        Marker = 'Sas'
        Pattern = '(?i)\b(sastoken|sharedaccesssignature|shared access signature)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^,"''\s;<>]{12,}'
    },
    [pscustomobject]@{
        Marker = 'Sas'
        Pattern = '(?i)(\?|&)(sv|sig)=[^"''\s<>]{3,}.*(\?|&)sig=[^"''\s<>]{12,}'
    },
    [pscustomobject]@{
        Marker = 'Sas'
        Pattern = '(?i)(\?|&)sig=[^"''\s<>]{12,}.*(\?|&)sv=[^"''\s<>]{3,}'
    },
    [pscustomobject]@{
        Marker = 'ConnectionString'
        Pattern = '(?i)\b(connection\s*string|connectionstrings|connectionstring)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)(?=[^"''\r\n<>]{0,512}\b(password|pwd)\s*=)[^"''\r\n<>]{8,}'
    },
    [pscustomobject]@{
        Marker = 'ConnectionString'
        Pattern = '(?i)\b(connection\s*string|connectionstrings|connectionstring)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)(?=[^"''\r\n<>]{0,512}\b(server|data source|address|addr|network address|host)\s*=)(?![^"''\r\n<>]{0,512}\b(server|data source|address|addr|network address|host)\s*=\s*(localhost|127\.0\.0\.1|::1|\.|\(local\)|\(localdb\)(?:\\[^;,\s"''<>]+)?)(?=;|,|\s|$|\)|"|'')[^"''\r\n<>]{0,512})[^"''\r\n<>]{8,}'
    },
    [pscustomobject]@{
        Marker = 'ConnectionString'
        Pattern = '(?i)\b([A-Z0-9_]*CONNECTION[A-Z0-9_]*|ConnectionStrings__[\w.-]+)\b\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)(?=[^"''\r\n<>]{0,512}\b(server|data source|address|addr|network address|host)\s*=)(?![^"''\r\n<>]{0,512}\b(server|data source|address|addr|network address|host)\s*=\s*(localhost|127\.0\.0\.1|::1|\.|\(local\)|\(localdb\)(?:\\[^;,\s"''<>]+)?)(?=;|,|\s|$|\)|"|'')[^"''\r\n<>]{0,512})[^"''\r\n<>]{8,}'
    },
    [pscustomobject]@{
        Marker = 'SqlParameterValue'
        Pattern = '(?i)\b(sqlparametervalue|parameter[_-]?value)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^,"''\s;<>]{3,}'
    },
    [pscustomobject]@{
        Marker = 'RowValue'
        Pattern = '(?i)\b(row[_-]?value)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^,"''\s;<>]{3,}'
    },
    [pscustomobject]@{
        Marker = 'CachePayload'
        Pattern = '(?i)\b(cache[_-]?payload|payloadvalue)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3})\b)[^,"''\s;<>]{3,}'
    },
    [pscustomobject]@{
        Marker = 'TenantUserIdentifier'
        Pattern = '(?i)\b(tenant[_-]?id|user[_-]?id|email[_-]?address)\b["'']?\s*[:=]\s*["'']?(?!(placeholder|redacted|null|false|true|\*+|\.{3}|userId|tenantId|customerId|productId|currentUser\.Id|request\.|\{|\$)\b)[^,"''\s;<>]{1,}'
    }
)

$connectionStringValuePattern = '(?i)(server|data source|address|addr|network address|host)\s*=\s*[^;,"''\r\n<>]+(?:;[^,"''\r\n<>]*)*'
$localServerPattern = '(?i)\b(server|data source|address|addr|network address|host)\s*=\s*(localhost|127\.0\.0\.1|::1|\.|\(local\)|\(localdb\)(?:\\[^;,\s"''<>]+)?)(?=;|,|\s|$|\)|"|'')'

function Resolve-ArtifactRoots {
    param([string[]] $CandidatePaths)

    [string[]] $roots = @(
        foreach ($path in $CandidatePaths) {
            if ([string]::IsNullOrWhiteSpace($path)) {
                continue
            }

            $resolved = Resolve-Path -LiteralPath $path -ErrorAction SilentlyContinue
            if ($null -ne $resolved) {
                $resolved.Path
            }
        }
    )

    $roots | Sort-Object -Unique
}

function ConvertTo-DisplayPath {
    param([string] $Path)

    if ($Path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $Path.Substring($repoRoot.Length).TrimStart('\', '/')
    }

    $Path
}

function Add-Hit {
    param(
        [System.Collections.Generic.HashSet[string]] $Keys,
        [System.Collections.Generic.List[object]] $Hits,
        [string] $Path,
        [string] $Marker
    )

    $displayPath = ConvertTo-DisplayPath -Path $Path
    $key = $displayPath + "`t" + $Marker
    if ($Keys.Add($key)) {
        $Hits.Add([pscustomobject]@{
            Path = $displayPath
            Marker = $Marker
        }) | Out-Null
    }
}

function Test-CodeLikeTenantUserIdentifierLine {
    param([string] $Line)

    $trimmed = $Line.Trim()
    $codeLikePatterns = @(
        '^\.(With|Sql|Procedure)\(',
        '^(var|string|int|long|Guid)\s+\w+\s*=',
        'new\s+\{[^}]*\b(tenant[_-]?id|user[_-]?id|email[_-]?address)\b\s*=',
        '=>\s*.*\b(tenant[_-]?id|user[_-]?id|email[_-]?address)\b'
    )

    foreach ($pattern in $codeLikePatterns) {
        if ([System.Text.RegularExpressions.Regex]::IsMatch($trimmed, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Read-ArtifactText {
    param([string] $Path)

    $stream = [System.IO.FileStream]::new(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite)

    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
        try {
            $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Find-VerificationArtifactSecretMarkers {
    param([string[]] $Roots)

    $hitKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $hits = [System.Collections.Generic.List[object]]::new()

    foreach ($root in $Roots) {
        Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction Stop |
            Where-Object {
                $extension = $_.Extension.ToLowerInvariant()
                $name = $_.Name.ToLowerInvariant()
                $textExtensions -contains $extension -or $textExtensions -contains $name
            } |
            ForEach-Object {
                $file = $_.FullName
                $content = Read-ArtifactText -Path $file

                foreach ($markerPattern in $markerPatterns) {
                    if ($markerPattern.Marker -eq 'TenantUserIdentifier') {
                        $lines = $content -split '\r?\n'
                        foreach ($line in $lines) {
                            if ([System.Text.RegularExpressions.Regex]::IsMatch($line, $markerPattern.Pattern) -and
                                -not (Test-CodeLikeTenantUserIdentifierLine -Line $line)) {
                                Add-Hit -Keys $hitKeys -Hits $hits -Path $file -Marker $markerPattern.Marker
                                break
                            }
                        }

                        continue
                    }

                    if ([System.Text.RegularExpressions.Regex]::IsMatch($content, $markerPattern.Pattern)) {
                        Add-Hit -Keys $hitKeys -Hits $hits -Path $file -Marker $markerPattern.Marker
                    }
                }

                $connectionMatches = [System.Text.RegularExpressions.Regex]::Matches($content, $connectionStringValuePattern)
                foreach ($match in $connectionMatches) {
                    $candidate = $match.Value
                    if ([string]::IsNullOrWhiteSpace($candidate)) {
                        continue
                    }

                    if ([System.Text.RegularExpressions.Regex]::IsMatch($candidate, $localServerPattern)) {
                        continue
                    }

                    Add-Hit -Keys $hitKeys -Hits $hits -Path $file -Marker 'ConnectionStringValue'
                    break
                }
            }
    }

    $hits
}

function Format-Hits {
    param([object[]] $Hits)

    $Hits |
        Sort-Object Path, Marker -Unique |
        ForEach-Object { "Path: $($_.Path); Marker: $($_.Marker)" }
}

function Invoke-SelfTest {
    $tempRoot = Join-Path (Join-Path $repoRoot 'Verification\artifacts') ('scanner-selftest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    try {
        $secretValues = @(
            for ($index = 0; $index -lt 14; $index++) {
                'fixture-' + $index.ToString([Globalization.CultureInfo]::InvariantCulture) + '-' + [Guid]::NewGuid().ToString('N')
            }
        )

        $fixture = @(
            "Password=$($secretValues[0])",
            "AccessToken=$($secretValues[1])",
            "ApiKey=$($secretValues[2])",
            "ClientSecret=$($secretValues[3])",
            "Bearer $($secretValues[4])",
            "SharedAccessSignature=$($secretValues[5])",
            "SignedUrl=https://account.blob.core.windows.net/container/blob.txt?sv=2025-01-01&sig=$($secretValues[6])",
            "SignedUrlReversed=https://account.blob.core.windows.net/container/blob.txt?sig=$($secretValues[7])&sv=2025-01-01",
            "SqlParameterValue=$($secretValues[8])",
            "RowValue=$($secretValues[9])",
            "CachePayload=$($secretValues[10])",
            "TenantId=$($secretValues[11])",
            "UserId=$($secretValues[12])",
            "EmailAddress=$($secretValues[13])"
        )

        $fixturePath = Join-Path $tempRoot 'secret-like.log'
        [System.IO.File]::WriteAllLines($fixturePath, $fixture)

        $hits = @(Find-VerificationArtifactSecretMarkers -Roots @($tempRoot))
        if ($hits.Count -eq 0) {
            Write-Error 'Scanner self-test failed: secret-like fixture did not produce a redacted failure marker.'
        }

        $expectedMarkers = @(
            'Password',
            'Token',
            'ApiKey',
            'ClientSecret',
            'Bearer',
            'Sas',
            'SqlParameterValue',
            'RowValue',
            'CachePayload',
            'TenantUserIdentifier'
        )

        $actualMarkers = @($hits | ForEach-Object { $_.Marker } | Sort-Object -Unique)
        foreach ($expectedMarker in $expectedMarkers) {
            if ($actualMarkers -notcontains $expectedMarker) {
                Write-Error "Scanner self-test failed: expected marker was not reported: $expectedMarker"
            }
        }

        $rendered = (Format-Hits -Hits $hits) -join [Environment]::NewLine
        foreach ($secretValue in $secretValues) {
            if ($rendered.Contains($secretValue, [StringComparison]::Ordinal)) {
                Write-Error 'Scanner self-test failed: rendered output echoed a fixture value.'
            }
        }

        Write-Output 'Scanner self-test passed. Secret-like fixture values were not echoed.'
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

if (-not $PSBoundParameters.ContainsKey('Paths') -or $null -eq $Paths -or $Paths.Count -eq 0) {
    $Paths = @(
        (Join-Path $repoRoot 'Verification\artifacts'),
        (Join-Path $repoRoot 'TestResults'),
        (Join-Path $repoRoot 'BenchmarkDotNet.Artifacts'),
        (Join-Path $repoRoot 'artifacts')
    )
}

[string[]] $roots = @(Resolve-ArtifactRoots -CandidatePaths $Paths)

if ($roots.Count -eq 0) {
    Write-Output 'No verification artifact paths found. Run verification first or pass -Paths explicitly.'
    exit 1
}

foreach ($root in $roots) {
    Write-Output "Scanning verification artifact path: $root"
}

$hits = @(Find-VerificationArtifactSecretMarkers -Roots $roots)

if ($hits.Count -gt 0) {
    Write-Output 'Potential verification artifact secret markers:'
    Format-Hits -Hits $hits
    exit 1
}

Write-Output 'No verification artifact secret markers found.'
