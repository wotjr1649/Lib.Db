// ============================================================================
// 파일: Unit/SchemaWarmupServiceTests.cs
// 설명: SchemaWarmupService 진단 정보 redaction 회귀 테스트
// ============================================================================

using Lib.Db.Hosting;
using Lib.Db.Diagnostics;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class SchemaWarmupServiceTests
{
    [Fact]
    public void CreateDiagnosticRequestInfo_ShouldRedactRawInstanceInDiagnostics()
    {
        string rawInstance = "raw:InstanceMaterialForWarmupTest;Segment=Delta;";

        DbRequestInfo info = SchemaWarmupService.CreateDiagnosticRequestInfo(rawInstance, schemaCount: 2);

        info.InstanceId.Should().Be("Raw:[redacted]");
        info.CorrelationId.Should().Be("warmup:Raw:[redacted]:2");
        info.InstanceId.Should().NotContain(rawInstance);
        info.CorrelationId.Should().NotContain(rawInstance);
        info.CorrelationId.Should().NotContain("Segment=Delta");
    }
}
