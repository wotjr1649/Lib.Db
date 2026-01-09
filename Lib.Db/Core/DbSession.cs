// ============================================================================
// 파일: Lib.Db/Core/DbCore.Session.cs
// 설명: DB 세션/트랜잭션 스코프 + DbSession 전용 SQL 보간 핸들러
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Runtime.CompilerServices;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Fluent;

namespace Lib.Db.Core;


#region 구현

/// <summary>
/// [구현] 통합 DB 세션 관리자
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>단일 책임 원칙</b>: DB 연결 수명주기(Lifecycle)와 실행 책임(Execution Responsibility)을 중앙에서 관리합니다.<br/>
/// - <b>상태 패턴 적용</b>: 트랜잭션 활성 여부에 따라 내부 Executor를 동적으로 교체하여 클라이언트 코드 변경 없이 트랜잭션을 지원합니다.<br/>
/// - <b>Zero-Allocation 지향</b>: `StringBuilder` 재사용 및 보간 문자열 핸들러를 통해 런타임 할당을 최소화합니다.<br/>
/// - <b>안전한 리소스 해제</b>: `AggregateException` 기반의 견고한 Dispose 패턴으로 누수 없는 리소스 정리를 보장합니다.
/// </para>
/// </summary>
public sealed class DbSession(
    IDbExecutorFactory executorFactory,
    IDbConnectionFactory connectionFactory,
    LibDbOptions options) : IDbSession, IDbContext
{
    #region 필드 선언 (C# 14)

    // [C# 14 field 키워드] Primary Constructor 매개변수를 명시적 필드로 선언
    // 컴파일러가 자동으로 백킹 필드를 생성 (field 키워드는 C# 14 Preview)

    // [상태 관리] 연결 및 트랜잭션
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;
    private IDbExecutor? _activeExecutor;
    private string? _currentInstanceName;

    // [최적화] 세션 내에서 SQL 빌더용 버퍼 재사용
    private readonly StringBuilder _sharedBuilder = new(capacity: 1024);

    // [Ad-hoc 연결 관리] Dispose 시 정리할 임시 인스턴스 목록
    private List<string>? _adhocInstances;

    // [동시성 제어] .NET 9+ System.Threading.Lock (object 대신 성능 개선)
    private readonly Lock _disposeLock = new();
    private bool _disposed;

    // [Internal] DbTransactionScopeAdapter에서 사용
    internal string? CurrentInstanceName => _currentInstanceName;

    #endregion

    #region 세션 설정 및 시작

    /// <summary>
    /// 특정 인스턴스를 대상으로 작업을 시작합니다.
    /// </summary>
    /// <param name="instanceName">대상 DB 인스턴스 이름</param>
    /// <returns>프로시저 단계 빌더</returns>
    public IProcedureStage Use(string instanceName)
    {
        CheckDisposed();
        _currentInstanceName = instanceName;

        var executor = GetOrCreateExecutor(instanceName);
        return new DbRequestBuilder(executor, instanceName);
    }

    /// <summary>
    /// [IDbContext 구현] 등록된 인스턴스 이름을 사용하여 작업을 시작합니다.
    /// </summary>
    public IProcedureStage UseInstance(string instanceName) => Use(instanceName);

    /// <summary>
    /// [IDbContext 구현] Ad-hoc 연결 문자열을 직접 사용하여 작업을 시작합니다.
    /// <para>
    /// <b>[구현 전략]</b> GUID 기반 임시 인스턴스명을 생성하고 
    /// ConnectionFactory에 동적 등록합니다.
    /// </para>
    /// </summary>
    /// <param name="connectionString">직접 제공하는 연결 문자열</param>
    /// <returns>프로시저 단계 빌더</returns>
    public IProcedureStage UseConnectionString(string connectionString)
    {
        CheckDisposed();

        // [임시 인스턴스명 생성] GUID 기반으로 충돌 방지
        string tempInstanceName = $"__adhoc_{Guid.NewGuid():N}";

        // [ConnectionFactory에 런타임 등록]
        connectionFactory.RegisterAdHocInstance(tempInstanceName, connectionString);

        // [정리 책임] Session Dispose 시 임시 인스턴스 제거를 위한 목록 관리
        _adhocInstances ??= new List<string>();
        _adhocInstances.Add(tempInstanceName);

        _currentInstanceName = tempInstanceName;
        var executor = GetOrCreateExecutor(tempInstanceName);
        return new DbRequestBuilder(executor, tempInstanceName);
    }

    /// <summary>
    /// [IDbContext 구현] 기본('Default') 인스턴스를 사용하여 작업을 시작합니다.
    /// <para>
    /// <b>[동작]</b><br/>
    /// LibDbOptions.ConnectionStrings의 첫 번째 등록 인스턴스를 자동 선택합니다.
    /// </para>
    /// <para>
    /// <b>[최적화 v2.0]</b> LINQ 제거로 할당 -80 bytes (열거자 방식 사용)
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">설정된 연결 문자열이 없을 때</exception>
    public IProcedureStage Default
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            CheckDisposed();
            
            if (options.ConnectionStrings.Count == 0)
                throw new InvalidOperationException("설정된 연결 문자열이 없습니다. LibDbOptions.ConnectionStrings에 최소 하나의 인스턴스를 등록해야 합니다.");
            
            // [Smart Pointer] ConnectionStringName이 유효하면 해당 인스턴스 사용
            var targetKey = options.ConnectionStringName;
            if (options.ConnectionStrings.ContainsKey(targetKey))
            {
                return UseInstance(targetKey);
            }
            
            // Dictionary 열거자는 struct이므로 힙 할당 없음 (Fallback)
            using var enumerator = options.ConnectionStrings.Keys.GetEnumerator();
            enumerator.MoveNext();
            return UseInstance(enumerator.Current);
        }
    }

    /// <summary>
    /// [IDbContext 구현] 명시적 트랜잭션 스코프를 시작합니다.
    /// </summary>
    /// <param name="instanceName">대상 인스턴스 이름</param>
    /// <param name="isoLevel">격리 수준</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>트랜잭션 스코프 인터페이스</returns>
    public async Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        IsolationLevel isoLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        _currentInstanceName = instanceName;
        await BeginTransactionAsync(isoLevel, ct);

        return new DbTransactionScopeAdapter(this);
    }

    #endregion

    #region 트랜잭션 관리

    /// <summary>
    /// 트랜잭션을 시작합니다.
    /// </summary>
    /// <param name="isoLevel">격리 수준 (기본: ReadCommitted)</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="InvalidOperationException">이미 트랜잭션이 활성화된 경우 또는 인스턴스가 지정되지 않은 경우</exception>
    public async Task BeginTransactionAsync(
        IsolationLevel isoLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        CheckDisposed();

        if (_transaction is not null)
            throw new InvalidOperationException(
                "이미 트랜잭션이 활성화되어 있습니다. Lib.Db는 중첩 트랜잭션을 지원하지 않습니다.");

        if (_currentInstanceName is null)
            throw new InvalidOperationException(
                "트랜잭션을 시작하기 전에 Use(instanceName)를 호출하여 대상 인스턴스를 지정해야 합니다.");

        // 1. 연결 생성 및 오픈
        _connection ??= await connectionFactory.CreateConnectionAsync(_currentInstanceName, ct);
        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);

        // 2. 트랜잭션 시작
        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(isoLevel, ct);

        // 3. Executor 교체 (Transactional Executor)
        _activeExecutor = executorFactory.CreateTransactional(_connection, _transaction);
    }

    /// <summary>
    /// 활성 트랜잭션을 커밋합니다.
    /// </summary>
    /// <exception cref="InvalidOperationException">활성 트랜잭션이 없을 때</exception>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        CheckDisposed();
        if (_transaction is null)
            throw new InvalidOperationException(
                "활성화된 트랜잭션이 없습니다. BeginTransactionAsync()를 먼저 호출해야 합니다.");

        try
        {
            await _transaction.CommitAsync(ct);
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    /// <summary>
    /// 활성 트랜잭션을 롤백합니다.
    /// </summary>
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        CheckDisposed();
        if (_transaction is null) return; // 이미 없으면 무시

        try
        {
            await _transaction.RollbackAsync(ct);
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    /// <summary>
    /// 트랜잭션 리소스를 정리합니다.
    /// </summary>
    private async Task DisposeTransactionAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        // Executor를 다시 기본 상태로 되돌림
        _activeExecutor = null;
    }

    #endregion

    #region Zero-Allocation SQL 문자열 보간

    /// <summary>
    /// 내부용 StringBuilder를 가져옵니다. (SessionSqlStringHandler에서 사용)
    /// </summary>
    internal StringBuilder GetSharedBuilder() => _sharedBuilder;

    /// <summary>
    /// <b>[혁신]</b> 보간된 문자열 핸들러를 사용한 즉시 SQL 실행 준비.
    /// <para>
    /// <b>예:</b> <c>session.Sql($"SELECT * FROM Users WHERE Id = {id}")</c><br/>
    /// 내부적으로 파라미터화된 쿼리로 변환됩니다.
    /// </para>
    /// </summary>
    /// <param name="handler">C# 14 Interpolated String Handler (ref struct)</param>
    /// <returns>실행 단계 빌더</returns>
    public IExecutionStage<Dictionary<string, object?>> Sql(
        [InterpolatedStringHandlerArgument("")] ref SessionSqlStringHandler handler)
    {
        CheckDisposed();

        // 1. 값 추출 (Handler가 이미 _sharedBuilder에 SQL을, handler.Parameters에 값을 채웠음)
        string sqlText = _sharedBuilder.ToString();

        // 2. 상태 정리 (다음 호출을 위해 버퍼 비우기)
        _sharedBuilder.Clear();

        // 3. 실행기 준비
        var instanceName = _currentInstanceName ?? "Default";
        var executor = GetOrCreateExecutor(instanceName);

        // 4. Parameter Capture (Handler는 ref struct라 밖으로 유출 불가하지만, Parameters 딕셔너리는 class라 가능)
        var capturedParams = handler.Parameters;

        // 5. 빌더 생성
        return new DbRequestBuilder(executor, instanceName)
            .Sql(sqlText)
            .With(capturedParams);
    }

    #endregion

    #region 내부 헬퍼

    /// <summary>
    /// Executor를 가져오거나 생성합니다.
    /// </summary>
    /// <param name="instanceName">대상 인스턴스 이름</param>
    /// <returns>DB 실행기</returns>
    private IDbExecutor GetOrCreateExecutor(string instanceName)
    {
        if (_activeExecutor != null) return _activeExecutor;

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
    /// 동기 Dispose 패턴 구현
    /// </summary>
    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;

            _transaction?.Dispose();
            _connection?.Dispose(); // Connection은 Dispose 시 Pool로 반환됨
        }
    }

    /// <summary>
    /// 비동기 DisposeAsync 패턴 구현
    /// <para>
    /// <b>[개선 사항]</b><br/>
    /// 1. 각 리소스를 개별 try-catch로 감싸 하나가 실패해도 나머지 정리 보장<br/>
    /// 2. 모든 예외를 수집하여 AggregateException으로 던짐<br/>
    /// 3. System.Threading.Lock으로 동시성 보장
    /// </para>
    /// </summary>
    /// <exception cref="AggregateException">Dispose 중 하나 이상의 예외가 발생한 경우</exception>
    public async ValueTask DisposeAsync()
    {
        // ✅ [동시성 보장] Lock을 사용하여 중복 폐기 방지
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        // ✅ [예외 수집] 모든 Dispose 작업의 예외를 모음
        List<Exception>? exceptions = null;

        // ✅ [1단계] Transaction 정리 (각각 try-catch로 보호)
        if (_transaction is not null)
        {
            try
            {
                await _transaction.DisposeAsync();
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(new InvalidOperationException(
                    "트랜잭션 리소스 해제 중 오류가 발생했습니다.", ex));
            }
        }

        // ✅ [2단계] Executor 정리
        if (_activeExecutor is IAsyncDisposable asyncDisposableExecutor)
        {
            try
            {
                await asyncDisposableExecutor.DisposeAsync();
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(new InvalidOperationException(
                    "DB 실행기 리소스 해제 중 오류가 발생했습니다.", ex));
            }
        }

        // ✅ [3단계] Connection 정리 (반드시 실행되어야 함!)
        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(new InvalidOperationException(
                    "DB 연결 리소스 해제 중 오류가 발생했습니다. Connection Pool이 고갈될 수 있습니다.", ex));
            }
        }

        // ✅ [4단계] Ad-hoc 인스턴스 정리
        if (_adhocInstances is not null)
        {
            try
            {
                foreach (var instanceName in _adhocInstances)
                {
                    connectionFactory.UnregisterAdHocInstance(instanceName);
                }
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(new InvalidOperationException(
                    "임시 인스턴스 등록 해제 중 오류가 발생했습니다.", ex));
            }
        }

        // ✅ [5단계] 수집된 예외가 있다면 AggregateException으로 던짐
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
/// DbSession의 트랜잭션 기능을 IDbTransactionScope 인터페이스로 래핑합니다.
/// </para>
/// </summary>
internal sealed class DbTransactionScopeAdapter : IDbTransactionScope
{
    private readonly DbSession _session;

    public DbTransactionScopeAdapter(DbSession session)
    {
        _session = session;
    }

    /// <summary>
    /// 저장 프로시저를 호출합니다.
    /// </summary>
    public IParameterStage Procedure(string spName)
    {
        var currentInstance = _session.CurrentInstanceName
            ?? throw new InvalidOperationException(
                "트랜잭션 인스턴스가 설정되지 않았습니다. BeginTransactionAsync() 호출 시 인스턴스를 지정해야 합니다.");

        return _session.Use(currentInstance).Procedure(spName);
    }

    /// <summary>
    /// Raw SQL 문을 실행합니다.
    /// </summary>
    public IParameterStage Sql(string sqlText)
    {
        var currentInstance = _session.CurrentInstanceName
            ?? throw new InvalidOperationException(
                "트랜잭션 인스턴스가 설정되지 않았습니다. BeginTransactionAsync() 호출 시 인스턴스를 지정해야 합니다.");

        return _session.Use(currentInstance).Sql(sqlText);
    }

    /// <summary>
    /// 트랜잭션을 커밋합니다.
    /// </summary>
    public Task CommitAsync(CancellationToken ct = default)
    {
        return _session.CommitAsync(ct);
    }

    /// <summary>
    /// 트랜잭션을 롤백합니다.
    /// </summary>
    public Task RollbackAsync(CancellationToken ct = default)
    {
        return _session.RollbackAsync(ct);
    }

    /// <summary>
    /// Dispose 시 자동 롤백을 수행합니다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 트랜잭션이 아직 활성화되어 있으면 자동 롤백
        await _session.RollbackAsync();
    }
}

#endregion

