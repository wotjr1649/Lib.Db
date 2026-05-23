// ============================================================================
// 파일: VerificationDb/AdvancedErrorTests.cs
// 설명: 고급 에러 시나리오 테스트 (구문 오류, 파라미터 불일치, TRY-CATCH, NOT NULL, NULL 스칼라, 빈 결과)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// 고급 에러 시나리오를 검증하는 테스트.
/// <para><b>[설계 의도]</b> 구문 오류, 파라미터 불일치, TRY-CATCH 롤백, NOT NULL 위반,
/// NULL 스칼라 반환, 빈 결과셋 등 다양한 엣지 케이스에서 DbResult가 올바른 에러 종류를
/// 반환하는지 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class AdvancedErrorTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region AE01: 구문 오류 (QuerySyntax)

    /// <summary>
    /// 동적 SQL 구문 오류 시 DbErrorKind.QuerySyntax가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task AE01_QuerySyntax_DynamicSql_ShouldReturnQuerySyntax()
    {
        // Act
        DbResult<int> result = await _db
            .Procedure("test.usp_Exception_QuerySyntax")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.QuerySyntax);
    }

    #endregion

    #region AE02: 파라미터 불일치 (ParameterMismatch)

    /// <summary>
    /// EXEC 텍스트 모드로 SP에 존재하지 않는 파라미터를 전달하면
    /// DbErrorKind.ParameterMismatch (SQL 8144)가 반환되는지 검증한다.
    /// <para>Lib.Db의 스키마 캐시가 Fluent Procedure 모드에서는 알려진 파라미터만 전달하므로,
    /// 스키마 캐시를 우회하기 위해 Sql() 텍스트 모드로 EXEC를 호출한다.</para>
    /// </summary>
    [Fact]
    public async Task AE02_ParameterMismatch_ExtraParam_ShouldReturnParameterMismatch()
    {
        // Act — Sql 텍스트 모드로 존재하지 않는 파라미터를 직접 전달
        DbResult<int> result = await _db
            .Sql("EXEC core.usp_Core_Get_User @UserId = 1, @FakeParam = 'extra'")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.ParameterMismatch);
    }

    #endregion

    #region AE03: TRY-CATCH 롤백 — 실패 시

    /// <summary>
    /// ShouldFail=true 시 TRY-CATCH에서 THROW 50010이 발생하고 UserDefined가 반환되는지 검증한다.
    /// <para>SP 내부에서 고정 Email('txtest@test.com')을 사용하므로 UNIQUE 충돌 방지를 위해
    /// 사전 정리를 수행한다.</para>
    /// </summary>
    [Fact]
    public async Task AE03_TransactionAborted_TryCatch_ShouldPropagateError()
    {
        // Arrange — 이전 테스트의 고정 Email 데이터 정리 (UNIQUE 충돌 방지)
        await _db.Sql("DELETE FROM [core].[Users] WHERE Email = 'txtest@test.com'").ExecuteAsync(TestContext.Current.CancellationToken);

        // Act
        DbResult<int> result = await _db
            .Procedure("test.usp_Error_TryCatch_Rollback")
            .With(new { ShouldFail = true })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.UserDefined);
        result.Error!.Value.SqlErrorCode.Should().Be(50010);
    }

    #endregion

    #region AE04: TRY-CATCH 커밋 — 성공 시

    /// <summary>
    /// ShouldFail=false 시 TRY-CATCH 내 트랜잭션이 정상 커밋되는지 검증한다.
    /// <para>SP 내부에서 고정 Email('txtest@test.com')을 사용하므로 UNIQUE 충돌 방지를 위해
    /// 사전 정리를 수행한다.</para>
    /// </summary>
    [Fact]
    public async Task AE04_TransactionAborted_Success_ShouldCommit()
    {
        // Arrange — 이전 테스트의 고정 Email 데이터 정리 (UNIQUE 충돌 방지)
        await _db.Sql("DELETE FROM [core].[Users] WHERE Email = 'txtest@test.com'").ExecuteAsync(TestContext.Current.CancellationToken);

        // Act
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("test.usp_Error_TryCatch_Rollback")
            .With(new { ShouldFail = false })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        List<Dictionary<string, object?>> rows = await result.Value!.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().NotBeEmpty();
        rows[0]["Result"].Should().Be("COMMITTED");

        // Cleanup — 성공 시 삽입된 데이터 정리
        await _db.Sql("DELETE FROM [core].[Users] WHERE Email = 'txtest@test.com'").ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region AE05: NOT NULL 위반 (ConstraintViolation)

    /// <summary>
    /// NOT NULL 컬럼에 NULL 삽입 시 DbErrorKind.ConstraintViolation이 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task AE05_NotNull_Violation_ShouldReturnConstraintViolation()
    {
        // Act
        DbResult<int> result = await _db
            .Procedure("test.usp_Error_NotNull_Violation")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.ConstraintViolation);
    }

    #endregion

    #region AE06: NULL 스칼라 반환

    /// <summary>
    /// NULL을 반환하는 SP에서 ExecuteScalarAsync가 IsSuccess=true, Value=null로 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task AE06_NullScalar_ShouldReturnSuccessWithNull()
    {
        // Act
        DbResult<string?> result = await _db
            .Procedure("test.usp_Core_Get_NullScalar")
            .ExecuteScalarAsync<string?>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    #endregion

    #region AE07: 빈 결과셋

    /// <summary>
    /// WHERE 1=0 조건으로 빈 결과셋을 반환하는 SP에서 IsSuccess=true이고 0건인지 검증한다.
    /// </summary>
    [Fact]
    public async Task AE07_EmptyResultSet_ShouldReturnSuccessWithNoRows()
    {
        // Act
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("test.usp_Core_Get_Empty")
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        List<Dictionary<string, object?>> rows = await result.Value!.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().BeEmpty("WHERE 1=0 조건이므로 결과가 없어야 합니다.");
    }

    #endregion
}
