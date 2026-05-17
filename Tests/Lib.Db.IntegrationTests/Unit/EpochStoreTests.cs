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
}
