// ============================================================================
// 파일: VerificationDb/RuntimeTvpExecutionTests.cs
// 설명: v2.3 런타임 TVP wrapper 실제 SQL Server 실행 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class RuntimeTvpExecutionTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _db = fixture.Verification;

    [Fact]
    public async Task RuntimeTvp_ShouldExecuteStoredProcedureWithMixedScalarParameters()
    {
        int orderId = Random.Shared.Next(100_000, 999_999);
        RuntimeOrderItemRow[] rows =
        [
            new(1, "A100", 2, 12.50m),
            new(2, "B200", 1, 8.25m)
        ];

        DbResult<long> inserted = await _db
            .Procedure("dbo.libdb_bench_InsertOrderItems")
            .With(new
            {
                OrderId = orderId,
                RequestedBy = "runtime-test",
                Rows = LibDb.Tvp("dbo.libdb_bench_OrderItem", rows)
            })
            .ExecuteScalarAsync<long>(TestContext.Current.CancellationToken);

        inserted.IsSuccess.Should().BeTrue(inserted.Error?.Message);
        inserted.Value.Should().Be(2);
    }

    private sealed record RuntimeOrderItemRow(int Id, string Sku, int Qty, decimal Price);
}
