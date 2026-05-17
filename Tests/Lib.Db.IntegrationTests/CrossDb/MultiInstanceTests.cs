// ============================================================================
// 파일: CrossDb/MultiInstanceTests.cs
// 설명: IDbSession 멀티 인스턴스(Default/Use/UseConnectionString/Parallel) 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.CrossDb;

/// <summary>
/// IDbSession의 Default, Use, UseConnectionString, 병렬 멀티 DB 접근을 검증하는 테스트.
/// <para><b>[설계 의도]</b> 멀티 인스턴스 아키텍처에서 각 진입점이 올바른 DB에 연결되고,
/// 병렬 실행 시 간섭 없이 독립적으로 동작하는지 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class MultiInstanceTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IDbSession _session = fixture.Session;
    private readonly IProcedureStage _verification = fixture.Verification;
    private readonly IProcedureStage _sorter = fixture.Sorter;
    private readonly string _verificationConnectionString = fixture.GetConnectionString(TestConnectionStrings.Verification);
    private readonly string _sorterConnectionString = fixture.GetConnectionString(TestConnectionStrings.Sorter);

    #endregion

    #region MI01: Default 인스턴스 — Verification DB

    /// <summary>
    /// Session.Default가 첫 번째 등록된 DB(Verification)에 연결되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task MI01_Default_Instance_ShouldBeVerification()
    {
        // Act
        DbResult<string?> result = await _session.Default
            .Sql("SELECT DB_NAME()")
            .ExecuteScalarAsync<string>();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(GetInitialCatalog(_verificationConnectionString));
    }

    #endregion

    #region MI02: Named 인스턴스 — Use("Verification")

    /// <summary>
    /// Session.Use("Verification")으로 명시적 인스턴스 선택 후 SELECT 1이 성공하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task MI02_Use_NamedInstance_ShouldWork()
    {
        // Act
        DbResult<int> result = await _session.Use("Verification")
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    #endregion

    #region MI03: Ad-hoc 연결 문자열 — UseConnectionString

    /// <summary>
    /// UseConnectionString으로 직접 연결 문자열을 지정하여 올바른 DB에 연결되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task MI03_UseConnectionString_AdHoc_ShouldWork()
    {
        // Act
        DbResult<string?> result = await _session.UseConnectionString(_verificationConnectionString)
            .Sql("SELECT DB_NAME()")
            .ExecuteScalarAsync<string>();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(GetInitialCatalog(_verificationConnectionString));
    }

    #endregion

    #region MI04: 병렬 2 DB — 동시 성공

    /// <summary>
    /// Verification + Sorter 두 DB에 동시에 SELECT 1을 실행하여 모두 성공하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task MI04_Parallel_TwoDb_BothSucceed()
    {
        // Act — 두 DB 동시 SELECT 1
        Task<DbResult<int>> vTask = _verification
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>();

        Task<DbResult<int>> sTask = _sorter
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>();

        DbResult<int>[] results = await Task.WhenAll(vTask, sTask).ConfigureAwait(false);

        // Assert — 둘 다 성공
        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Value.Should().Be(1);
        });
    }

    #endregion

    #region MI05: 인스턴스 전환 — DB_NAME 확인

    /// <summary>
    /// Use("Verification")와 Use("Sorter")로 인스턴스 전환 시
    /// 각각 올바른 DB에 연결되는지 DB_NAME()으로 검증한다.
    /// </summary>
    [Fact]
    public async Task MI05_InstanceSwitch_DbName_ShouldMatch()
    {
        // Act — Verification DB
        DbResult<string?> vResult = await _session.Use("Verification")
            .Sql("SELECT DB_NAME()")
            .ExecuteScalarAsync<string>();

        // Assert
        vResult.IsSuccess.Should().BeTrue();
        vResult.Value.Should().Be(GetInitialCatalog(_verificationConnectionString));

        // Act — Sorter DB
        DbResult<string?> sResult = await _session.Use("Sorter")
            .Sql("SELECT DB_NAME()")
            .ExecuteScalarAsync<string>();

        // Assert
        sResult.IsSuccess.Should().BeTrue();
        sResult.Value.Should().Be(GetInitialCatalog(_sorterConnectionString));
    }

    #endregion

    #region MI06: WithTimeout + ExecuteScalar — 타임아웃 발생

    /// <summary>
    /// 10초 지연 SQL에 2초 타임아웃을 설정하면 Timeout 에러가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task MI06_WithTimeout_QueryAsync_ShouldTimeout()
    {
        // Act — 10초 지연, 2초 타임아웃 (FormattableString SQL)
        DbResult<int> result = await _verification
            .Sql((FormattableString)$"WAITFOR DELAY '00:00:10'; SELECT 1")
            .WithTimeout(2)
            .ExecuteScalarAsync<int>();

        // Assert — 타임아웃 에러
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.Timeout,
            "2초 타임아웃에 10초 지연이면 타임아웃이 발생해야 합니다.");
    }

    #endregion

    private static string GetInitialCatalog(string connectionString)
        => new SqlConnectionStringBuilder(connectionString).InitialCatalog;
}
