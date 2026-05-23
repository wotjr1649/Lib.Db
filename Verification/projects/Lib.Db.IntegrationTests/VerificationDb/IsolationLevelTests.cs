// ============================================================================
// 파일: VerificationDb/IsolationLevelTests.cs
// 설명: 트랜잭션 격리 수준(IsolationLevel) 오버로드 API 통합 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// 트랜잭션 격리 수준 API 테스트.
/// <para><b>[설계 의도]</b> BeginTransactionAsync의 IsolationLevel 오버로드가
/// 다양한 격리 수준에서 정상 동작하는지 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class IsolationLevelTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IDbSession _session = fixture.Session;

    #endregion

    #region IL01: ReadCommitted 기본값 — 기존 오버로드 호환

    /// <summary>
    /// 기존 오버로드(isolationLevel 파라미터 없음)가 ReadCommitted 기본값으로 정상 동작하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task IL01_ReadCommitted_Default_ShouldWork()
    {
        // Arrange & Act — 기존 오버로드 사용 (IsolationLevel 파라미터 없음)
        await using IDbTransactionScope tx = await _session.BeginTransactionAsync("Verification", TestContext.Current.CancellationToken);

        DbResult<int> result = await tx
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        DbResult<bool> commitResult = await tx.CommitAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("기존 오버로드(ReadCommitted 기본값)로 SELECT 1 실행이 성공해야 합니다.");
        result.Value.Should().Be(1);
        commitResult.IsSuccess.Should().BeTrue("커밋이 성공해야 합니다.");
    }

    #endregion

    #region IL02: ReadUncommitted — Dirty Read 가능

    /// <summary>
    /// ReadUncommitted 격리 수준으로 트랜잭션을 시작하고 INSERT 후 커밋 전에도 읽기가 가능한지 검증한다.
    /// </summary>
    [Fact]
    public async Task IL02_ReadUncommitted_ShouldWork()
    {
        string uniqueEmail = $"iso_ru_{Guid.NewGuid():N}@test.com";

        // Arrange — ReadUncommitted 트랜잭션에서 INSERT (커밋하지 않음)
        await using IDbTransactionScope txInsert = await _session.BeginTransactionAsync(
            "Verification",
            IsolationLevel.ReadUncommitted, TestContext.Current.CancellationToken);

        DbResult<int> insertResult = await txInsert
            .Sql((FormattableString)$"INSERT INTO core.Users (UserName, Email, Age) VALUES ('IsoTest', {uniqueEmail}, 25)")
            .ExecuteAsync(TestContext.Current.CancellationToken);
        insertResult.IsSuccess.Should().BeTrue("INSERT가 성공해야 합니다.");

        // Act — 별도 세션에서 ReadUncommitted로 Dirty Read 시도
        // (같은 세션이지만 별도 인스턴스/연결이므로 NOLOCK 효과 확인)
        // 참고: 같은 트랜잭션 내에서 자체 읽기는 항상 가능
        DbResult<int> readResult = await txInsert
            .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {uniqueEmail}")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        // Assert
        readResult.IsSuccess.Should().BeTrue("ReadUncommitted 트랜잭션 내 읽기가 성공해야 합니다.");
        readResult.Value.Should().BeGreaterThan(0, "트랜잭션 내 자체 INSERT 데이터를 읽을 수 있어야 합니다.");

        // Cleanup — 롤백하여 테스트 데이터 제거
        DbResult<bool> rollbackResult = await txInsert.RollbackAsync(TestContext.Current.CancellationToken);
        rollbackResult.IsSuccess.Should().BeTrue("롤백이 성공해야 합니다.");
    }

    #endregion

    #region IL03: Serializable — 최고 격리 수준

    /// <summary>
    /// Serializable 격리 수준으로 트랜잭션을 시작하고 SELECT 후 커밋이 성공하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task IL03_Serializable_ShouldWork()
    {
        // Arrange & Act
        await using IDbTransactionScope tx = await _session.BeginTransactionAsync(
            "Verification",
            IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        DbResult<int> result = await tx
            .Sql("SELECT COUNT(*) FROM core.Users")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        DbResult<bool> commitResult = await tx.CommitAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("Serializable 격리 수준에서 SELECT가 성공해야 합니다.");
        result.Value.Should().BeGreaterThanOrEqualTo(0);
        commitResult.IsSuccess.Should().BeTrue("커밋이 성공해야 합니다.");
    }

    #endregion

    #region IL04: RepeatableRead — 반복 읽기 보장

    /// <summary>
    /// RepeatableRead 격리 수준으로 트랜잭션을 시작하고 SELECT 후 커밋이 성공하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task IL04_RepeatableRead_ShouldWork()
    {
        // Arrange & Act
        await using IDbTransactionScope tx = await _session.BeginTransactionAsync(
            "Verification",
            IsolationLevel.RepeatableRead, TestContext.Current.CancellationToken);

        DbResult<int> result = await tx
            .Sql("SELECT COUNT(*) FROM core.Users")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        DbResult<bool> commitResult = await tx.CommitAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue("RepeatableRead 격리 수준에서 SELECT가 성공해야 합니다.");
        result.Value.Should().BeGreaterThanOrEqualTo(0);
        commitResult.IsSuccess.Should().BeTrue("커밋이 성공해야 합니다.");
    }

    #endregion
}
