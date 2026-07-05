// ============================================================================
// 파일: Unit/EpochStoreTests.cs
// 설명: EpochStore 진단 메시지 redaction 회귀 테스트
// ============================================================================

using Lib.Db.Caching;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class EpochStoreTests
{
    [Fact]
    public void RedactInstanceForDiagnostics_ShouldRedactRawInstance()
    {
        string rawInstance = "Raw:InstanceMaterialForEpochTest;Segment=Epsilon;";

        string diagnostic = EpochStore.RedactInstanceForDiagnostics(rawInstance);

        diagnostic.Should().Be("Raw:[redacted]");
        diagnostic.Should().NotContain(rawInstance);
        diagnostic.Should().NotContain("Segment=Epsilon");
    }

    [Fact]
    public void RedactInstanceForDiagnostics_ShouldKeepHashedInstance()
    {
        EpochStore.RedactInstanceForDiagnostics("instance-hash-1")
            .Should().Be("instance-hash-1");
    }

    [Fact]
    public void BuildMutexLogicalName_ShouldIncludeStorageNamespaceWithoutRawBasePath()
    {
        string firstBasePath = Path.Combine(Path.GetTempPath(), "LibDbEpochA-" + Guid.NewGuid().ToString("N"));
        string secondBasePath = Path.Combine(Path.GetTempPath(), "LibDbEpochB-" + Guid.NewGuid().ToString("N"));

        string first = EpochStore.BuildMutexLogicalName(firstBasePath, 7);
        string firstAgain = EpochStore.BuildMutexLogicalName(firstBasePath, 7);
        string second = EpochStore.BuildMutexLogicalName(secondBasePath, 7);

        first.Should().Be(firstAgain);
        first.Should().NotBe(second);
        first.Should().Contain("Stripe7");
        first.Should().NotContain(firstBasePath);
        second.Should().NotContain(secondBasePath);
    }

    [Fact]
    public void BuildMutexLogicalName_ShouldCanonicalizeEquivalentBasePaths()
    {
        string basePath = Path.Combine(Path.GetTempPath(), "LibDbEpochCanonical-" + Guid.NewGuid().ToString("N"));
        string withSeparator = basePath + Path.DirectorySeparatorChar;

        EpochStore.BuildMutexLogicalName(withSeparator, 3)
            .Should().Be(EpochStore.BuildMutexLogicalName(basePath, 3));

        if (OperatingSystem.IsWindows())
        {
            EpochStore.BuildMutexLogicalName(basePath.ToUpperInvariant(), 3)
                .Should().Be(EpochStore.BuildMutexLogicalName(basePath.ToLowerInvariant(), 3));
        }
    }
}
