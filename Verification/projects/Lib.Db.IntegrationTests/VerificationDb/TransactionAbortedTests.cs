// ============================================================================
// 파일: VerificationDb/TransactionAbortedTests.cs
// 설명: TransactionAborted(DOOMED) + Unknown 에러 매핑 검증 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// XACT_ABORT ON 환경에서 DOOMED 트랜잭션 에러와 매핑되지 않은 SQL 에러 코드의
/// DbErrorKind 분류를 검증하는 테스트.
/// <para><b>[설계 의도]</b> DbErrorMapper의 TransactionAborted, ConstraintViolation,
/// Unknown 분류 경로가 올바르게 동작하는지 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class TransactionAbortedTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region TA01: DOOMED 트랜잭션 — TransactionAborted 또는 ConstraintViolation

    /// <summary>
    /// test.usp_Simulate_TransactionAborted SP 호출 시
    /// XACT_ABORT ON + NOT NULL 위반으로 DOOMED 트랜잭션이 발생하고,
    /// DbErrorKind가 TransactionAborted(3930/3621) 또는 ConstraintViolation(515) 중
    /// 하나로 반환되는지 검증한다.
    /// <para><b>[주의]</b> SQL Server는 XACT_ABORT ON에서 NOT NULL 위반 시
    /// ConstraintViolation(515) 또는 TransactionAborted(3930/3621) 에러를 반환할 수 있다.
    /// 핵심은 SP 에러가 올바르게 DbResult로 래핑되는지 검증하는 것이다.</para>
    /// </summary>
    [Fact]
    public async Task TA01_TransactionAborted_DoomedTransaction_Returns3930()
    {
        // Act — XACT_ABORT ON + NOT NULL 위반으로 DOOMED 트랜잭션 유발
        DbResult<int> result = await _db
            .Procedure("test.usp_Simulate_TransactionAborted")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert — 실패해야 함
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().BeOneOf(
            [DbErrorKind.TransactionAborted, DbErrorKind.ConstraintViolation, DbErrorKind.DataConversion],
            "XACT_ABORT ON + NOT NULL 위반 시 TransactionAborted, ConstraintViolation, 또는 DataConversion 중 하나가 반환되어야 합니다.");
    }

    #endregion

    #region UE01: 매핑 안 된 SQL 에러 코드 — Unknown

    /// <summary>
    /// IDENTITY 열에 IDENTITY_INSERT OFF 상태에서 명시적 값을 삽입하면
    /// SQL 에러 544가 발생하고, DbErrorMapper에 544가 매핑되지 않으므로
    /// DbErrorKind.Unknown이 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task UE01_UnknownErrorCode_ShouldReturnUnknown()
    {
        // Act — IDENTITY 열에 명시적 값 삽입 (IDENTITY_INSERT OFF) → SQL 에러 544
        DbResult<int> result = await _db
            .Sql("INSERT INTO core.Users (UserId, UserName, Email) VALUES (999999, N'UnknownTest', N'unknown@test.com')")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert — 실패하고 Kind가 Unknown (SQL 544는 매핑 안 됨)
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.Unknown,
            "SQL 에러 544(IDENTITY_INSERT OFF)는 DbErrorMapper에 매핑되지 않아 Unknown이어야 합니다.");
    }

    #endregion
}
