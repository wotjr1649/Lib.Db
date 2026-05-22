// ============================================================================
// 파일: VerificationDb/TransactionTests.cs
// 설명: 트랜잭션 커밋/롤백/자동롤백/세이브포인트/순차 무결성 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// 트랜잭션 커밋, 롤백, 자동 롤백, 세이브포인트, 순차 무결성 테스트.
/// <para><b>[설계 의도]</b> IDbTransactionScope의 CommitAsync, RollbackAsync, 자동 Dispose 롤백,
/// SP 내부 세이브포인트, 순차 트랜잭션 혼합 시나리오를 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class TransactionTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IDbSession _session = fixture.Session;
    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region TX01: 커밋 — 데이터 영속

    /// <summary>
    /// 트랜잭션 커밋 후 INSERT된 데이터가 영속되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task V07_Transaction_Commit_ShouldPersist()
    {
        string uniqueEmail = $"commit_{Guid.NewGuid():N}@test.com";

        await using IDbTransactionScope tx = await _session.BeginTransactionAsync("Verification", TestContext.Current.CancellationToken);
        DbResult<int> insertResult = await tx
            .Sql((FormattableString)$"INSERT INTO core.Users (UserName, Email, Age) VALUES ('CommitTest', {uniqueEmail}, 30)")
            .ExecuteAsync(TestContext.Current.CancellationToken);
        insertResult.IsSuccess.Should().BeTrue();

        DbResult<bool> commitResult = await tx.CommitAsync(TestContext.Current.CancellationToken);
        commitResult.IsSuccess.Should().BeTrue();

        // 커밋 후 데이터 존재 확인
        DbResult<int> countResult = await _db
            .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {uniqueEmail}")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        countResult.IsSuccess.Should().BeTrue();
        countResult.Value.Should().BeGreaterThan(0);
    }

    #endregion

    #region TX02: 롤백 — 데이터 미영속

    /// <summary>
    /// 트랜잭션 롤백 후 INSERT된 데이터가 사라지는지 검증한다.
    /// </summary>
    [Fact]
    public async Task V08_Transaction_Rollback_ShouldNotPersist()
    {
        string uniqueEmail = $"rollback_{Guid.NewGuid():N}@test.com";

        await using IDbTransactionScope tx = await _session.BeginTransactionAsync("Verification", TestContext.Current.CancellationToken);
        DbResult<int> insertResult = await tx
            .Sql((FormattableString)$"INSERT INTO core.Users (UserName, Email, Age) VALUES ('RollbackTest', {uniqueEmail}, 30)")
            .ExecuteAsync(TestContext.Current.CancellationToken);
        insertResult.IsSuccess.Should().BeTrue();

        DbResult<bool> rollbackResult = await tx.RollbackAsync(TestContext.Current.CancellationToken);
        rollbackResult.IsSuccess.Should().BeTrue();

        // 롤백 후 데이터 미존재 확인
        DbResult<int> countResult = await _db
            .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {uniqueEmail}")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        countResult.IsSuccess.Should().BeTrue();
        countResult.Value.Should().Be(0);
    }

    #endregion

    #region TX03: 자동 롤백 — CommitAsync 없이 Dispose

    /// <summary>
    /// CommitAsync 호출 없이 트랜잭션 스코프가 Dispose되면 자동 롤백이 수행되는지 검증한다.
    /// <para><b>[설계 의도]</b> 'Secure by Default' 원칙에 따라, 명시적 커밋 없이는
    /// 데이터가 절대 반영되지 않아야 한다.</para>
    /// </summary>
    [Fact]
    public async Task TX03_AutoRollback_DisposeWithoutCommit_ShouldNotPersist()
    {
        string uniqueEmail = $"autorollback_{Guid.NewGuid():N}@test.com";

        // Arrange & Act — CommitAsync 호출 없이 블록 종료 → Dispose → 자동 롤백
        {
            await using IDbTransactionScope tx = await _session.BeginTransactionAsync("Verification", TestContext.Current.CancellationToken);
            await tx
                .Sql((FormattableString)$"INSERT INTO core.Users (UserName, Email) VALUES ('AutoRollbackTest', {uniqueEmail})")
                .ExecuteAsync(TestContext.Current.CancellationToken);
            // CommitAsync 호출 없이 블록 종료
        }

        // Assert — 데이터가 존재하면 안 됨
        DbResult<int> countResult = await _db
            .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {uniqueEmail}")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        countResult.IsSuccess.Should().BeTrue();
        countResult.Value.Should().Be(0, "CommitAsync 없이 Dispose 시 자동 롤백되어야 합니다.");
    }

    #endregion

    #region TX04: 세이브포인트 — SP 내부 부분 롤백

    /// <summary>
    /// core.usp_Core_Transaction_Test SP 호출 시 ShouldRollback=1이면
    /// 세이브포인트로 부분 롤백 후 'ROLLED_BACK_TO_SAVEPOINT' 결과를 반환하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task TX04_Savepoint_PartialRollback_ShouldWork()
    {
        // Act — ShouldRollback=1로 세이브포인트 롤백
        string uniqueEmail = $"savepoint_{Guid.NewGuid():N}@test.com";
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("core.usp_Core_Transaction_Test")
            .With(new { UserName = "SavepointTest", Email = uniqueEmail, ShouldRollback = 1 })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert — SP 자체가 성공하고, 결과에 'ROLLED_BACK_TO_SAVEPOINT' 포함
        result.IsSuccess.Should().BeTrue();

        string? resultText = null;
        await foreach (Dictionary<string, object?> row in result.Value!)
        {
            if (row.TryGetValue("Result", out object? val))
                resultText = val?.ToString();
        }
        resultText.Should().Be("ROLLED_BACK_TO_SAVEPOINT");

        // 세이브포인트 롤백으로 인해 데이터가 존재하지 않아야 함
        DbResult<int> countResult = await _db
            .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {uniqueEmail}")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        countResult.IsSuccess.Should().BeTrue();
        countResult.Value.Should().Be(0, "세이브포인트 롤백으로 INSERT가 취소되어야 합니다.");
    }

    #endregion

    #region TX05: 순차 트랜잭션 혼합 — 커밋/롤백 무결성

    /// <summary>
    /// 5개 트랜잭션을 순차 실행(3 커밋 + 2 롤백)하고,
    /// 커밋된 3개만 데이터가 존재하고 롤백된 2개는 미존재하는지 검증한다.
    /// <para><b>[설계 의도]</b> 트랜잭션 내 SQL은 FormattableString을 사용하여
    /// SP 스키마 캐시 의존성을 제거하고 순수 트랜잭션 동작만 검증한다.</para>
    /// </summary>
    [Fact]
    public async Task TX05_MultipleSequential_Integrity_ShouldMaintain()
    {
        // Arrange — 5개 고유 이메일 생성
        string[] emails = new string[5];
        for (int i = 0; i < 5; i++)
            emails[i] = $"sequential_{i}_{Guid.NewGuid():N}@test.com";

        // Act — 짝수 인덱스(0,2,4) 커밋, 홀수 인덱스(1,3) 롤백
        for (int i = 0; i < 5; i++)
        {
            string email = emails[i];
            string userName = $"SeqUser_{i}";
            await using IDbTransactionScope tx = await _session.BeginTransactionAsync("Verification", TestContext.Current.CancellationToken);
            DbResult<int> insertResult = await tx
                .Sql((FormattableString)$"INSERT INTO core.Users (UserName, Email) VALUES ({userName}, {email})")
                .ExecuteAsync(TestContext.Current.CancellationToken);
            insertResult.IsSuccess.Should().BeTrue();

            if (i % 2 == 0)
                await tx.CommitAsync(TestContext.Current.CancellationToken);
            else
                await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }

        // Assert — 커밋된 3개만 존재, 롤백된 2개는 미존재
        for (int i = 0; i < 5; i++)
        {
            DbResult<int> countResult = await _db
                .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {emails[i]}")
                .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
            countResult.IsSuccess.Should().BeTrue();

            if (i % 2 == 0)
                countResult.Value.Should().Be(1, $"인덱스 {i}는 커밋됨 — 데이터가 존재해야 합니다.");
            else
                countResult.Value.Should().Be(0, $"인덱스 {i}는 롤백됨 — 데이터가 없어야 합니다.");
        }
    }

    #endregion

    #region TX06: 세이브포인트 부분 커밋 — A 유지, B 롤백

    /// <summary>
    /// test.usp_Savepoint_PartialCommit SP 호출 시
    /// EmailA는 커밋되어 유지되고, EmailB는 세이브포인트 롤백으로 삭제되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task TX06_Savepoint_PartialCommit_OnlyFirstPersists()
    {
        // Arrange — 고유 이메일 2개
        string emailA = $"savepoint_a_{Guid.NewGuid():N}@test.com";
        string emailB = $"savepoint_b_{Guid.NewGuid():N}@test.com";

        // Act — SP 호출: A 삽입 → 세이브포인트 → B 삽입 → 세이브포인트 롤백 → 커밋
        DbResult<int> result = await _db
            .Procedure("test.usp_Savepoint_PartialCommit")
            .With(new { EmailA = emailA, EmailB = emailB })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();

        // Assert — EmailA 존재 (유지됨)
        DbResult<int> countA = await _db
            .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {emailA}")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        countA.IsSuccess.Should().BeTrue();
        countA.Value.Should().Be(1, "EmailA는 세이브포인트 이전에 삽입되어 유지되어야 합니다.");

        // Assert — EmailB 미존재 (롤백됨)
        DbResult<int> countB = await _db
            .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {emailB}")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        countB.IsSuccess.Should().BeTrue();
        countB.Value.Should().Be(0, "EmailB는 세이브포인트 롤백으로 삭제되어야 합니다.");
    }

    #endregion
}
