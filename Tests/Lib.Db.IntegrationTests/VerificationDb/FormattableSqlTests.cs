// ============================================================================
// 파일: VerificationDb/FormattableSqlTests.cs
// 설명: FormattableString 기반 SQL 자동 파라미터화 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// FormattableString(보간 문자열)을 사용한 SQL 자동 파라미터화를 검증하는 테스트.
/// <para><b>[설계 의도]</b> <c>Sql((FormattableString)$"...")</c> 호출 시 보간 인수가 자동으로
/// @p0, @p1 등의 파라미터로 변환되어 SQL Injection을 방지하는지 검증한다.</para>
/// <para><b>[주의]</b> IProcedureStage 인터페이스에 Sql(string)과 Sql(FormattableString) 오버로드가
/// 공존하므로, C# 컴파일러가 기본적으로 string 오버로드를 선택한다.
/// FormattableString 오버로드를 명시적으로 호출하려면 <c>(FormattableString)</c> 캐스트가 필요하다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class FormattableSqlTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region 단일 파라미터 테스트

    /// <summary>
    /// 보간 문자열에 단일 파라미터를 전달하면 자동 파라미터화되어 정상 실행되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task InterpolatedSql_ShouldAutoParameterize()
    {
        // Arrange
        int userId = 1;

        // Act — FormattableString 오버로드 명시적 호출
        DbResult<CoreUser?> result = await _db
            .Sql((FormattableString)$"SELECT UserId, UserName, Email, Age, CreatedAt FROM [core].[Users] WHERE UserId = {userId}")
            .QuerySingleAsync<CoreUser>();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.UserId.Should().Be(1);
        result.Value.UserName.Should().Be("Alice");
    }

    #endregion

    #region 다중 파라미터 테스트

    /// <summary>
    /// 보간 문자열에 복수 파라미터를 전달하면 @p0, @p1 등으로 자동 변환되어 정상 동작하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task InterpolatedSql_MultipleParams_ShouldWork()
    {
        // Arrange
        string userName = "Alice";
        int minAge = 20;

        // Act — 복수 보간 인수 + FormattableString 명시적 캐스트
        DbResult<int> scalarResult = await _db
            .Sql((FormattableString)$"SELECT COUNT(*) FROM [core].[Users] WHERE UserName = {userName} AND Age >= {minAge}")
            .ExecuteScalarAsync<int>();

        // Assert
        scalarResult.IsSuccess.Should().BeTrue();
        scalarResult.Value.Should().BeGreaterThan(0, "Alice(Age=28)가 시드 데이터에 존재하므로 1건 이상이어야 합니다.");
    }

    #endregion
}
