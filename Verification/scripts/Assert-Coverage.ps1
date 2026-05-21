param(
    [Parameter(Mandatory = $true)]
    [string] $CoberturaPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CoberturaPath)) {
    throw "Cobertura file not found: $CoberturaPath"
}

$culture = [System.Globalization.CultureInfo]::InvariantCulture
$readerSettings = [System.Xml.XmlReaderSettings]::new()
$readerSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
$readerSettings.XmlResolver = $null

$document = [System.Xml.XmlDocument]::new()
$document.XmlResolver = $null
$reader = [System.Xml.XmlReader]::Create($CoberturaPath, $readerSettings)
try {
    $document.Load($reader)
}
finally {
    if ($null -ne $reader) {
        $reader.Dispose()
    }
}

$coverage = $document.coverage
if ($null -eq $coverage) {
    throw 'Cobertura root element was not found.'
}

function Convert-CoverageRate {
    param([Parameter(Mandatory = $true)] [object] $Value)
    return [double]::Parse([string] $Value, $culture)
}

function Assert-AtLeast {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [double] $Actual,
        [Parameter(Mandatory = $true)] [double] $Expected
    )

    if ($Actual + 0.0000001 -lt $Expected) {
        throw "$Name expected >= $Expected but was $Actual"
    }
}

function Assert-TargetCoverage {
    param(
        [Parameter(Mandatory = $true)] [object[]] $Classes,
        [Parameter(Mandatory = $true)] [string] $DisplayName,
        [Parameter(Mandatory = $true)] [string] $Prefix
    )

    $targetClasses = @($Classes | Where-Object {
        $name = $_.GetAttribute('name')
        $name -eq $Prefix -or $name.StartsWith("$Prefix/") -or $name.StartsWith("$Prefix+")
    })

    if ($targetClasses.Count -eq 0) {
        throw "$DisplayName target classes were not found with prefix '$Prefix'."
    }

    $lines = @($targetClasses | ForEach-Object { $_.SelectNodes('lines/line') } | Where-Object { $null -ne $_ })
    if ($lines.Count -eq 0) {
        throw "$DisplayName coverage lines were not found."
    }

    $uncoveredLines = @($lines | Where-Object { [int] $_.GetAttribute('hits') -eq 0 })
    if ($uncoveredLines.Count -gt 0) {
        $sample = ($uncoveredLines | Select-Object -First 8 | ForEach-Object { $_.GetAttribute('number') }) -join ', '
        throw "$DisplayName line coverage expected 100%; uncovered lines: $sample"
    }

    $coveredBranches = 0
    $totalBranches = 0
    $partialBranchLines = @()
    foreach ($line in $lines) {
        if ($line.GetAttribute('branch') -ne 'True') {
            continue
        }

        $lineNumber = $line.GetAttribute('number')
        $className = $line.ParentNode.ParentNode.GetAttribute('name')
        $conditionCoverage = $line.GetAttribute('condition-coverage')
        if ([string]::IsNullOrWhiteSpace($conditionCoverage) -or $conditionCoverage -notmatch '\((\d+)/(\d+)\)') {
            throw "$DisplayName branch coverage on $className line $lineNumber could not be parsed from condition-coverage '$conditionCoverage'."
        }

        $lineCoveredBranches = [int] $Matches[1]
        $lineTotalBranches = [int] $Matches[2]
        if ($lineTotalBranches -le 0) {
            throw "$DisplayName branch coverage on $className line $lineNumber reported no branch totals in condition-coverage '$conditionCoverage'."
        }

        $coveredBranches += $lineCoveredBranches
        $totalBranches += $lineTotalBranches
        if ($lineCoveredBranches -ne $lineTotalBranches) {
            $partialBranchLines += "$className line:$lineNumber $lineCoveredBranches/$lineTotalBranches"
        }
    }

    if ($partialBranchLines.Count -gt 0) {
        $sample = ($partialBranchLines | Select-Object -First 8) -join ', '
        throw "$DisplayName branch coverage expected 100%; covered $coveredBranches of $totalBranches branches; partial branch lines: $sample"
    }

    $methods = @($targetClasses | ForEach-Object { $_.SelectNodes('methods/method') } | Where-Object { $null -ne $_ })
    if ($methods.Count -eq 0) {
        throw "$DisplayName coverage methods were not found."
    }

    $uncoveredMethods = @($methods | Where-Object {
        (Convert-CoverageRate $_.GetAttribute('line-rate')) -lt 1.0 -or
        ($_.GetAttribute('branch-rate') -ne '' -and (Convert-CoverageRate $_.GetAttribute('branch-rate')) -lt 1.0)
    })

    if ($uncoveredMethods.Count -gt 0) {
        $sample = ($uncoveredMethods | Select-Object -First 8 | ForEach-Object { $_.GetAttribute('name') }) -join ', '
        throw "$DisplayName method coverage expected 100%; uncovered or partial methods: $sample"
    }

    Write-Host "$DisplayName coverage gate passed."
}

Assert-AtLeast 'Lib.Db overall line coverage' (Convert-CoverageRate $coverage.GetAttribute('line-rate')) 0.80

$classes = @($coverage.SelectNodes('packages/package/classes/class'))
$targets = @(
    @{ DisplayName = 'CacheMaintenanceService'; Prefix = 'Lib.Db.Caching.CacheMaintenanceService' },
    @{ DisplayName = 'SchemaWarmupService'; Prefix = 'Lib.Db.Hosting.SchemaWarmupService' },
    @{ DisplayName = 'QueryCacheExtensions'; Prefix = 'Lib.Db.Extensions.QueryCacheExtensions' },
    @{ DisplayName = 'GeneratedResultMapper<T>'; Prefix = 'Lib.Db.Execution.Binding.GeneratedResultMapper`1' },
    @{ DisplayName = 'ReflectionParameterMapper<T>'; Prefix = 'Lib.Db.Execution.Binding.ReflectionParameterMapper`1' },
    @{ DisplayName = 'TVP ColumnarTvpReader'; Prefix = 'Lib.Db.Execution.Tvp.ColumnarTvpReader' },
    @{ DisplayName = 'TVP ITvpSchemaProvider'; Prefix = 'Lib.Db.Execution.Tvp.ITvpSchemaProvider' },
    @{ DisplayName = 'TVP LibDbTvpValue'; Prefix = 'Lib.Db.Execution.Tvp.LibDbTvpValue' },
    @{ DisplayName = 'TVP RuntimeTvpDataReader'; Prefix = 'Lib.Db.Execution.Tvp.RuntimeTvpDataReader' },
    @{ DisplayName = 'TVP SqlDataRecordTvpEnumerable'; Prefix = 'Lib.Db.Execution.Tvp.SqlDataRecordTvpEnumerable' },
    @{ DisplayName = 'TVP TvpAccessorCache'; Prefix = 'Lib.Db.Execution.Tvp.TvpAccessorCache' },
    @{ DisplayName = 'TVP TvpAccessorRegistry'; Prefix = 'Lib.Db.Execution.Tvp.TvpAccessorRegistry' },
    @{ DisplayName = 'TVP TvpColumnShape'; Prefix = 'Lib.Db.Execution.Tvp.TvpColumnShape' },
    @{ DisplayName = 'TVP TvpMappingBuilder<T>'; Prefix = 'Lib.Db.Execution.Tvp.TvpMappingBuilder`1' },
    @{ DisplayName = 'TVP TvpMappingRegistry'; Prefix = 'Lib.Db.Execution.Tvp.TvpMappingRegistry' },
    @{ DisplayName = 'TVP TvpOptions'; Prefix = 'Lib.Db.Execution.Tvp.TvpOptions' },
    @{ DisplayName = 'TVP TvpRowAccessorCache'; Prefix = 'Lib.Db.Execution.Tvp.TvpRowAccessorCache' },
    @{ DisplayName = 'TVP TvpRowBinding'; Prefix = 'Lib.Db.Execution.Tvp.TvpRowBinding' },
    @{ DisplayName = 'TVP TvpSchemaFingerprint'; Prefix = 'Lib.Db.Execution.Tvp.TvpSchemaFingerprint' },
    @{ DisplayName = 'TVP TvpSchemaProvider'; Prefix = 'Lib.Db.Execution.Tvp.TvpSchemaProvider' },
    @{ DisplayName = 'TVP TvpShape'; Prefix = 'Lib.Db.Execution.Tvp.TvpShape' },
    @{ DisplayName = 'TVP TvpShape<T>'; Prefix = 'Lib.Db.Execution.Tvp.TvpShape`1' },
    @{ DisplayName = 'TVP TvpShapeBuilder<T>'; Prefix = 'Lib.Db.Execution.Tvp.TvpShapeBuilder`1' },
    @{ DisplayName = 'TVP TvpTypeName'; Prefix = 'Lib.Db.Execution.Tvp.TvpTypeName' },
    @{ DisplayName = 'TVP TypedColumnBuffer<T>'; Prefix = 'Lib.Db.Execution.Tvp.TypedColumnBuffer`1' }
)

foreach ($target in $targets) {
    Assert-TargetCoverage -Classes $classes -DisplayName $target.DisplayName -Prefix $target.Prefix
}

Write-Host 'All Lib.Db v2.3.0 coverage gates passed.'
