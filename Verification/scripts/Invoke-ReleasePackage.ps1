param(
    [string] $ArtifactsDirectory = 'Verification\artifacts\release-package',
    [string] $PackageVersion = '',
    [bool] $AllowUnsigned = $true,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repoRoot 'Lib.Db\Lib.Db.csproj'
$verificationArtifactsRoot = Join-Path $repoRoot 'Verification\artifacts'
$artifactScanner = Join-Path $PSScriptRoot 'Scan-VerificationArtifacts.ps1'
$secretLikeStatusPattern = '(?i)(password|pwd|token|secret|api[_-]?key|connection\s*string|credential|authorization|sas[_-]?token)\s*[:=]|(?i)(server|data\s+source|address|addr|network\s+address)\s*=\s*[^;\s]+;.*(database|initial\s+catalog|user\s+id|uid|password|pwd|encrypt|trustservercertificate|application\s+name)\s*='

function Resolve-RepoChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ChildPath
    )

    if ([string]::IsNullOrWhiteSpace($ChildPath)) {
        throw 'Repository child path cannot be empty.'
    }

    if ([System.IO.Path]::IsPathRooted($ChildPath)) {
        throw "Absolute paths are not allowed: $ChildPath"
    }

    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ChildPath))
    $rootWithSeparator = $repoRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    if (-not $fullPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path resolves outside the repository root: $ChildPath"
    }

    return $fullPath
}

function Assert-PathUnderDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $PathValue,
        [Parameter(Mandatory = $true)]
        [string] $Directory,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $fullPath = [System.IO.Path]::GetFullPath($PathValue).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $directoryWithSeparator = $fullDirectory + [System.IO.Path]::DirectorySeparatorChar

    if ([string]::Equals($fullPath, $fullDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must resolve under Verification artifacts, not to the artifacts root."
    }

    if (-not ($fullPath + [System.IO.Path]::DirectorySeparatorChar).StartsWith(
            $directoryWithSeparator,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must resolve under Verification artifacts."
    }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [string[]] $Arguments = @(),
        [switch] $CaptureOutput
    )

    if ($CaptureOutput) {
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "$FilePath failed with exit code $exitCode.`n$($output -join [Environment]::NewLine)"
        }

        return ($output -join [Environment]::NewLine).Trim()
    }

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Read-XmlDocument {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $document = [System.Xml.XmlDocument]::new()
    $document.PreserveWhitespace = $false
    $document.Load($Path)
    return $document
}

function Get-XmlChildElement {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNode] $Node,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    return $Node.SelectSingleNode("./*[local-name()='$Name']")
}

function Get-XmlChildText {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNode] $Node,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $child = Get-XmlChildElement -Node $Node -Name $Name
    if ($null -eq $child) {
        return $null
    }

    return $child.InnerText.Trim()
}

function Get-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument] $ProjectDocument,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $nodes = $ProjectDocument.SelectNodes("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='$Name']")
    foreach ($node in $nodes) {
        if (-not [string]::IsNullOrWhiteSpace($node.InnerText)) {
            return $node.InnerText.Trim()
        }
    }

    throw "Project property was not found: $Name"
}

function Resolve-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectVersion,
        [string] $OverrideVersion = ''
    )

    if ([string]::IsNullOrWhiteSpace($OverrideVersion)) {
        return $ProjectVersion
    }

    $trimmedVersion = $OverrideVersion.Trim()
    if ($trimmedVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([-][0-9A-Za-z.-]+)?$') {
        throw "PackageVersion must be a SemVer version without the v prefix: $trimmedVersion"
    }

    if (-not [string]::Equals($trimmedVersion, $ProjectVersion, [System.StringComparison]::Ordinal)) {
        throw "PackageVersion override must match project Version. Project=$ProjectVersion, Override=$trimmedVersion"
    }

    return $trimmedVersion
}

function Get-ProjectPackageReferences {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument] $ProjectDocument
    )

    $references = [System.Collections.Generic.List[object]]::new()
    $nodes = $ProjectDocument.SelectNodes("/*[local-name()='Project']/*[local-name()='ItemGroup']/*[local-name()='PackageReference']")
    foreach ($node in $nodes) {
        $includeAttribute = $node.Attributes['Include']
        if ($null -eq $includeAttribute -or [string]::IsNullOrWhiteSpace($includeAttribute.Value)) {
            continue
        }

        $version = $null
        $versionAttribute = $node.Attributes['Version']
        if ($null -ne $versionAttribute -and -not [string]::IsNullOrWhiteSpace($versionAttribute.Value)) {
            $version = $versionAttribute.Value.Trim()
        }
        else {
            $versionNode = $node.SelectSingleNode("./*[local-name()='Version']")
            if ($null -ne $versionNode -and -not [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
                $version = $versionNode.InnerText.Trim()
            }
        }

        if ([string]::IsNullOrWhiteSpace($version)) {
            throw "PackageReference is missing Version: $($includeAttribute.Value)"
        }

        $references.Add([pscustomobject]@{
            Id = $includeAttribute.Value.Trim()
            Version = $version
        })
    }

    return @($references)
}

function Get-NuspecDependencyEntries {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNode] $MetadataNode
    )

    $groups = @($MetadataNode.SelectNodes("./*[local-name()='dependencies']/*[local-name()='group']"))
    $targetFrameworkGroups = @($groups | Where-Object {
            $targetFramework = $_.Attributes['targetFramework']
            $null -ne $targetFramework -and -not [string]::IsNullOrWhiteSpace($targetFramework.Value)
        })

    if ($targetFrameworkGroups.Count -ne 1) {
        throw "Expected exactly one dependency targetFramework group in package nuspec, found $($targetFrameworkGroups.Count)."
    }

    $dependencies = [System.Collections.Generic.List[object]]::new()
    $dependencyNodes = $targetFrameworkGroups[0].SelectNodes("./*[local-name()='dependency']")
    foreach ($dependencyNode in $dependencyNodes) {
        $idAttribute = $dependencyNode.Attributes['id']
        $versionAttribute = $dependencyNode.Attributes['version']
        if ($null -eq $idAttribute -or [string]::IsNullOrWhiteSpace($idAttribute.Value)) {
            throw 'Package dependency is missing id.'
        }

        if ($null -eq $versionAttribute -or [string]::IsNullOrWhiteSpace($versionAttribute.Value)) {
            throw "Package dependency is missing version: $($idAttribute.Value)"
        }

        $dependencies.Add([pscustomobject]@{
            Id = $idAttribute.Value.Trim()
            Version = $versionAttribute.Value.Trim()
        })
    }

    return @($dependencies)
}

function Assert-PackageRepositoryCommitMatchesHead {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryCommit,
        [Parameter(Mandatory = $true)]
        [string] $Head
    )

    if ($Head -notmatch '^[0-9a-fA-F]{40}$') {
        throw "HEAD is not a full Git commit SHA: $Head"
    }

    if ($RepositoryCommit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Package repository commit does not match HEAD. Expected full HEAD $Head but found '$RepositoryCommit'."
    }

    if (-not [string]::Equals($RepositoryCommit, $Head, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Package repository commit does not match HEAD. Expected $Head but found $RepositoryCommit."
    }
}

function Assert-RepositoryStatusClean {
    param(
        [AllowEmptyCollection()]
        [string[]] $StatusLines = @()
    )

    $changes = @($StatusLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($changes.Count -eq 0) {
        return
    }

    $preview = @($changes | Select-Object -First 20 | ForEach-Object { ConvertTo-SafeRepositoryStatusLine -StatusLine $_ })
    $more = if ($changes.Count -gt $preview.Count) { " plus $($changes.Count - $preview.Count) more change(s)" } else { '' }
    throw "Repository has uncommitted source changes. Commit or remove changes before release packaging:`n - $($preview -join "`n - ")$more"
}

function ConvertTo-SafeRepositoryStatusLine {
    param([string] $StatusLine)

    if ([string]::IsNullOrWhiteSpace($StatusLine)) {
        return $StatusLine
    }

    if ($StatusLine -match $secretLikeStatusPattern) {
        $prefix = if ($StatusLine.Length -ge 2) { $StatusLine.Substring(0, 2) } else { '??' }
        return "$prefix <redacted-path>"
    }

    return $StatusLine
}

function Get-RepositoryStatusLines {
    $statusOutput = & git @('-C', $repoRoot, 'status', '--porcelain=v1', '--untracked-files=all') 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $safeOutput = @($statusOutput | ForEach-Object { ConvertTo-SafeRepositoryStatusLine -StatusLine $_.ToString() })
        throw "git status failed with exit code $exitCode.`n$($safeOutput -join [Environment]::NewLine)"
    }

    return @($statusOutput | ForEach-Object { $_.ToString() })
}

function Assert-PackageDependenciesMatchProject {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument] $ProjectDocument,
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNode] $MetadataNode
    )

    $projectDependencies = @(Get-ProjectPackageReferences -ProjectDocument $ProjectDocument)
    $packageDependencies = @(Get-NuspecDependencyEntries -MetadataNode $MetadataNode)

    $projectKeys = @($projectDependencies |
        Sort-Object -Property Id |
        ForEach-Object { "$($_.Id)|$($_.Version)" })
    $packageKeys = @($packageDependencies |
        Sort-Object -Property Id |
        ForEach-Object { "$($_.Id)|$($_.Version)" })

    if ($projectKeys.Count -ne $packageKeys.Count) {
        throw "Package dependency count does not match project PackageReference count. Project=$($projectKeys.Count), Package=$($packageKeys.Count)."
    }

    for ($index = 0; $index -lt $projectKeys.Count; $index++) {
        if (-not [string]::Equals($projectKeys[$index], $packageKeys[$index], [System.StringComparison]::Ordinal)) {
            throw "Package dependency does not match project PackageReference. Expected '$($projectKeys[$index])' but found '$($packageKeys[$index])'."
        }
    }
}

function Test-OnlyAcceptedUnsignedNuGetFailure {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowEmptyCollection()]
        [string[]] $Output
    )

    $lines = @($Output | ForEach-Object { [string] $_ })
    $text = $lines -join "`n"
    $codes = @([System.Text.RegularExpressions.Regex]::Matches(
            $text,
            '\bNU\d{4}\b',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) |
        ForEach-Object { $_.Value.ToUpperInvariant() } |
        Select-Object -Unique)

    $unexpectedCodes = @($codes | Where-Object { $_ -ne 'NU3004' })
    if ($codes.Count -eq 0 -or $unexpectedCodes.Count -gt 0) {
        return $false
    }

    $unsignedPattern = '(?i)(package\s+is\s+not\s+signed|package\s+is\s+unsigned|package.+unsigned|unsigned\s+package|package-not-signed|not\s+signed|서명.+않|서명되지)'
    if ($text -notmatch $unsignedPattern) {
        return $false
    }

    $fatalOrErrorPattern = '(?i)\b(fatal|error|failed|failure)\b|실패'
    $relatedSignatureFailurePattern = '(?i)(package\s+signature.*(failed|failure)|signature\s+validation.*(failed|failure)|시그니처.*(실패|유효성)|서명.*(실패|유효성))'
    foreach ($line in $lines) {
        if ($line -match $fatalOrErrorPattern) {
            if ($line -match '\bNU3004\b' -and $line -match $unsignedPattern) {
                continue
            }

            if ($line -match $relatedSignatureFailurePattern) {
                continue
            }

            return $false
        }
    }

    return $true
}

function Expand-Nupkg {
    param(
        [Parameter(Mandatory = $true)]
        [string] $PackagePath,
        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Destination | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $Destination)
}

function Assert-PackageMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo] $Package,
        [Parameter(Mandatory = $true)]
        [string] $ExpandedDirectory,
        [Parameter(Mandatory = $true)]
        [string] $Head,
        [Parameter(Mandatory = $true)]
        [string] $ExpectedVersion
    )

    $projectDocument = Read-XmlDocument -Path $projectPath
    $licenseExpression = Get-ProjectProperty -ProjectDocument $projectDocument -Name 'PackageLicenseExpression'
    $readmeFile = Get-ProjectProperty -ProjectDocument $projectDocument -Name 'PackageReadmeFile'
    $repositoryUrl = Get-ProjectProperty -ProjectDocument $projectDocument -Name 'RepositoryUrl'
    $repositoryType = Get-ProjectProperty -ProjectDocument $projectDocument -Name 'RepositoryType'

    Expand-Nupkg -PackagePath $Package.FullName -Destination $ExpandedDirectory
    $nuspec = Get-ChildItem -LiteralPath $ExpandedDirectory -Filter '*.nuspec' -File | Select-Object -First 1
    if ($null -eq $nuspec) {
        throw 'Package nuspec was not found.'
    }

    $nuspecDocument = Read-XmlDocument -Path $nuspec.FullName
    $metadata = $nuspecDocument.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata) {
        throw 'Package nuspec metadata was not found.'
    }

    $id = Get-XmlChildText -Node $metadata -Name 'id'
    if ($id -ne 'Lib.Db') {
        throw "Unexpected package id: $id"
    }

    $packageVersion = Get-XmlChildText -Node $metadata -Name 'version'
    if ($packageVersion -ne $ExpectedVersion) {
        throw "Unexpected package version. Expected $ExpectedVersion but found $packageVersion."
    }

    $license = Get-XmlChildElement -Node $metadata -Name 'license'
    if ($null -eq $license) {
        throw 'Package license metadata was not found.'
    }

    $licenseType = $license.Attributes['type']
    if ($null -eq $licenseType -or $licenseType.Value -ne 'expression' -or $license.InnerText.Trim() -ne $licenseExpression) {
        throw "Package license expression does not match project metadata."
    }

    $readme = Get-XmlChildText -Node $metadata -Name 'readme'
    if ($readme -ne $readmeFile) {
        throw "Package readme metadata does not match project metadata. Expected $readmeFile but found $readme."
    }

    $repository = Get-XmlChildElement -Node $metadata -Name 'repository'
    if ($null -eq $repository) {
        throw 'Package repository metadata was not found.'
    }

    $repositoryUrlAttribute = $repository.Attributes['url']
    if ($null -eq $repositoryUrlAttribute -or $repositoryUrlAttribute.Value -ne $repositoryUrl) {
        throw 'Package repository url does not match project metadata.'
    }

    $repositoryTypeAttribute = $repository.Attributes['type']
    if ($null -eq $repositoryTypeAttribute -or $repositoryTypeAttribute.Value -ne $repositoryType) {
        throw 'Package repository type does not match project metadata.'
    }

    $repositoryCommitAttribute = $repository.Attributes['commit']
    if ($null -eq $repositoryCommitAttribute -or [string]::IsNullOrWhiteSpace($repositoryCommitAttribute.Value)) {
        throw 'Package repository commit metadata was not found.'
    }

    Assert-PackageRepositoryCommitMatchesHead -RepositoryCommit $repositoryCommitAttribute.Value.Trim() -Head $Head
    Assert-PackageDependenciesMatchProject -ProjectDocument $projectDocument -MetadataNode $metadata
}

function Invoke-NuGetVerify {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo] $Package
    )

    Write-Host "Running dotnet nuget verify --all for $($Package.Name)"
    $verifyOutput = & dotnet @('nuget', 'verify', $Package.FullName, '--all') 2>&1
    $verifyExitCode = $LASTEXITCODE

    if ($verifyExitCode -eq 0) {
        return
    }

    if (-not $AllowUnsigned) {
        throw "dotnet nuget verify failed with exit code $verifyExitCode and unsigned packages are not allowed."
    }

    if (Test-OnlyAcceptedUnsignedNuGetFailure -Output $verifyOutput) {
        Write-Warning 'dotnet nuget verify reported only the accepted unsigned local package NU3004 failure.'
        return
    }

    throw "dotnet nuget verify failed with unaccepted output.`n$($verifyOutput -join [Environment]::NewLine)"
}

function Assert-SelfTestPass {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,
        [Parameter(Mandatory = $true)]
        [scriptblock] $Assertion
    )

    if (-not (& $Assertion)) {
        throw "Self-test failed: $Name"
    }

    Write-Host "SelfTest passed: $Name"
}

function Invoke-SelfTest {
    $head = '0123456789abcdef0123456789abcdef01234567'
    $different = 'fedcba9876543210fedcba9876543210fedcba98'

    Assert-SelfTestPass -Name 'AcceptsLocalizedUnsignedNu3004WithSignatureFailureSummary' -Assertion {
        Test-OnlyAcceptedUnsignedNuGetFailure -Output @(
            '',
            'Lib.Db.2.4.0을 확인하고 있습니다.',
            '콘텐츠 해시: abc',
            "error: NU3004: Package 'Lib.Db 2.4.0': 패키지가 서명되어 있지 않습니다.",
            '',
            '패키지 시그니처 유효성 검증에 실패했습니다.')
    }

    Assert-SelfTestPass -Name 'RejectsMixedNu3004AndAnotherNuGetCode' -Assertion {
        -not (Test-OnlyAcceptedUnsignedNuGetFailure -Output @(
                "error: NU3004: Package 'Lib.Db 2.4.0': The package is not signed.",
                "error: NU3018: The author primary signature's certificate chain is invalid."))
    }

    Assert-SelfTestPass -Name 'RejectsNu3004WithUnrelatedFatalText' -Assertion {
        -not (Test-OnlyAcceptedUnsignedNuGetFailure -Output @(
                "error: NU3004: Package 'Lib.Db 2.4.0': The package is not signed.",
                'fatal: unrelated repository verification failure.'))
    }

    Assert-SelfTestPass -Name 'RejectsShortRepositoryCommit' -Assertion {
        try {
            Assert-PackageRepositoryCommitMatchesHead -RepositoryCommit $head.Substring(0, 12) -Head $head
            return $false
        }
        catch {
            return $_.Exception.Message.Contains('Package repository commit does not match HEAD', [System.StringComparison]::Ordinal)
        }
    }

    Assert-SelfTestPass -Name 'RejectsDifferentRepositoryCommit' -Assertion {
        try {
            Assert-PackageRepositoryCommitMatchesHead -RepositoryCommit $different -Head $head
            return $false
        }
        catch {
            return $_.Exception.Message.Contains('Package repository commit does not match HEAD', [System.StringComparison]::Ordinal)
        }
    }

    Assert-SelfTestPass -Name 'AcceptsCleanRepositoryStatus' -Assertion {
        try {
            Assert-RepositoryStatusClean -StatusLines @()
            return $true
        }
        catch {
            return $false
        }
    }

    Assert-SelfTestPass -Name 'RejectsDirtyRepositoryStatus' -Assertion {
        try {
            Assert-RepositoryStatusClean -StatusLines @(
                ' M Lib.Db/Lib.Db.csproj',
                '?? .agents/skills/lib-db/SKILL.md')
            return $false
        }
        catch {
            return $_.Exception.Message.Contains('uncommitted source changes', [System.StringComparison]::Ordinal)
        }
    }

    Assert-SelfTestPass -Name 'RedactsSecretLikeDirtyStatusPath' -Assertion {
        try {
            Assert-RepositoryStatusClean -StatusLines @('?? docs/Password=fixture-secret/libdb.contracts.json')
            return $false
        }
        catch {
            return $_.Exception.Message.Contains('<redacted-path>', [System.StringComparison]::Ordinal) -and
                -not $_.Exception.Message.Contains('fixture-secret', [System.StringComparison]::Ordinal)
        }
    }

    Assert-SelfTestPass -Name 'RejectsArtifactDirectoryOutsideVerificationArtifacts' -Assertion {
        try {
            $outsideArtifacts = Resolve-RepoChildPath -ChildPath 'Lib.Db'
            Assert-PathUnderDirectory -PathValue $outsideArtifacts -Directory $verificationArtifactsRoot -Name 'ArtifactsDirectory'
            return $false
        }
        catch {
            return $_.Exception.Message.Contains('must resolve under Verification artifacts', [System.StringComparison]::Ordinal)
        }
    }

    Assert-SelfTestPass -Name 'RejectsVerificationArtifactsRootAsArtifactDirectory' -Assertion {
        try {
            Assert-PathUnderDirectory -PathValue $verificationArtifactsRoot -Directory $verificationArtifactsRoot -Name 'ArtifactsDirectory'
            return $false
        }
        catch {
            return $_.Exception.Message.Contains('not to the artifacts root', [System.StringComparison]::Ordinal)
        }
    }

    Assert-SelfTestPass -Name 'AcceptsExplicitPackageVersionOverride' -Assertion {
        try {
            Resolve-PackageVersion -ProjectVersion '2.5.0-rc.1' -OverrideVersion '2.5.0-rc.1' | Out-Null
            return $true
        }
        catch {
            return $false
        }
    }

    Assert-SelfTestPass -Name 'RejectsPrefixedPackageVersionOverride' -Assertion {
        try {
            Resolve-PackageVersion -ProjectVersion '2.4.0' -OverrideVersion 'v2.5.0' | Out-Null
            return $false
        }
        catch {
            return $_.Exception.Message.Contains('without the v prefix', [System.StringComparison]::Ordinal)
        }
    }

    Assert-SelfTestPass -Name 'RejectsMismatchedPackageVersionOverride' -Assertion {
        try {
            Resolve-PackageVersion -ProjectVersion '2.5.0' -OverrideVersion '2.5.1' | Out-Null
            return $false
        }
        catch {
            return $_.Exception.Message.Contains('must match project Version', [System.StringComparison]::Ordinal)
        }
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

Assert-RepositoryStatusClean -StatusLines (Get-RepositoryStatusLines)

$artifactRoot = Resolve-RepoChildPath -ChildPath $ArtifactsDirectory
Assert-PathUnderDirectory -PathValue $artifactRoot -Directory $verificationArtifactsRoot -Name 'ArtifactsDirectory'
if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactRoot | Out-Null

$head = Invoke-Checked 'git' @('-C', $repoRoot, 'rev-parse', 'HEAD') -CaptureOutput
if ($head -notmatch '^[0-9a-fA-F]{40}$') {
    throw "git rev-parse HEAD did not return a full commit SHA: $head"
}

$projectDocument = Read-XmlDocument -Path $projectPath
$projectVersion = Get-ProjectProperty -ProjectDocument $projectDocument -Name 'Version'
$effectivePackageVersion = Resolve-PackageVersion -ProjectVersion $projectVersion -OverrideVersion $PackageVersion

Invoke-Checked 'dotnet' @(
    'pack',
    $projectPath,
    '-c', 'Release',
    '-o', $artifactRoot,
    "/p:RepositoryCommit=$head",
    "/p:PackageVersion=$effectivePackageVersion",
    "/p:Version=$effectivePackageVersion",
    '-p:UseSharedCompilation=false'
)

$packages = @(Get-ChildItem -LiteralPath $artifactRoot -Filter '*.nupkg' -File |
    Where-Object { $_.Name -notlike '*.snupkg' })

if ($packages.Count -ne 1) {
    throw "Expected exactly one .nupkg package, found $($packages.Count)."
}

$expandedDirectory = Join-Path $artifactRoot 'expanded'
try {
    Assert-PackageMetadata -Package $packages[0] -ExpandedDirectory $expandedDirectory -Head $head -ExpectedVersion $effectivePackageVersion
    Invoke-Checked 'pwsh' @('-NoProfile', '-File', $artifactScanner, '-SelfTest')
    Invoke-Checked 'pwsh' @('-NoProfile', '-File', $artifactScanner, '-Paths', $artifactRoot)
}
finally {
    if (Test-Path -LiteralPath $expandedDirectory) {
        Remove-Item -LiteralPath $expandedDirectory -Recurse -Force
    }
}

Invoke-NuGetVerify -Package $packages[0]

Write-Host 'Release package verification completed.'
