// ============================================================================
// 파일: VerificationDb/V221BlockerVerificationTests.cs
// 설명: v2.2.1 차단 이슈를 검증 DB의 별도 SP/테스트 데이터로 재현 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// v2.2.1에서 수정한 결과 매핑 convention, generated mapper reader 호환성, DateOnly raw 바인딩을 실 DB에서 검증한다.
/// </summary>
[Collection("MultiDb")]
public sealed class V221BlockerVerificationTests(MultiDbFixture fixture)
{
    private static readonly DateOnly VerificationDate = new(2026, 5, 17);
    private readonly IProcedureStage _db = fixture.Verification;

    [Fact]
    public async Task V22101_DefaultMapper_ShouldMapUpperSnakeResultSet_ToPascalCasePositionalRecord()
    {
        DbResult<IAsyncEnumerable<V221SuspendRow>> result = await _db
            .Procedure("verify.usp_GetSuspendRows")
            .With(new { ScanDate = VerificationDate })
            .QueryAsync<V221SuspendRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        List<V221SuspendRow> rows = await result.Value!.ToListAsync(TestContext.Current.CancellationToken);

        rows.Should().ContainSingle();
        rows[0].CellNo.Should().Be(17);
        rows[0].SlotName.Should().Be("A01");
    }

    [Fact]
    public async Task V22102_GeneratedDbResult_ShouldMapThroughMonitoredDbDataReader()
    {
        DbResult<IAsyncEnumerable<V221GeneratedVerificationRow>> result = await _db
            .Procedure("verify.usp_GetGeneratedRows")
            .With(new { ScanDate = VerificationDate })
            .QueryAsync<V221GeneratedVerificationRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        List<V221GeneratedVerificationRow> rows = await result.Value!.ToListAsync(TestContext.Current.CancellationToken);

        rows.Should().ContainSingle();
        rows[0].UserId.Should().Be(1001);
        rows[0].UserName.Should().Be("Generated User");
        rows[0].Email.Should().Be("generated.user@example.test");
        rows[0].Age.Should().Be(27);
    }

    [Fact]
    public async Task V22103_RawSqlDateOnlyParameter_ShouldBindAsSqlDate()
    {
        DbResult<int> result = await _db
            .Sql("""
                SELECT COUNT(*)
                FROM [verify].[ResultMappingRows]
                WHERE [SCAN_DATE] = @ScanDate
                """)
            .With(new { ScanDate = VerificationDate })
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task V22104_QuotedIdentifierVerificationIndex_ShouldBeCreated()
    {
        DbResult<int> result = await _db
            .Sql("""
                SELECT COUNT(*)
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[verify].[QuotedIdentifierRows]')
                  AND name = N'IX_QuotedIdentifierRows_NormalizedCode'
                """)
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }
}
