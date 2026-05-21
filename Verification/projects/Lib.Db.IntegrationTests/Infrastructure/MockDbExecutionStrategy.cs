// ============================================================================
// 파일: Infrastructure/MockDbExecutionStrategy.cs
// 설명: 테스트용 Mock DB 실행 전략
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Schema;

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// DB 의존성을 제거한 Mock 실행 전략.
/// </summary>
public sealed class MockDbExecutionStrategy : IDbExecutionStrategy
{
    public int ExecuteAsyncCount { get; private set; }
    public int ExecuteStreamAsyncCount { get; private set; }

    public bool IsTransactional => false;
    public SqlTransaction? CurrentTransaction => null;
    public SchemaResolutionMode DefaultSchemaMode => SchemaResolutionMode.SnapshotThenServiceFallback;

    public void EnlistTransaction(SqlCommand cmd) { }

    public Task<TResult> ExecuteAsync<TResult, TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken ct)
    {
        ExecuteAsyncCount++;
        return Task.FromResult<TResult>(default!);
    }

    public Task<DbDataReader?> ExecuteStreamAsync<TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<SqlDataReader>> operation,
        CancellationToken ct)
    {
        ExecuteStreamAsyncCount++;
        return Task.FromResult<DbDataReader?>(null);
    }
}
