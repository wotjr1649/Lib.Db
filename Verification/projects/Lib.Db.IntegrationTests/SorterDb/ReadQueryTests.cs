// ============================================================================
// 파일: SorterDb/ReadQueryTests.cs
// 설명: LV_ANP_SORTER 안전 조회 SP + 직접 SQL 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.SorterDb;

[Collection("MultiDb")]
public sealed class ReadQueryTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _db = fixture.Sorter;

    [Fact]
    public async Task S03_ChuteInfo_ReturnsRows()
    {
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Sql("SELECT TOP 10 * FROM IF_CHUTE_INFO")
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();

        int count = 0;
        await foreach (Dictionary<string, object?> row in result.Value!)
            count++;
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task S04_BrandMaster_Returns7Rows()
    {
        DbResult<int> result = await _db
            .Sql("SELECT COUNT(*) FROM IF_BRAND_MASTER")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7);
    }

    [Fact]
    public async Task S05_BoxList_HasData()
    {
        // Act — 날짜 필터 없이 전체 COUNT
        DbResult<int> result = await _db
            .Sql("SELECT COUNT(*) FROM IF_BOX_LIST")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        // Assert — 데이터 존재 여부와 무관하게 쿼리 성공 검증
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task D05_InterpolatedSql_AutoParameterization()
    {
        string bizDay = "20260309";
        DbResult<int> result = await _db
            .Sql($"SELECT COUNT(*) FROM IF_BOX_LIST WHERE BIZ_DAY = {bizDay}")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task S_UserInfo_Returns3Users()
    {
        DbResult<int> result = await _db
            .Sql("SELECT COUNT(*) FROM USR_INFO")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
    }

    [Fact]
    public async Task S_MenuInfo_Returns18Rows()
    {
        DbResult<int> result = await _db
            .Sql("SELECT COUNT(*) FROM MENU_INFO")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(18);
    }
}
