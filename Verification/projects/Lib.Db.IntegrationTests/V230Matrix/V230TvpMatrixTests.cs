// ============================================================================
// 파일: V230Matrix/V230TvpMatrixTests.cs
// 설명: v2.3.0 검증 DB의 TVP 저장 프로시저 전체 실행 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.V230Matrix;

[Collection("MultiDb")]
public sealed class V230TvpMatrixTests(MultiDbFixture fixture)
{
    [Fact]
    public async Task VerificationDatabase_ShouldExecuteRepresentativeMixedScalarAndMultiTvpProcedure()
    {
        int tenantId = Random.Shared.Next(30_000, 39_999);
        string externalOrderId = $"VERIFY-MATRIX-{Guid.NewGuid():N}";
        Guid correlationId = Guid.NewGuid();
        VerificationOrderHeaderRow[] headers =
        [
            new(1, externalOrderId, "CUST-230", "VIP", "N", DateTime.UtcNow, 129.9900m, """{"source":"v230-matrix","priority":3}""")
        ];
        VerificationOrderLineRow[] lines =
        [
            new(1, 1, "SKU-RED", 2, 29.9900m, 0.0000m, 0.100000m),
            new(1, 2, "SKU-BLUE", 1, 70.0100m, 0.0000m, 0.100000m)
        ];

        DbResult<Dictionary<string, object?>?> result = await fixture.Verification
            .Procedure("verify.usp_Verification_UpsertOrders")
            .With(new
            {
                TenantId = tenantId,
                RequestedBy = "v230-matrix",
                CorrelationId = correlationId,
                Headers = LibDb.Tvp("verify.Tvp_VerificationOrderHeader", headers),
                Lines = LibDb.Tvp("verify.Tvp_VerificationOrderLine", lines)
            })
            .QuerySingleAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().NotBeNull();
        Convert.ToInt32(result.Value!["InsertedOrders"]).Should().Be(1);
        Convert.ToInt32(result.Value!["InsertedLines"]).Should().Be(2);
    }

    [Fact]
    public async Task StressDatabase_ShouldExecuteEveryTvpProcedure()
    {
        TvpMatrixRunSummary summary = await TvpMatrixProcedureHarness.ExecuteAllAsync(
            fixture.Stress,
            fixture.GetConnectionString(TestConnectionStrings.Stress),
            TestContext.Current.CancellationToken);

        summary.DiscoveredProcedures.Should().BeGreaterThan(0);
        summary.UnexpectedFailures.Should().BeEmpty();
        summary.ExecutedProcedures.Should().Be(summary.DiscoveredProcedures);
    }

    [Fact]
    public async Task ChaosDatabase_ShouldExecuteEveryTvpProcedure()
    {
        TvpMatrixRunSummary summary = await TvpMatrixProcedureHarness.ExecuteAllAsync(
            fixture.Chaos,
            fixture.GetConnectionString(TestConnectionStrings.Chaos),
            TestContext.Current.CancellationToken);

        summary.DiscoveredProcedures.Should().BeGreaterThan(0);
        summary.UnexpectedFailures.Should().BeEmpty();
        summary.ExecutedProcedures.Should().Be(summary.DiscoveredProcedures);
    }

    [Fact]
    public async Task BenchmarkDatabase_ShouldExecuteEveryTvpProcedure()
    {
        TvpMatrixRunSummary summary = await TvpMatrixProcedureHarness.ExecuteAllAsync(
            fixture.Benchmark,
            fixture.GetConnectionString(TestConnectionStrings.Benchmark),
            TestContext.Current.CancellationToken);

        summary.DiscoveredProcedures.Should().BeGreaterThan(0);
        summary.UnexpectedFailures.Should().BeEmpty();
        summary.ExecutedProcedures.Should().Be(summary.DiscoveredProcedures);
    }

    [Fact]
    public void DefaultAllVerificationScript_ShouldNotRunServerLevelChaos()
    {
        string scriptPath = SqlScriptRunner.ResolveScriptPath("verify-libdb-all.migration-reference.sql");
        string script = File.ReadAllText(scriptPath);

        script.Contains("chaos-server-optin", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        script.Contains("KILL ", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        script.Contains("ALTER SERVER", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private sealed record VerificationOrderHeaderRow(
        int RowNo,
        string ExternalOrderId,
        string CustomerCode,
        string SegmentCode,
        string OrderStatus,
        DateTime OrderDate,
        decimal TotalAmount,
        string MetadataJson);

    private sealed record VerificationOrderLineRow(
        int HeaderRowNo,
        int LineNo,
        string ProductCode,
        int Quantity,
        decimal UnitPrice,
        decimal DiscountAmount,
        decimal TaxRate);
}
