// ============================================================================
// 파일: VerificationDb/BulkInsertTests.cs
// 설명: SqlBulkCopy 기반 BulkInsertAsync API 검증 테스트 5개
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using System.Diagnostics;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// BulkInsertAsync API 검증 테스트.
/// <para><b>[설계 의도]</b> SqlBulkCopy 기반 대량 INSERT의 정상 동작, 옵션, 빈 컬렉션, 오류, TVP 비교를 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class BulkInsertTests(MultiDbFixture fixture, ITestOutputHelper output)
{
    #region 필드 선언 (C# 14)

    private readonly IDbSession _session = fixture.Session;
    private readonly IProcedureStage _db = fixture.Verification;
    private readonly ITestOutputHelper _output = output;

    #endregion

    #region BI01: 10K건 벌크 삽입 성공

    /// <summary>
    /// 10,000건을 BulkInsertAsync로 삽입하고 성공을 검증한다.
    /// </summary>
    [Fact]
    public async Task BI01_BulkInsert_10KRows_ShouldSucceed()
    {
        // Arrange
        const int rowCount = 10_000;
        int batchId = Random.Shared.Next(100_000, 999_999);

        List<BulkTargetRecord> records = Enumerable.Range(0, rowCount)
            .Select(i => new BulkTargetRecord
            {
                Data = $"BI01_{i:D5}_{Guid.NewGuid():N}"[..Math.Min(200, 40)],
                BatchId = batchId
            })
            .ToList();

        // Act
        Stopwatch sw = Stopwatch.StartNew();
        DbResult<long> result = await _session.BulkInsertAsync(
            "Verification",
            "[gap].[BulkTarget]",
            records, ct: TestContext.Current.CancellationToken);
        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue("10K건 벌크 삽입이 성공해야 합니다.");
        result.Value.Should().Be(rowCount, $"{rowCount}건이 삽입되어야 합니다.");

        _output.WriteLine($"=== BI01: BulkInsert {rowCount:N0}건 ===");
        _output.WriteLine($"소요 시간: {sw.Elapsed.TotalMilliseconds:F0}ms");
        _output.WriteLine($"처리량: {rowCount / sw.Elapsed.TotalSeconds:F0} rows/sec");

        // Cleanup
        await _db
            .Sql($"DELETE FROM [gap].[BulkTarget] WHERE BatchId = {batchId}")
            .ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region BI02: 커스텀 배치 사이즈

    /// <summary>
    /// BulkInsertOptions.BatchSize를 1,000으로 설정하여 5,000건을 삽입한다.
    /// </summary>
    [Fact]
    public async Task BI02_BulkInsert_WithCustomBatchSize_ShouldWork()
    {
        // Arrange
        const int rowCount = 5_000;
        int batchId = Random.Shared.Next(100_000, 999_999);

        List<BulkTargetRecord> records = Enumerable.Range(0, rowCount)
            .Select(i => new BulkTargetRecord
            {
                Data = $"BI02_{i:D5}",
                BatchId = batchId
            })
            .ToList();

        BulkInsertOptions options = new() { BatchSize = 1_000 };

        // Act
        DbResult<long> result = await _session.BulkInsertAsync(
            "Verification",
            "[gap].[BulkTarget]",
            records,
            options, ct: TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("커스텀 배치 사이즈 벌크 삽입이 성공해야 합니다.");
        result.Value.Should().Be(rowCount);

        // Cleanup
        await _db
            .Sql($"DELETE FROM [gap].[BulkTarget] WHERE BatchId = {batchId}")
            .ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region BI03: 빈 컬렉션

    /// <summary>
    /// 빈 List를 전달하면 0건을 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task BI03_BulkInsert_EmptyCollection_ShouldReturn0()
    {
        // Arrange
        List<BulkTargetRecord> records = [];

        // Act
        DbResult<long> result = await _session.BulkInsertAsync(
            "Verification",
            "[gap].[BulkTarget]",
            records, ct: TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("빈 컬렉션은 성공으로 처리되어야 합니다.");
        result.Value.Should().Be(0, "빈 컬렉션은 0건을 반환해야 합니다.");
    }

    #endregion

    #region BI04: 존재하지 않는 테이블

    /// <summary>
    /// 존재하지 않는 테이블명으로 BulkInsert를 시도하면 실패해야 한다.
    /// </summary>
    [Fact]
    public async Task BI04_BulkInsert_InvalidTable_ShouldReturnError()
    {
        // Arrange
        List<BulkTargetRecord> records =
        [
            new BulkTargetRecord { Data = "Test", BatchId = 1 }
        ];

        // Act
        DbResult<long> result = await _session.BulkInsertAsync(
            "Verification",
            "[gap].[NonExistentTable_BI04]",
            records, ct: TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse("존재하지 않는 테이블에 대한 벌크 삽입은 실패해야 합니다.");
        result.Error.Should().NotBeNull();

        _output.WriteLine($"=== BI04: 예상된 오류 ===");
        _output.WriteLine($"Kind: {result.Error?.Kind}");
        _output.WriteLine($"Message: {result.Error?.Message}");
    }

    #endregion

    #region BI05: BulkCopy vs TVP 성능 비교

    /// <summary>
    /// 10K건 BulkCopy와 10K건 TVP의 소요 시간을 비교한다.
    /// </summary>
    [Fact]
    public async Task BI05_BulkInsert_VsTvp_PerformanceComparison()
    {
        // Arrange
        const int rowCount = 10_000;
        int batchIdBulk = Random.Shared.Next(100_000, 499_999);
        int batchIdTvp = Random.Shared.Next(500_000, 999_999);

        // --- BulkCopy ---
        List<BulkTargetRecord> bulkRecords = Enumerable.Range(0, rowCount)
            .Select(i => new BulkTargetRecord
            {
                Data = $"BULK_{i:D5}",
                BatchId = batchIdBulk
            })
            .ToList();

        Stopwatch swBulk = Stopwatch.StartNew();
        DbResult<long> bulkResult = await _session.BulkInsertAsync(
            "Verification",
            "[gap].[BulkTarget]",
            bulkRecords, ct: TestContext.Current.CancellationToken);
        swBulk.Stop();

        // --- TVP (perf 스키마 사용 — 검증 완료된 SP) ---
        List<PerfBulkInsertTvp> tvpRecords = Enumerable.Range(0, rowCount)
            .Select(i => new PerfBulkInsertTvp
            {
                BatchNumber = batchIdTvp,
                Data = $"TVP_{i:D5}"
            })
            .ToList();

        Stopwatch swTvp = Stopwatch.StartNew();
        DbResult<int> tvpResult = await _db
            .Procedure("perf.usp_Perf_Bulk_Insert")
            .With(new { Items = tvpRecords })
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        swTvp.Stop();

        // Assert
        bulkResult.IsSuccess.Should().BeTrue("BulkCopy 삽입이 성공해야 합니다.");
        tvpResult.IsSuccess.Should().BeTrue("TVP 삽입이 성공해야 합니다.");

        _output.WriteLine($"=== BI05: BulkCopy vs TVP 성능 비교 ({rowCount:N0}건) ===");
        _output.WriteLine($"BulkCopy: {swBulk.Elapsed.TotalMilliseconds:F0}ms ({rowCount / swBulk.Elapsed.TotalSeconds:F0} rows/sec)");
        _output.WriteLine($"TVP:      {swTvp.Elapsed.TotalMilliseconds:F0}ms ({rowCount / swTvp.Elapsed.TotalSeconds:F0} rows/sec)");
        _output.WriteLine($"비율: BulkCopy는 TVP 대비 {swTvp.Elapsed.TotalMilliseconds / swBulk.Elapsed.TotalMilliseconds:F1}x");

        // Cleanup
        await _db
            .Sql($"DELETE FROM [gap].[BulkTarget] WHERE BatchId = {batchIdBulk}")
            .ExecuteAsync(TestContext.Current.CancellationToken);
        await _db
            .Sql($"DELETE FROM [perf].[BulkTest] WHERE BatchNumber = {batchIdTvp}")
            .ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion
}

#region BulkTarget DTO

/// <summary>
/// [gap].[BulkTarget] 테이블 BulkInsert용 DTO (SqlBulkCopy 열 매핑).
/// <para><b>[주의]</b> Property 이름이 테이블 컬럼명과 정확히 일치해야 합니다.</para>
/// </summary>
public sealed class BulkTargetRecord
{
    public string Data { get; set; } = "";
    public int BatchId { get; set; }
}

#endregion
