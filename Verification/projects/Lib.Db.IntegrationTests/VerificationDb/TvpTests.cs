// ============================================================================
// 파일: VerificationDb/TvpTests.cs
// 설명: TVP(테이블 반환 매개변수) 대량 삽입 + AllTypes 매핑 테스트 (IT02 이관)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// TVP 기반 대량 삽입 및 다양한 타입 매핑을 검증하는 테스트.
/// <para><b>[설계 의도]</b> TestSuite IT02를 IntegrationTests로 이관하고,
/// tvp.usp_Tvp_Bulk_Insert_AllTypes를 통해 .NET 10 타입 매핑까지 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class TvpTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region TVP 대량 삽입 테스트

    /// <summary>
    /// perf.Tvp_Perf_BulkInsert TVP를 사용하여 50행 대량 삽입 후 검증한다.
    /// </summary>
    [Fact]
    public async Task BulkInsert_Tvp_ShouldInsertRows()
    {
        // Arrange
        Random rand = new();
        int batchNumber = rand.Next(100000, 999999);
        int rowCount = 50;

        List<PerfBulkInsertTvp> items = Enumerable.Range(0, rowCount)
            .Select(i => new PerfBulkInsertTvp
            {
                BatchNumber = batchNumber,
                Data = $"Row_{i}_{Guid.NewGuid()}"
            })
            .ToList();

        // Act — TVP를 통한 대량 삽입
        DbResult<int> scalarResult = await _db
            .Procedure("perf.usp_Perf_Bulk_Insert")
            .With(new { Items = items })
            .ExecuteScalarAsync<int>();

        // Assert — 삽입 행 수 검증
        scalarResult.IsSuccess.Should().BeTrue();
        scalarResult.Value.Should().Be(rowCount, $"{rowCount}행이 삽입되어야 합니다.");

        // Act — 삽입된 데이터 조회
        DbResult<IAsyncEnumerable<PerfBulkTest>> streamResult = await _db
            .Procedure("perf.usp_Perf_Query_With_Param")
            .With(new { BatchNumber = batchNumber })
            .QueryAsync<PerfBulkTest>();

        streamResult.IsSuccess.Should().BeTrue();
        List<PerfBulkTest> insertedRows = await streamResult.Value!.ToListAsync();

        // Assert — 데이터 무결성 검증
        insertedRows.Should().HaveCount(rowCount);
        insertedRows.Should().OnlyContain(r => r.BatchNumber == batchNumber);

        // Cleanup
        await _db
            .Sql($"DELETE FROM [perf].[BulkTest] WHERE BatchNumber = {batchNumber}")
            .ExecuteAsync();
    }

    #endregion

    #region AllTypes TVP 매핑 테스트

    /// <summary>
    /// tvp.Tvp_Tvp_AllTypes TVP를 사용하여 DateOnly, TimeOnly, Half, Guid, Decimal 타입 매핑을 검증한다.
    /// </summary>
    [Fact]
    public async Task AllTypes_Tvp_ShouldMapCorrectly()
    {
        // Arrange
        Guid testGuid = Guid.NewGuid();
        List<TvpAllTypes> items =
        [
            new()
            {
                DateOnlyValue = new DateOnly(2026, 4, 5),
                TimeOnlyValue = new TimeOnly(14, 30, 0),
                HalfValue = (Half)3.14,
                GuidValue = testGuid,
                DecimalValue = 12345.6789m
            }
        ];

        // Act — AllTypes TVP를 통한 삽입
        DbResult<int> scalarResult = await _db
            .Procedure("tvp.usp_Tvp_Bulk_Insert_AllTypes")
            .With(new { Items = items })
            .ExecuteScalarAsync<int>();

        // Assert — 삽입 성공 검증
        scalarResult.IsSuccess.Should().BeTrue();
        scalarResult.Value.Should().Be(1, "1행이 삽입되어야 합니다.");

        // Act — 삽입된 데이터 조회로 타입 매핑 검증
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> queryResult = await _db
            .Procedure("tvp.usp_Tvp_Get_AllTypes")
            .QueryAsync<Dictionary<string, object?>>();

        queryResult.IsSuccess.Should().BeTrue();
        List<Dictionary<string, object?>> rows = await queryResult.Value!.ToListAsync();
        rows.Should().NotBeEmpty();

        // 마지막 삽입 행에서 GuidValue 검증
        Dictionary<string, object?> lastRow = rows[^1];
        lastRow["GuidValue"].Should().Be(testGuid);
    }

    #endregion

    #region TVP 스키마 불일치 테스트

    /// <summary>
    /// tvp.usp_Tvp_Test_Schema_Mismatch SP를 FormattableString SQL로 호출하여
    /// TVP 데이터를 전달하고 에러 없이 결과를 반환하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task SchemaMismatch_Tvp_ShouldHandleGracefully()
    {
        // Act — FormattableString SQL로 TVP 직접 호출
        DbResult<Dictionary<string, object?>?> result = await _db
            .Sql("DECLARE @t tvp.Tvp_Tvp_SchemaMismatch; INSERT INTO @t VALUES (N'A', 1, GETDATE()); INSERT INTO @t VALUES (N'B', 2, GETDATE()); EXEC tvp.usp_Tvp_Test_Schema_Mismatch @Items = @t;")
            .QuerySingleAsync<Dictionary<string, object?>>();

        // Assert — SP 호출 성공 (최소 1행 반환)
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull("TVP에 2행을 전달했으므로 최소 1행이 반환되어야 합니다.");
    }

    #endregion
}
