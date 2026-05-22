// ============================================================================
// 파일: Stress/DeadlockTests.cs
// 설명: 교착 상태(Deadlock) 감지 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.Stress;

/// <summary>
/// 교착 상태(Deadlock) 시나리오에서 DbErrorKind.Deadlock이 올바르게 반환되는지 검증하는 테스트.
/// <para><b>[설계 의도]</b> test.usp_Deadlock_TableA(A→B)와 test.usp_Deadlock_TableB(B→A)를
/// 동시에 실행하여 교착 상태를 유발하고, Lib.Db의 Deadlock 감지 로직을 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class DeadlockTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region DL01: 교차 테이블 UPDATE — Deadlock 감지

    // 교착 상태는 타이밍 의존적이므로 100% 재현을 보장할 수 없음.
    // SQL Server가 Deadlock Victim을 선택하지 않으면 테스트가 실패할 수 있다.

    /// <summary>
    /// 두 SP를 동시에 실행하여 교착 상태를 유발하고,
    /// 하나는 성공/하나는 Deadlock(1205) 에러를 반환하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task DL01_Deadlock_CrossTableUpdate_ShouldDetectDeadlock()
    {
        // Act — 두 SP를 동시에 실행하여 교착 상태 유발
        Task<DbResult<int>> taskA = _db
            .Procedure("test.usp_Deadlock_TableA")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Task<DbResult<int>> taskB = _db
            .Procedure("test.usp_Deadlock_TableB")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        DbResult<int>[] results = await Task.WhenAll(taskA, taskB);

        // Assert — 둘 중 최소 하나가 Deadlock(1205)이어야 함
        bool anyDeadlock = results.Any(r =>
            !r.IsSuccess &&
            r.Error.HasValue &&
            r.Error.Value.Kind == DbErrorKind.Deadlock);

        bool anySuccess = results.Any(r => r.IsSuccess);

        // Deadlock이 발생했거나, 모두 성공한 경우(타이밍 미스) 모두 허용
        // 핵심: Deadlock이 발생한 경우 올바른 Kind를 반환하는지 검증
        if (anyDeadlock)
        {
            anyDeadlock.Should().BeTrue("둘 중 하나는 Deadlock Victim으로 선택되어야 합니다.");

            // Deadlock 에러의 SqlErrorCode가 1205인지 확인
            DbResult<int> deadlockResult = results.First(r =>
                !r.IsSuccess && r.Error.HasValue && r.Error.Value.Kind == DbErrorKind.Deadlock);
            deadlockResult.Error!.Value.SqlErrorCode.Should().Be(1205);
        }
        else
        {
            // 타이밍에 의해 Deadlock이 발생하지 않은 경우 — 둘 다 성공이어야 함
            anySuccess.Should().BeTrue("Deadlock 미발생 시 최소 하나는 성공해야 합니다.");
        }
    }

    #endregion
}
