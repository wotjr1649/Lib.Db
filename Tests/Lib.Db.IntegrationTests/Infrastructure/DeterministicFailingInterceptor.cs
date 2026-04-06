// ============================================================================
// 파일: Infrastructure/DeterministicFailingInterceptor.cs
// 설명: 결정론적 실패 주입기 for Resilience Testing
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Data.Common;
using Lib.Db.Contracts.Execution;

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// 결정론적 실패 주입기 for Resilience Testing.
/// <para>
/// - EX-01: Deadlock(1205) 재시도 검증용<br/>
/// - 설정된 횟수만큼 지정된 에러를 발생시키고, 이후 성공시킴.
/// </para>
/// </summary>
public sealed class DeterministicFailingInterceptor : IDbCommandInterceptor
{
    private int _callCount = 0;

    /// <summary>주입할 에러 번호 (예: 1205 Deadlock)</summary>
    public int ErrorNumberToThrow { get; set; } = 1205;

    /// <summary>
    /// 몇 번째 시도에서 실패할지 (1-based).
    /// <para>Default: 1 (첫 번째 시도 실패 -> 재시도 -> 성공)</para>
    /// </summary>
    public int FailOnAttempt { get; set; } = 1;

    /// <summary>임의의 예외 주입 (설정 시 SqlExceptionFactory보다 우선)</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>실패 주입 횟수</summary>
    public int FailureInjectedCount { get; private set; }

    public ValueTask ReaderExecutingAsync(DbCommand command, DbCommandInterceptionContext context)
    {
        CheckAndInjectFailure(command);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReaderExecutedAsync(DbCommand command, DbCommandExecutedEventData eventData)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask CommandFailedAsync(DbCommand command, DbCommandFailedEventData eventData)
    {
        return ValueTask.CompletedTask;
    }

    private void CheckAndInjectFailure(DbCommand command)
    {
        int current = Interlocked.Increment(ref _callCount);

        if (current == FailOnAttempt)
        {
            FailureInjectedCount++;

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            Microsoft.Data.SqlClient.SqlException ex = SqlExceptionFactory.Create(ErrorNumberToThrow, $"Deterministic Failure Injection (Attempt {current})");
            throw ex;
        }
    }
}
