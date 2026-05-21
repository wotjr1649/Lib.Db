// ============================================================================
// 파일: VerificationDb/SmokeTests.cs
// 설명: 기본 연결 및 Fluent API 스모크 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class SmokeTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _verification = fixture.Verification;
    private readonly IProcedureStage _sorter = fixture.Sorter;

    [Fact]
    public async Task Verification_Connection_ShouldWork()
    {
        DbResult<int> result = await _verification
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task Sorter_Connection_ShouldWork()
    {
        DbResult<int> result = await _sorter
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task MultiDb_Parallel_ShouldWork()
    {
        Task<DbResult<int>> verificationTask = _verification
            .Sql("SELECT 42")
            .ExecuteScalarAsync<int>();

        Task<DbResult<int>> sorterTask = _sorter
            .Sql("SELECT 99")
            .ExecuteScalarAsync<int>();

        DbResult<int>[] results = await Task.WhenAll(verificationTask, sorterTask);
        DbResult<int> vResult = results[0];
        DbResult<int> sResult = results[1];

        vResult.IsSuccess.Should().BeTrue();
        vResult.Value.Should().Be(42);
        sResult.IsSuccess.Should().BeTrue();
        sResult.Value.Should().Be(99);
    }
}
