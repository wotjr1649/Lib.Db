using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Schema;

namespace Lib.Db.Verification.Tests.Infrastructure;

public class MockDbExecutionStrategy : IDbExecutionStrategy
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
        // 실제 operation을 실행하지 않고 기본값을 반환하여 DB 의존성 제거
        // Bulk Pipeline의 경우 TResult는 int(count)임.
        // 하지만 실제 Count를 반환하지 않아도 Pipeline 흐름에는 영향이 없음(반환값 무시됨)
        // 단, 호출 횟수 검증이 목적임.
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
