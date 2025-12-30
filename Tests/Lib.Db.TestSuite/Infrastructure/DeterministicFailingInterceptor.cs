// File: Lib.Db.Verification.Tests/Infrastructure/DeterministicFailingInterceptor.cs
#nullable enable

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Lib.Db.Contracts.Execution;

namespace Lib.Db.Verification.Tests.Infrastructure;

/// <summary>
/// 결정론적 실패 주입기 for Resilience Testing
/// <para>
/// - EX-01: Deadlock(1205) 재시도 검증용<br/>
/// - 설정된 횟수만큼 지정된 에러를 발생시키고, 이후 성공시킴.
/// </para>
/// </summary>
public class DeterministicFailingInterceptor : IDbCommandInterceptor
{
    private int _callCount = 0;

    /// <summary>
    /// 주입할 에러 번호 (예: 1205 Deadlock)
    /// </summary>
    public int ErrorNumberToThrow { get; set; } = 1205;

    /// <summary>
    /// 몇 번째 시도에서 실패할지 (1-based)
    /// <para>Default: 1 (첫 번째 시도 실패 -> 재시도 -> 성공)</para>
    /// </summary>
    public int FailOnAttempt { get; set; } = 1;
    
    // [New Option] 임의의 예외 주입 (설정 시 SqlExceptionFactory보다 우선)
    public Exception? ExceptionToThrow { get; set; }

    public int FailureInjectedCount { get; private set; }

    // [Interface Implementation]
    // IDbCommandInterceptor has only 3 methods: ReaderExecutingAsync, ReaderExecutedAsync, CommandFailedAsync.
    // All commands (Scalar, NonQuery) go through ReaderExecutingAsync or are covered by it in this architecture.

    public ValueTask ReaderExecutingAsync(DbCommand command, DbCommandInterceptionContext context)
    {
        // 모든 커맨드 실행 전 주입 시도
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
        // 스레드 안전하게 호출 횟수 증가
        int current = Interlocked.Increment(ref _callCount);

        if (current == FailOnAttempt)
        {
            FailureInjectedCount++;
            
            // [Extensions] 명시적 예외가 설정되어 있다면 그것을 우선 투척
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            // [Default] 기존 동작: SqlExceptionFactory 사용
            var ex = SqlExceptionFactory.Create(ErrorNumberToThrow, $"Deterministic Failure Injection (Attempt {current})");
            throw ex;
        }
    }
}
