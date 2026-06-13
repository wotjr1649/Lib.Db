// ============================================================================
// 파일: VerificationDb/RepresentativeVerificationDbTests.cs
// 설명: LIBDB_VERIFICATION_TEST 대표 복잡도/TVP 검증 보강 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class RepresentativeVerificationDbTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _db = fixture.Verification;

    [Fact]
    public async Task VerificationDatabase_ShouldExposeRepresentativeComplexityObjects()
    {
        DbResult<int> missing = await _db.Sql("""
            SELECT
                CASE WHEN OBJECT_ID(N'[verify].[VerificationOrderHeaders]', N'U') IS NULL THEN 1 ELSE 0 END +
                CASE WHEN OBJECT_ID(N'[verify].[VerificationOrderLines]', N'U') IS NULL THEN 1 ELSE 0 END +
                CASE WHEN OBJECT_ID(N'[verify].[VerificationOrderAudit]', N'U') IS NULL THEN 1 ELSE 0 END +
                CASE WHEN TYPE_ID(N'[verify].[Tvp_VerificationOrderHeader]') IS NULL THEN 1 ELSE 0 END +
                CASE WHEN TYPE_ID(N'[verify].[Tvp_VerificationOrderLine]') IS NULL THEN 1 ELSE 0 END +
                CASE WHEN OBJECT_ID(N'[verify].[usp_Verification_UpsertOrders]', N'P') IS NULL THEN 1 ELSE 0 END +
                CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[verify].[VerificationOrderHeaders]', N'U')
                      AND name = N'IX_verify_VerificationOrderHeaders_OpenStatus'
                      AND has_filter = 1
                ) THEN 0 ELSE 1 END +
                CASE WHEN
                (
                    SELECT COUNT(*)
                    FROM sys.parameters
                    WHERE object_id = OBJECT_ID(N'[verify].[usp_Verification_UpsertOrders]', N'P')
                      AND user_type_id IN (TYPE_ID(N'[verify].[Tvp_VerificationOrderHeader]'), TYPE_ID(N'[verify].[Tvp_VerificationOrderLine]'))
                      AND is_readonly = 1
                ) = 2 THEN 0 ELSE 1 END +
                CASE WHEN
                (
                    SELECT COUNT(*)
                    FROM sys.parameters
                    WHERE object_id = OBJECT_ID(N'[verify].[usp_Verification_UpsertOrders]', N'P')
                      AND is_output = 1
                ) = 2 THEN 0 ELSE 1 END +
                CASE WHEN
                (
                    SELECT COUNT(*)
                    FROM sys.indexes AS i
                    JOIN sys.table_types AS tt ON tt.type_table_object_id = i.object_id
                    WHERE tt.user_type_id IN (TYPE_ID(N'[verify].[Tvp_VerificationOrderHeader]'), TYPE_ID(N'[verify].[Tvp_VerificationOrderLine]'))
                      AND i.index_id > 0
                ) >= 3 THEN 0 ELSE 1 END +
                CASE WHEN
                (
                    SELECT COUNT(*)
                    FROM sys.check_constraints AS ck
                    JOIN sys.table_types AS tt ON tt.type_table_object_id = ck.parent_object_id
                    WHERE tt.user_type_id IN (TYPE_ID(N'[verify].[Tvp_VerificationOrderHeader]'), TYPE_ID(N'[verify].[Tvp_VerificationOrderLine]'))
                ) >= 4 THEN 0 ELSE 1 END AS [MissingCount];
            """).ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        missing.IsSuccess.Should().BeTrue(missing.Error?.Message);
        missing.Value.Should().Be(0);
    }

    [Fact]
    public async Task RuntimeTvp_ShouldExecuteRepresentativeMixedScalarAndMultiTvpProcedure()
    {
        int tenantId = Random.Shared.Next(20_000, 29_999);
        string externalOrderId = $"VERIFY-{Guid.NewGuid():N}";
        Guid correlationId = Guid.NewGuid();
        VerificationOrderHeaderRow[] headers =
        [
            new(1, externalOrderId, "CUST-230", "VIP", "N", DateTime.UtcNow, 129.9900m, """{"source":"xunit","priority":3}""")
        ];
        VerificationOrderLineRow[] lines =
        [
            new(1, 1, "SKU-RED", 2, 29.9900m, 0.0000m, 0.100000m),
            new(1, 2, "SKU-BLUE", 1, 70.0100m, 0.0000m, 0.100000m)
        ];
        var insertedOrders = new SqlParameter("@InsertedOrders", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        var insertedLines = new SqlParameter("@InsertedLines", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };

        DbResult<Dictionary<string, object?>?> result = await _db
            .Procedure("verify.usp_Verification_UpsertOrders")
            .With(new
            {
                TenantId = tenantId,
                RequestedBy = "representative-test",
                CorrelationId = correlationId,
                Headers = LibDb.Tvp("verify.Tvp_VerificationOrderHeader", headers),
                Lines = LibDb.Tvp("verify.Tvp_VerificationOrderLine", lines),
                InsertedOrders = insertedOrders,
                InsertedLines = insertedLines
            })
            .QuerySingleAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().NotBeNull();
        Convert.ToInt32(result.Value!["InsertedOrders"]).Should().Be(1);
        Convert.ToInt32(result.Value!["InsertedLines"]).Should().Be(2);
        Convert.ToInt32(insertedOrders.Value).Should().Be(1);
        Convert.ToInt32(insertedLines.Value).Should().Be(2);
    }

    private sealed record VerificationOrderHeaderRow(
        int RowNo,
        string ExternalOrderId,
        string CustomerCode,
        string SegmentCode,
        string OrderStatus,
        DateTime SubmittedAt,
        decimal TotalAmount,
        string? MetadataJson);

    private sealed record VerificationOrderLineRow(
        int RowNo,
        int LineNo,
        string ProductCode,
        int Quantity,
        decimal UnitPrice,
        decimal DiscountAmount,
        decimal TaxRate);
}
