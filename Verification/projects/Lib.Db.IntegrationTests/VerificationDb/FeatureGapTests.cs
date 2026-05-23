// ============================================================================
// 파일: VerificationDb/FeatureGapTests.cs
// 설명: SQL Server 기능 완전성(Feature Gap) 검증 테스트 7개
//       (벌크 TVP, 격리 수준, JSON, MERGE, 페이지네이션, 윈도우 함수)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using System.Diagnostics;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// Lib.Db v2 SQL Server 기능 완전성(Feature Gap) 검증 테스트.
/// <para><b>[설계 의도]</b> gap 스키마의 SP 8개를 통해 벌크 TVP 10K건,
/// 트랜잭션 격리 수준, JSON 컬럼, MERGE+OUTPUT, 페이지네이션,
/// CTE+윈도우 함수 등 SQL Server 핵심 기능의 Lib.Db 지원 여부를 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class FeatureGapTests(MultiDbFixture fixture, ITestOutputHelper output)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;
    private readonly ITestOutputHelper _output = output;

    #endregion

    #region FG01: TVP 벌크 삽입 10K건

    /// <summary>
    /// 10,000건 TVP 데이터를 perf.usp_Perf_Bulk_Insert로 삽입하고 성능을 측정한다.
    /// <para><b>[설계 의도]</b> 기존 검증 완료된 perf 스키마 인프라를 재사용하여
    /// 10K건 대량 삽입의 안정성과 처리량을 측정한다.</para>
    /// </summary>
    [Fact]
    public async Task FG01_BulkInsert_10KRows_WithTvp_ShouldSucceed()
    {
        // Arrange
        const int rowCount = 10_000;
        int batchNumber = Random.Shared.Next(100_000, 999_999);

        List<PerfBulkInsertTvp> items = Enumerable.Range(0, rowCount)
            .Select(i => new PerfBulkInsertTvp
            {
                BatchNumber = batchNumber,
                Data = $"FG01_{i:D5}_{Guid.NewGuid():N}"
            })
            .ToList();

        // Act
        Stopwatch sw = Stopwatch.StartNew();
        DbResult<int> result = await _db
            .Procedure("perf.usp_Perf_Bulk_Insert")
            .With(new { Items = items })
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue("TVP 10K건 벌크 삽입이 성공해야 합니다.");
        result.Value.Should().BeGreaterThanOrEqualTo(rowCount,
            $"{rowCount}건 이상이 삽입되어야 합니다.");

        _output.WriteLine($"=== FG01: TVP 벌크 삽입 {rowCount:N0}건 ===");
        _output.WriteLine($"소요 시간: {sw.Elapsed.TotalMilliseconds:F0}ms");
        _output.WriteLine($"처리량: {rowCount / sw.Elapsed.TotalSeconds:F0} rows/sec");

        // Cleanup
        await _db
            .Sql($"DELETE FROM [perf].[BulkTest] WHERE BatchNumber = {batchNumber}")
            .ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region FG02: 격리 수준 READ UNCOMMITTED

    /// <summary>
    /// gap.usp_IsolationLevel_ReadUncommitted 호출 시 NOLOCK 힌트로 데이터를 정상 반환하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task FG02_IsolationLevel_ReadUncommitted_ViaSQL_ShouldWork()
    {
        // Act
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("gap.usp_IsolationLevel_ReadUncommitted")
            .With(new { TargetId = 1 })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("READ UNCOMMITTED 격리 수준 SP가 성공해야 합니다.");
        List<Dictionary<string, object?>> rows = await result.Value!.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().NotBeEmpty("시드 데이터(UserId=1)가 존재해야 합니다.");
        rows[0].Should().ContainKey("UserName");
    }

    #endregion

    #region FG03: 격리 수준 SERIALIZABLE

    /// <summary>
    /// gap.usp_IsolationLevel_Serializable 호출 시 SERIALIZABLE 트랜잭션이 정상 완료되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task FG03_IsolationLevel_Serializable_ViaSQL_ShouldWork()
    {
        // Act
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("gap.usp_IsolationLevel_Serializable")
            .With(new { TargetId = 1 })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("SERIALIZABLE 격리 수준 SP가 성공해야 합니다.");
        List<Dictionary<string, object?>> rows = await result.Value!.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().NotBeEmpty("시드 데이터(UserId=1)가 존재해야 합니다.");
    }

    #endregion

    #region FG04: JSON 삽입 및 쿼리

    /// <summary>
    /// JSON 데이터를 삽입한 후 JSON_VALUE로 특정 키를 추출하여 검증한다.
    /// </summary>
    [Fact]
    public async Task FG04_Json_InsertAndQuery_ShouldWork()
    {
        // Arrange — JSON 삽입
        string jsonPayload = """{"name":"test","value":42}""";
        DbResult<int> insertResult = await _db
            .Procedure("gap.usp_Json_Insert")
            .With(new { JsonPayload = jsonPayload })
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        insertResult.IsSuccess.Should().BeTrue("JSON 삽입이 성공해야 합니다.");
        int newId = insertResult.Value;
        newId.Should().BeGreaterThan(0);

        // Act — JSON 쿼리
        DbResult<IAsyncEnumerable<GapJsonQueryResult>> queryResult = await _db
            .Procedure("gap.usp_Json_Query")
            .With(new { Key = "name" })
            .QueryAsync<GapJsonQueryResult>(TestContext.Current.CancellationToken);

        // Assert
        queryResult.IsSuccess.Should().BeTrue("JSON 쿼리가 성공해야 합니다.");
        List<GapJsonQueryResult> rows = await queryResult.Value!.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().Contain(r => r.Id == newId && r.ExtractedValue == "test",
            "JSON_VALUE로 추출한 값이 'test'여야 합니다.");

        // Cleanup
        await _db
            .Sql($"DELETE FROM [gap].[JsonData] WHERE Id = {newId}")
            .ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region FG05: MERGE Upsert (INSERT → UPDATE)

    /// <summary>
    /// MERGE로 INSERT 후 UPDATE하여 OUTPUT 절의 MergeAction이 정확히 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task FG05_Merge_Upsert_InsertThenUpdate_ShouldReturnAction()
    {
        // Arrange
        int testId = Random.Shared.Next(900_000, 999_999);

        // Act 1 — 첫 호출: INSERT
        DbResult<IAsyncEnumerable<GapMergeResult>> insertResult = await _db
            .Procedure("gap.usp_Merge_Upsert")
            .With(new { Id = testId, Name = "First" })
            .QueryAsync<GapMergeResult>(TestContext.Current.CancellationToken);

        insertResult.IsSuccess.Should().BeTrue("첫 MERGE 호출이 성공해야 합니다.");
        List<GapMergeResult> insertRows = await insertResult.Value!.ToListAsync(TestContext.Current.CancellationToken);
        insertRows.Should().ContainSingle();
        insertRows[0].MergeAction.Should().Be("INSERT",
            "존재하지 않는 행에 대한 MERGE는 INSERT여야 합니다.");

        // Act 2 — 두 번째 호출: UPDATE
        DbResult<IAsyncEnumerable<GapMergeResult>> updateResult = await _db
            .Procedure("gap.usp_Merge_Upsert")
            .With(new { Id = testId, Name = "Updated" })
            .QueryAsync<GapMergeResult>(TestContext.Current.CancellationToken);

        updateResult.IsSuccess.Should().BeTrue("두 번째 MERGE 호출이 성공해야 합니다.");
        List<GapMergeResult> updateRows = await updateResult.Value!.ToListAsync(TestContext.Current.CancellationToken);
        updateRows.Should().ContainSingle();
        updateRows[0].MergeAction.Should().Be("UPDATE",
            "이미 존재하는 행에 대한 MERGE는 UPDATE여야 합니다.");

        // Cleanup
        await _db
            .Sql($"DELETE FROM [gap].[MergeTarget] WHERE Id = {testId}")
            .ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region FG06: 페이지네이션 OFFSET-FETCH

    /// <summary>
    /// OFFSET-FETCH 페이지네이션 SP가 정확한 페이지 데이터를 반환하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task FG06_Pagination_OffsetFetch_ShouldReturnCorrectPage()
    {
        // Act — 첫 번째 결과셋(페이지 데이터)만 확인
        DbResult<IAsyncEnumerable<GapPaginatedUser>> result = await _db
            .Procedure("gap.usp_Paginate")
            .With(new { PageNum = 1, PageSize = 2 })
            .QueryAsync<GapPaginatedUser>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("페이지네이션 SP가 성공해야 합니다.");
        List<GapPaginatedUser> page = await result.Value!.ToListAsync(TestContext.Current.CancellationToken);
        page.Should().HaveCountLessThanOrEqualTo(2,
            "PageSize=2이므로 최대 2건이 반환되어야 합니다.");
        page.Should().NotBeEmpty("시드 데이터가 존재하므로 1건 이상이어야 합니다.");

        // UserId 순서 검증 (ORDER BY UserId)
        if (page.Count == 2)
        {
            page[0].UserId.Should().BeLessThan(page[1].UserId,
                "UserId 오름차순으로 정렬되어야 합니다.");
        }

        _output.WriteLine($"=== FG06: 페이지네이션 결과 {page.Count}건 ===");
        foreach (GapPaginatedUser user in page)
        {
            _output.WriteLine($"  UserId={user.UserId}, UserName={user.UserName}");
        }
    }

    #endregion

    #region FG07: CTE + 윈도우 함수 (ROW_NUMBER, RANK, DENSE_RANK)

    /// <summary>
    /// CTE + 윈도우 함수(ROW_NUMBER, RANK, DENSE_RANK, COUNT OVER)가
    /// 정상적으로 결과 컬럼을 반환하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task FG07_WindowFunction_Rank_ShouldReturnRankColumns()
    {
        // Act
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("gap.usp_WindowFunction_RankUsers")
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("윈도우 함수 SP가 성공해야 합니다.");
        List<Dictionary<string, object?>> rows = await result.Value!.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().NotBeEmpty("시드 데이터가 존재하므로 1건 이상이어야 합니다.");

        // 필수 컬럼 존재 확인
        Dictionary<string, object?> firstRow = rows[0];
        firstRow.Should().ContainKey("RowNum", "ROW_NUMBER 컬럼이 존재해야 합니다.");
        firstRow.Should().ContainKey("AgeRank", "RANK 컬럼이 존재해야 합니다.");
        firstRow.Should().ContainKey("DenseAgeRank", "DENSE_RANK 컬럼이 존재해야 합니다.");
        firstRow.Should().ContainKey("TotalUsers", "COUNT(*) OVER() 컬럼이 존재해야 합니다.");

        // RowNum이 순차적인지 검증
        for (int i = 0; i < rows.Count; i++)
        {
            long rowNum = Convert.ToInt64(rows[i]["RowNum"]);
            rowNum.Should().Be(i + 1, $"RowNum은 {i + 1}이어야 합니다.");
        }

        // TotalUsers가 행 수와 일치하는지 검증
        int totalUsers = Convert.ToInt32(rows[0]["TotalUsers"]);
        totalUsers.Should().Be(rows.Count,
            "TotalUsers는 전체 행 수와 일치해야 합니다.");

        _output.WriteLine($"=== FG07: 윈도우 함수 결과 {rows.Count}건 ===");
        foreach (Dictionary<string, object?> row in rows)
        {
            _output.WriteLine($"  RowNum={row["RowNum"]}, UserName={row["UserName"]}, " +
                              $"AgeRank={row["AgeRank"]}, DenseAgeRank={row["DenseAgeRank"]}");
        }
    }

    #endregion
}
