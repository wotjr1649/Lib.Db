// ============================================================================
// 파일: Lib.Db/Core/DbSession.cs
// 설명: DB 세션/트랜잭션 스코프 구현 (멀티 인스턴스 지원)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Diagnostics;
using Lib.Db.Execution.Bulk;
using Lib.Db.Fluent;

namespace Lib.Db.Core;


#region 구현

/// <summary>
/// [구현] 통합 DB 세션 관리자 — IDbSession의 유일한 구현체
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>멀티 인스턴스 지원</b>: ConcurrentDictionary 기반으로 인스턴스별 독립 연결/트랜잭션을 격리 관리합니다.<br/>
/// - <b>상태 패턴 적용</b>: 트랜잭션 활성 여부에 따라 내부 Executor를 동적으로 교체하여 클라이언트 코드 변경 없이 트랜잭션을 지원합니다.<br/>
/// - <b>Zero-Allocation 지향</b>: StringBuilder 재사용 및 보간 문자열 핸들러를 통해 런타임 할당을 최소화합니다.<br/>
/// - <b>안전한 리소스 해제</b>: AggregateException 기반의 견고한 Dispose 패턴으로 누수 없는 리소스 정리를 보장합니다.
/// </para>
/// </summary>
internal sealed class DbSession(
    IDbExecutorFactory executorFactory,
    IDbConnectionFactory connectionFactory,
    LibDbOptions options) : IDbSession, IDisposable
{
    #region 필드 선언 (C# 14)

    // [BulkInsert 최적화] PropertyInfo[] 캐싱 (타입별 1회만 리플렉션)
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> s_bulkPropertyCache = new();

    // [멀티 인스턴스 상태 관리] 인스턴스별 독립 연결/트랜잭션
    private readonly ConcurrentDictionary<string, DbInstanceState> _instances = new();

    // [최적화] 세션 내에서 SQL 빌더용 버퍼 재사용
    private readonly StringBuilder _sharedBuilder = new(capacity: 1024);

    // [동시성 제어] .NET 9+ System.Threading.Lock (object 대신 성능 개선)
    private readonly Lock _disposeLock = new();
    private bool _disposed;

    #endregion

    #region 세션 설정 및 시작

    /// <summary>
    /// 등록된 DB 인스턴스로 작업을 시작합니다.
    /// </summary>
    /// <param name="instanceName">대상 DB 인스턴스 이름</param>
    /// <returns>프로시저 단계 빌더</returns>
    public IProcedureStage Use(string instanceName)
    {
        CheckDisposed();

        DbInstanceState state = GetOrCreateInstanceState(instanceName);
        IDbExecutor executor = GetOrCreateExecutor(state);
        return new DbRequestBuilder(executor, instanceName);
    }

    /// <summary>
    /// Ad-hoc 연결 문자열을 직접 사용하여 작업을 시작합니다.
    /// <para>
    /// <b>[구현 전략]</b> 연결 문자열의 결정적 해시 기반 인스턴스명을 생성하고
    /// ConnectionFactory에 동적 등록합니다.
    /// </para>
    /// </summary>
    /// <param name="connectionString">직접 제공하는 연결 문자열</param>
    /// <returns>프로시저 단계 빌더</returns>
    public IProcedureStage UseConnectionString(string connectionString)
    {
        CheckDisposed();

        // [결정적 인스턴스명 생성] SHA256 해시 기반으로 동일 연결 문자열은 동일 인스턴스를 재사용
        string hash = ComputeConnectionStringHash(connectionString);
        string tempInstanceName = $"__adhoc_{hash}";

        DbInstanceState state = _instances.GetOrAdd(tempInstanceName, static (key, ctx) =>
        {
            // [ConnectionFactory에 런타임 등록]
            ctx.factory.RegisterAdHocInstance(key, ctx.connStr);

            return new DbInstanceState
            {
                InstanceName = key,
                ConnectionHash = key,
                IsAdHoc = true
            };
        }, (factory: connectionFactory, connStr: connectionString));

        IDbExecutor executor = GetOrCreateExecutor(state);
        return new DbRequestBuilder(executor, tempInstanceName);
    }

    /// <summary>
    /// 기본('Default') 인스턴스를 사용하여 작업을 시작합니다.
    /// <para>
    /// <b>[동작]</b><br/>
    /// <see cref="LibDbOptions.ConnectionStringNames"/>의 첫 번째 항목을 기본 인스턴스로 사용합니다.
    /// </para>
    /// </summary>
    public IProcedureStage Default => Use(options.ConnectionStringNames[0]);

    /// <summary>
    /// 인스턴스별 독립 트랜잭션 스코프를 시작합니다. (기본 격리 수준: ReadCommitted)
    /// </summary>
    /// <param name="instanceName">대상 인스턴스 이름</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>트랜잭션 스코프 인터페이스</returns>
    public Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        CancellationToken ct = default)
        => BeginTransactionAsync(instanceName, IsolationLevel.ReadCommitted, ct);

    /// <summary>
    /// 지정 인스턴스에서 특정 격리 수준으로 트랜잭션 스코프를 시작합니다.
    /// </summary>
    /// <param name="instanceName">대상 인스턴스 이름</param>
    /// <param name="isolationLevel">트랜잭션 격리 수준</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>트랜잭션 스코프 인터페이스</returns>
    public async Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        IsolationLevel isolationLevel,
        CancellationToken ct = default)
    {
        CheckDisposed();

        DbInstanceState state = GetOrCreateInstanceState(instanceName);

        if (state.Transaction is not null)
            throw new InvalidOperationException(
                $"인스턴스 '{instanceName}'에 이미 트랜잭션이 활성화되어 있습니다. Lib.Db는 인스턴스당 중첩 트랜잭션을 지원하지 않습니다.");

        // 1. 연결 생성 및 오픈
        state.Connection ??= await connectionFactory.CreateConnectionAsync(instanceName, ct).ConfigureAwait(false);
        if (state.Connection.State != ConnectionState.Open)
            await state.Connection.OpenAsync(ct).ConfigureAwait(false);

        // 2. 트랜잭션 시작 (지정된 격리 수준 사용)
        state.Transaction = (SqlTransaction)await state.Connection.BeginTransactionAsync(isolationLevel, ct).ConfigureAwait(false);

        // 3. Executor 교체 (Transactional Executor)
        state.ActiveExecutor = executorFactory.CreateTransactional(state.Connection, state.Transaction);

        return new DbTransactionScopeAdapter(this, state);
    }

    #endregion

    #region 벌크 연산

    /// <summary>
    /// SqlBulkCopy 기반 대량 INSERT를 수행합니다.
    /// <para>
    /// <b>[구현 전략]</b><br/>
    /// 1. DbConnectionFactory를 통해 연결을 획득합니다.<br/>
    /// 2. BulkInsertOptions를 SqlBulkCopyOptions로 변환합니다.<br/>
    /// 3. T의 public property를 기반으로 열 매핑을 수행합니다.<br/>
    /// 4. ObjectDataReader&lt;T&gt; 어댑터로 IDataReader를 생성합니다.<br/>
    /// 5. SqlBulkCopy.WriteToServerAsync로 데이터를 전송합니다.
    /// </para>
    /// </summary>
    [RequiresUnreferencedCode(
        "BulkInsertAsync는 Reflection을 사용하여 T의 속성을 열거합니다. AOT 환경에서는 사용할 수 없습니다.")]
    public async Task<DbResult<long>> BulkInsertAsync<T>(
        string instanceName,
        string destinationTable,
        IEnumerable<T> records,
        BulkInsertOptions? options,
        CancellationToken ct) where T : class
    {
        CheckDisposed();

        try
        {
            options ??= new BulkInsertOptions();

            // 레코드를 List로 구체화 (카운트 확인 + 재사용)
            List<T> recordList = records as List<T> ?? [.. records];
            if (recordList.Count == 0)
                return DbResult<long>.Ok(0);

            // 연결 획득 (기존 DbConnectionFactory 사용)
            await using SqlConnection connection = await connectionFactory
                .CreateConnectionAsync(instanceName, ct)
                .ConfigureAwait(false);

            // SqlBulkCopyOptions 구성
            SqlBulkCopyOptions copyOptions = SqlBulkCopyOptions.Default;
            if (options.FireTriggers) copyOptions |= SqlBulkCopyOptions.FireTriggers;
            if (options.CheckConstraints) copyOptions |= SqlBulkCopyOptions.CheckConstraints;
            if (options.KeepIdentity) copyOptions |= SqlBulkCopyOptions.KeepIdentity;

            using SqlBulkCopy bulkCopy = new(connection, copyOptions, null)
            {
                DestinationTableName = destinationTable,
                BatchSize = options.BatchSize,
                BulkCopyTimeout = options.TimeoutSeconds,
                EnableStreaming = options.EnableStreaming,
            };

            // T의 public instance property로 열 매핑 (타입별 캐싱)
            PropertyInfo[] properties = s_bulkPropertyCache.GetOrAdd(typeof(T),
                static t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
            foreach (PropertyInfo prop in properties)
                bulkCopy.ColumnMappings.Add(prop.Name, prop.Name);

            // IDataReader 어댑터를 통해 스트리밍 전송
            using ObjectDataReader<T> reader = new(recordList.GetEnumerator(), properties);
            await bulkCopy.WriteToServerAsync(reader, ct).ConfigureAwait(false);

            return DbResult<long>.Ok(recordList.Count);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = DbErrorMapper.FromSqlException(ex);
            return DbResult<long>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = new()
            {
                Kind = DbErrorKind.Unknown,
                Message = ex.Message,
                InnerException = ex
            };
            return DbResult<long>.Fail(error);
        }
    }

    #endregion

    #region 인스턴스 상태 관리

    /// <summary>
    /// 인스턴스별 상태를 가져오거나 새로 생성합니다.
    /// </summary>
    /// <param name="instanceName">대상 인스턴스 이름</param>
    /// <returns>인스턴스 상태 컨테이너</returns>
    private DbInstanceState GetOrCreateInstanceState(string instanceName)
    {
        return _instances.GetOrAdd(instanceName, static (key, _) => new DbInstanceState
        {
            InstanceName = key,
            ConnectionHash = key,
            IsAdHoc = false
        }, (object?)null);
    }

    /// <summary>
    /// 연결 문자열에서 결정적 해시를 생성합니다.
    /// <para>동일 연결 문자열은 항상 동일 해시를 반환하여 인스턴스를 재사용합니다.</para>
    /// </summary>
    /// <param name="connectionString">연결 문자열</param>
    /// <returns>16자 hex 해시 문자열</returns>
    private static string ComputeConnectionStringHash(string connectionString)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        int bytesWritten = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(connectionString), hashBytes);
        // 앞 8바이트(16자 hex)만 사용하여 충분히 고유하면서 간결한 인스턴스명 생성
        return Convert.ToHexString(hashBytes[..8]);
    }

    #endregion

    #region 트랜잭션 관리 (내부)

    /// <summary>
    /// 특정 인스턴스의 활성 트랜잭션을 커밋합니다. (내부 전용)
    /// </summary>
    /// <param name="state">대상 인스턴스 상태</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="InvalidOperationException">활성 트랜잭션이 없을 때</exception>
    internal async Task CommitInternalAsync(DbInstanceState state, CancellationToken ct = default)
    {
        CheckDisposed();
        if (state.Transaction is null)
            throw new InvalidOperationException(
                "활성화된 트랜잭션이 없습니다. BeginTransactionAsync()를 먼저 호출해야 합니다.");

        try
        {
            await state.Transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await DisposeInstanceTransactionAsync(state).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 특정 인스턴스의 활성 트랜잭션을 롤백합니다. (내부 전용)
    /// </summary>
    /// <param name="state">대상 인스턴스 상태</param>
    /// <param name="ct">취소 토큰</param>
    internal async Task RollbackInternalAsync(DbInstanceState state, CancellationToken ct = default)
    {
        CheckDisposed();
        if (state.Transaction is null)
            return; // 이미 없으면 무시

        try
        {
            await state.Transaction.RollbackAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await DisposeInstanceTransactionAsync(state).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 특정 인스턴스의 트랜잭션 리소스를 정리합니다.
    /// </summary>
    /// <param name="state">대상 인스턴스 상태</param>
    private static async Task DisposeInstanceTransactionAsync(DbInstanceState state)
    {
        if (state.Transaction is not null)
        {
            await state.Transaction.DisposeAsync().ConfigureAwait(false);
            state.Transaction = null;
        }

        // Executor를 다시 기본 상태로 되돌림
        state.ActiveExecutor = null;
    }

    #endregion

    #region Zero-Allocation SQL 문자열 보간

    /// <summary>
    /// 내부용 StringBuilder를 가져옵니다.
    /// </summary>
    internal StringBuilder GetSharedBuilder() => _sharedBuilder;

    #endregion

    #region 내부 헬퍼

    /// <summary>
    /// 인스턴스 상태에서 Executor를 가져오거나 생성합니다.
    /// </summary>
    /// <param name="state">인스턴스 상태</param>
    /// <returns>DB 실행기</returns>
    private IDbExecutor GetOrCreateExecutor(DbInstanceState state)
    {
        if (state.ActiveExecutor is not null)
            return state.ActiveExecutor;

        // 트랜잭션이 없다면, 일반(Resilient) Executor 생성
        return executorFactory.CreateResilient();
    }

    /// <summary>
    /// Dispose 상태를 확인하고, 이미 폐기되었다면 예외를 던집니다.
    /// </summary>
    /// <exception cref="ObjectDisposedException">이미 폐기된 경우</exception>
    private void CheckDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(
                nameof(DbSession),
                "DbSession이 이미 폐기되었습니다. 폐기된 세션은 재사용할 수 없습니다.");
    }

    #endregion

    #region 리소스 해제 (비동기 처분 패턴)

    /// <summary>
    /// 동기 Dispose 패턴 구현 (DI 컨테이너 호환성)
    /// </summary>
    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (DbInstanceState state in _instances.Values)
            {
                state.Transaction?.Dispose();
                state.Connection?.Dispose();
            }
        }
    }

    /// <summary>
    /// 비동기 DisposeAsync 패턴 구현
    /// <para>
    /// <b>[개선 사항]</b><br/>
    /// 1. 모든 인스턴스를 순회하며 각 리소스를 개별 try-catch로 감싸 하나가 실패해도 나머지 정리 보장<br/>
    /// 2. 모든 예외를 수집하여 AggregateException으로 던짐<br/>
    /// 3. System.Threading.Lock으로 동시성 보장
    /// </para>
    /// </summary>
    /// <exception cref="AggregateException">Dispose 중 하나 이상의 예외가 발생한 경우</exception>
    public async ValueTask DisposeAsync()
    {
        // [동시성 보장] Lock을 사용하여 중복 폐기 방지
        lock (_disposeLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        // [예외 수집] 모든 Dispose 작업의 예외를 모음
        List<Exception>? exceptions = null;

        // [인스턴스별 정리] 모든 인스턴스의 트랜잭션/Executor/연결 정리
        foreach (DbInstanceState state in _instances.Values)
        {
            // [1단계] Transaction 정리
            if (state.Transaction is not null)
            {
                try
                {
                    await state.Transaction.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exceptions ??= [];
                    exceptions.Add(new InvalidOperationException(
                        $"인스턴스 '{state.InstanceName}'의 트랜잭션 리소스 해제 중 오류가 발생했습니다.", ex));
                }
            }

            // [2단계] Executor 정리
            if (state.ActiveExecutor is IAsyncDisposable asyncDisposableExecutor)
            {
                try
                {
                    await asyncDisposableExecutor.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exceptions ??= [];
                    exceptions.Add(new InvalidOperationException(
                        $"인스턴스 '{state.InstanceName}'의 DB 실행기 리소스 해제 중 오류가 발생했습니다.", ex));
                }
            }

            // [3단계] Connection 정리 (반드시 실행되어야 함!)
            if (state.Connection is not null)
            {
                try
                {
                    await state.Connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exceptions ??= [];
                    exceptions.Add(new InvalidOperationException(
                        $"인스턴스 '{state.InstanceName}'의 DB 연결 리소스 해제 중 오류가 발생했습니다. Connection Pool이 고갈될 수 있습니다.", ex));
                }
            }

            // [4단계] Ad-hoc 인스턴스 등록 해제
            if (state.IsAdHoc)
            {
                try
                {
                    connectionFactory.UnregisterAdHocInstance(state.InstanceName);
                }
                catch (Exception ex)
                {
                    exceptions ??= [];
                    exceptions.Add(new InvalidOperationException(
                        $"임시 인스턴스 '{state.InstanceName}' 등록 해제 중 오류가 발생했습니다.", ex));
                }
            }
        }

        // [최종] 수집된 예외가 있다면 AggregateException으로 던짐
        if (exceptions is not null && exceptions.Count > 0)
        {
            throw new AggregateException(
                "DbSession 리소스 해제 중 하나 이상의 오류가 발생했습니다. 자세한 내용은 InnerExceptions를 확인하세요.",
                exceptions);
        }
    }

    #endregion
}

#endregion

#region 트랜잭션 스코프 어댑터

/// <summary>
/// [헬퍼 클래스] IDbTransactionScope 어댑터
/// <para>
/// 특정 <see cref="DbInstanceState"/>에 바인딩되어
/// 인스턴스별 독립 트랜잭션을 관리합니다.
/// DbResult 반환으로 커밋/롤백 성공/실패를 명시적으로 전달합니다.
/// </para>
/// </summary>
internal sealed class DbTransactionScopeAdapter : IDbTransactionScope
{
    private readonly DbSession _session;
    private readonly DbInstanceState _state;

    /// <summary>
    /// 특정 인스턴스 상태에 바인딩된 트랜잭션 스코프를 초기화합니다.
    /// </summary>
    /// <param name="session">소유 세션</param>
    /// <param name="state">바인딩 대상 인스턴스 상태</param>
    public DbTransactionScopeAdapter(DbSession session, DbInstanceState state)
    {
        _session = session;
        _state = state;
    }

    /// <summary>
    /// 저장 프로시저를 호출합니다.
    /// </summary>
    public IParameterStage Procedure(string spName)
    {
        return CreateBoundBuilder().Procedure(spName);
    }

    /// <summary>
    /// Raw SQL 문을 실행합니다.
    /// </summary>
    public IParameterStage Sql(string sqlText)
    {
        return CreateBoundBuilder().Sql(sqlText);
    }

    /// <summary>
    /// 보간 문자열 SQL을 실행합니다.
    /// </summary>
    public IParameterStage Sql(FormattableString sql)
    {
        return CreateBoundBuilder().Sql(sql);
    }

    /// <summary>
    /// 바인딩된 인스턴스의 Executor를 사용하는 DbRequestBuilder를 생성합니다.
    /// </summary>
    private DbRequestBuilder CreateBoundBuilder()
    {
        IDbExecutor executor = _state.ActiveExecutor
            ?? throw new InvalidOperationException(
                $"인스턴스 '{_state.InstanceName}'에 활성 트랜잭션 Executor가 없습니다. " +
                "트랜잭션이 이미 커밋 또는 롤백되었을 수 있습니다.");

        return new DbRequestBuilder(executor, _state.InstanceName);
    }

    /// <summary>
    /// 트랜잭션을 커밋합니다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>커밋 성공 여부를 포함하는 <see cref="DbResult{T}"/></returns>
    public async Task<DbResult<bool>> CommitAsync(CancellationToken ct = default)
    {
        try
        {
            await _session.CommitInternalAsync(_state, ct).ConfigureAwait(false);
            return DbResult<bool>.Ok(true);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = Diagnostics.DbErrorMapper.FromSqlException(ex);
            return DbResult<bool>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = new()
            {
                Kind = DbErrorKind.Unknown,
                Message = ex.Message,
                InnerException = ex
            };
            return DbResult<bool>.Fail(error);
        }
    }

    /// <summary>
    /// 트랜잭션을 롤백합니다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>롤백 성공 여부를 포함하는 <see cref="DbResult{T}"/></returns>
    public async Task<DbResult<bool>> RollbackAsync(CancellationToken ct = default)
    {
        try
        {
            await _session.RollbackInternalAsync(_state, ct).ConfigureAwait(false);
            return DbResult<bool>.Ok(true);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = Diagnostics.DbErrorMapper.FromSqlException(ex);
            return DbResult<bool>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = new()
            {
                Kind = DbErrorKind.Unknown,
                Message = ex.Message,
                InnerException = ex
            };
            return DbResult<bool>.Fail(error);
        }
    }

    /// <summary>
    /// Dispose 시 자동 롤백을 수행합니다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 트랜잭션이 아직 활성화되어 있으면 자동 롤백
        await _session.RollbackInternalAsync(_state).ConfigureAwait(false);
    }
}

#endregion
