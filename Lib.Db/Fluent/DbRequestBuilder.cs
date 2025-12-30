// ============================================================================
// 파일: Lib.Db/Fluent/DbRequestBuilder.cs
// 설명: Fluent API 빌더 구현체 (Stateful Builder)
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Executors;

namespace Lib.Db.Fluent;

/// <summary>
/// DB 명령을 생성하고 실행하는 Fluent API 빌더입니다. (상태 보유)
/// <para>
/// <b>[사용 예시]</b><br/>
/// builder.Procedure("usp_GetUsers").With(new { Id = 1 }).QueryAsync&lt;User&gt;();
/// </para>
/// </summary>
public sealed class DbRequestBuilder : IProcedureStage, IParameterStage
{
    #region [필드] 내부 상태 필드

    private readonly IDbExecutor _executor;
    private readonly string _instanceName;
    
    // 빌더 상태 (State)
    private string _commandText = string.Empty;
    private CommandType _commandType = CommandType.Text;
    private int? _timeout;
    private SchemaResolutionMode? _schemaModeOverride;

    #endregion

    #region [생성자] 및 [고급 설정]

    /// <summary>
    /// 지정된 실행기(Executor)와 인스턴스 이름으로 빌더를 초기화합니다.
    /// </summary>
    public DbRequestBuilder(IDbExecutor executor, string instanceName)
    {
        _executor = executor;
        _instanceName = instanceName;
    }

    /// <summary>
    /// [고급 설정] SP 실행 시 스키마 해석 모드(SchemaResolutionMode)를 강제로 지정합니다.
    /// <para>
    /// 기본값은 Executor의 전략을 따르지만, 이 메서드로 특정 명령에 대해서만 동작을 변경할 수 있습니다.
    /// </para>
    /// </summary>
    /// <param name="mode">적용할 스키마 해석 모드 (예: SnapshotOnly, MetadataService 등)</param>
    public void OverrideSchemaMode(SchemaResolutionMode mode)
    {
        _schemaModeOverride = mode;
    }

    #endregion

    #region [확장 메서드] 절차적 단계 (Procedure Stage)

    /// <summary>
    /// 실행할 저장 프로시저(Stored Procedure)의 이름을 지정합니다.
    /// </summary>
    /// <param name="spName">저장 프로시저 이름 (예: dbo.usp_GetUser)</param>
    /// <returns>파라미터 설정 단계로 이동</returns>
    public IParameterStage Procedure(string spName)
    {
        _commandText = spName;
        _commandType = CommandType.StoredProcedure;
        return this;
    }

    /// <summary>
    /// 실행할 인라인 SQL 쿼리를 지정합니다.
    /// </summary>
    /// <param name="sqlText">SQL 쿼리 문장 (예: SELECT * FROM Users WHERE Id = @Id)</param>
    /// <returns>파라미터 설정 단계로 이동</returns>
    public IParameterStage Sql(string sqlText)
    {
        _commandText = sqlText;
        _commandType = CommandType.Text;
        return this;
    }

    /// <summary>
    /// FormattableString(보간된 문자열)을 사용하여 SQL을 지정합니다. (파라미터화는 지원되지 않음 - 단순 텍스트 변환)
    /// </summary>
    /// <param name="sql">보간된 SQL 문자열</param>
    /// <returns>파라미터가 이미 포함된 것으로 간주하는 실행 단계</returns>
    public IExecutionStage<Dictionary<string, object?>> Sql(FormattableString sql)
    {
        // 주의: 현재 구현은 FormattableString을 단순 문자열로 변환합니다. 
        // 실제 파라미터화(Parameterize) 로직은 포함되어 있지 않습니다.
        _commandText = sql.Format; 
        _commandType = CommandType.Text;
        
        return new ExecutionStage<Dictionary<string, object?>>(_executor, _instanceName, _commandText, _commandType, 
            new Dictionary<string, object?>(), _timeout, _schemaModeOverride);
    }
    
    /// <summary>
    /// SqlInterpolatedStringHandler를 사용한 Zero-Allocation SQL 생성 및 자동 파라미터화
    /// <para>
    /// <b>[Zero-Allocation 전략]</b><br/>
    /// - ArrayPool 기반 버퍼 관리<br/>
    /// - Span&lt;char&gt; 기반 문자열 조합<br/>
    /// - 자동 파라미터 수집 (@p0, @p1, ...)<br/>
    /// - SQL Injection 자동 방지
    /// </para>
    /// </summary>
    /// <param name="handler">컴파일러가 자동 생성하는 SqlInterpolatedStringHandler</param>
    /// <returns>파라미터가 바인딩된 실행 단계</returns>
    /// <example>
    /// <code>
    /// int userId = 123;
    /// var user = await db.Default
    ///     .Sql($"SELECT * FROM Users WHERE Id = {userId}")
    ///     .QuerySingleAsync&lt;User&gt;();
    /// 
    /// // 생성된 SQL: "SELECT * FROM Users WHERE Id = @p0"
    /// // 파라미터: { "@p0": 123 }
    /// </code>
    /// </example>
    public IExecutionStage<Dictionary<string, object?>> Sql(
        ref SqlInterpolatedStringHandler handler)
    {
        var (sql, parameters) = handler.GetResult();
        
        // 버퍼 반환 (ArrayPool)
        handler.Dispose();
        
        _commandText = sql;
        _commandType = CommandType.Text;
        
        return new ExecutionStage<Dictionary<string, object?>>(
            _executor, 
            _instanceName, 
            _commandText, 
            _commandType, 
            parameters, 
            _timeout, 
            _schemaModeOverride);
    }
    
    /// <summary>
    /// 최적화된 SQL 포맷팅을 지원하는 메서드입니다. (Span 호환성 구현)
    /// <para>
    /// <b>[성능 노트]</b><br/>
    /// - ArrayPool을 사용하여 Span을 배열로 변환하는 오버헤드를 최소화합니다.<br/>
    /// - <see cref="string.Format(IFormatProvider, string, object[])"/>을 내부적으로 호출합니다.
    /// </para>
    /// </summary>
    public IExecutionStage<Dictionary<string, object?>> Sql(
        [System.Diagnostics.CodeAnalysis.StringSyntax(System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.CompositeFormat)] string sqlFormat, 
        params ReadOnlySpan<object?> args)
    {
        // ArrayPool을 사용하여 Span -> Array 변환 (최적화)
        object?[] argsArray = ArrayPool<object?>.Shared.Rent(args.Length);
        try
        {
            args.CopyTo(argsArray);
            
            // string.Format을 사용하여 SQL 생성
            // 주의: 이 방식은 파라미터화를 수행하지 않고 텍스트를 포맷팅합니다.
            // C# 10+ / .NET 6+ API 활용
            string sql = string.Format(CultureInfo.InvariantCulture, sqlFormat, argsArray.AsSpan(0, args.Length));
            
            return Sql(sql);
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(argsArray, clearArray: true);
        }
    }

    #endregion

    #region [확장 메서드] 파라미터 설정 단계 (Parameter Stage)

    /// <summary>
    /// 쿼리 또는 SP 실행에 사용할 파라미터 객체를 지정합니다.
    /// </summary>
    /// <typeparam name="TParams">파라미터 객체 타입 (익명 객체, DTO 등)</typeparam>
    /// <param name="parameters">파라미터 데이터가 담긴 객체</param>
    /// <returns>실행 단계(Execution Stage)로 이동</returns>
    public IExecutionStage<TParams> With<TParams>(TParams parameters)
    {
        return new ExecutionStage<TParams>(_executor, _instanceName, _commandText, _commandType, parameters, _timeout, _schemaModeOverride);
    }

    /// <summary>
    /// 명령 실행 제한 시간(Timeout)을 설정합니다.
    /// </summary>
    /// <param name="timeoutSeconds">초 단위 제한 시간</param>
    /// <returns>현재 단계 유지 (IParameterStage)</returns>
    public IParameterStage WithTimeout(int timeoutSeconds)
    {
        _timeout = timeoutSeconds;
        return this;
    }

    #endregion

    #region [확장 메서드] 실행 단계 위임 (Execution Delegate)

    // IExecutionStage<object> 구현 (IParameterStage가 이를 상속받으므로 구현 필요)
    // 참고: IParameterStage 상태에서는 파라미터가 아직 없으므로(null), 빈 객체나 null로 처리하여 실행을 시도합니다.
    
    private IExecutionStage<object> AsExecutionStage() 
        => new ExecutionStage<object>(_executor, _instanceName, _commandText, _commandType, null!, _timeout, _schemaModeOverride);

    /// <inheritdoc/>
    public IAsyncEnumerable<TResult> QueryAsync<TResult>(CancellationToken ct = default) => AsExecutionStage().QueryAsync<TResult>(ct);
    
    /// <inheritdoc/>
    public Task<TResult?> QuerySingleAsync<TResult>(CancellationToken ct = default) => AsExecutionStage().QuerySingleAsync<TResult>(ct);
    
    /// <inheritdoc/>
    public Task<TScalar?> ExecuteScalarAsync<TScalar>(CancellationToken ct = default) => AsExecutionStage().ExecuteScalarAsync<TScalar>(ct);
    
    /// <inheritdoc/>
    public Task<IMultipleResultReader> QueryMultipleAsync(CancellationToken ct = default) => AsExecutionStage().QueryMultipleAsync(ct);
    
    /// <inheritdoc/>
    public Task<int> ExecuteAsync(CancellationToken ct = default) => AsExecutionStage().ExecuteAsync(ct);

    #endregion

    #region [확장 메서드] 대량 작업 및 파이프라인 (Bulk & Pipeline)
    
    /// <summary>
    /// SqlBulkCopy를 사용하여 대량의 데이터를 대상 테이블에 고속으로 삽입합니다.
    /// </summary>
    /// <typeparam name="T">데이터 모델 타입</typeparam>
    /// <param name="destinationTableName">대상 테이블 이름</param>
    /// <param name="data">삽입할 데이터 컬렉션</param>
    /// <param name="ct">취소 토큰</param>
    public Task BulkInsertAsync<T>(string destinationTableName, IEnumerable<T> data, CancellationToken ct = default)
    {
        return _executor.BulkInsertAsync(destinationTableName, data, _instanceName, ct);
    }
    
    /// <summary>
    /// 임시 테이블과 MERGE 문을 활용하여 대량 업데이트(Bulk Update)를 수행합니다.
    /// </summary>
    /// <param name="targetTableName">업데이트 대상 테이블</param>
    /// <param name="data">업데이트할 데이터</param>
    /// <param name="keyColumns">매칭 기준 컬럼 (PK)</param>
    /// <param name="updateColumns">실제 변경할 컬럼 목록</param>
    /// <param name="ct">취소 토큰</param>
    public Task BulkUpdateAsync<T>(string targetTableName, IEnumerable<T> data, string[] keyColumns, string[] updateColumns, CancellationToken ct = default)
    {
        return _executor.BulkUpdateAsync(targetTableName, data, keyColumns, updateColumns, _instanceName, ct);
    }
    
    /// <summary>
    /// 임시 테이블과 JOIN을 활용하여 대량 삭제(Bulk Delete)를 수행합니다.
    /// </summary>
    public Task BulkDeleteAsync<T>(string targetTableName, IEnumerable<T> data, string[] keyColumns, CancellationToken ct = default)
    {
        return _executor.BulkDeleteAsync(targetTableName, data, keyColumns, _instanceName, ct);
    }
    
    /// <summary>
    /// ChannelReader를 소스로 사용하여 대량 삽입 파이프라인(Bulk Insert Pipeline)을 실행합니다.
    /// <para>데이터 생성(Producer)과 DB 삽입(Consumer)을 병렬로 처리하여 메모리 효율을 극대화합니다.</para>
    /// </summary>
    public Task BulkInsertPipelineAsync<T>(string tableName, ChannelReader<T> reader, int batchSize = 5000, CancellationToken ct = default)
    {
        return _executor.BulkInsertPipelineAsync(tableName, reader, _instanceName, batchSize, ct);
    }
    
    /// <summary>
    /// ChannelReader를 소스로 사용하여 대량 업데이트 파이프라인(Bulk Update Pipeline)을 실행합니다.
    /// </summary>
    public Task BulkUpdatePipelineAsync<T>(string tableName, ChannelReader<T> reader, string[] keyColumns, string[] updateColumns, int batchSize = 5000, CancellationToken ct = default)
    {
        return _executor.BulkUpdatePipelineAsync(tableName, reader, keyColumns, updateColumns, _instanceName, batchSize, ct);
    }

    /// <summary>
    /// ChannelReader를 소스로 사용하여 대량 삭제 파이프라인(Bulk Delete Pipeline)을 실행합니다.
    /// </summary>
    public Task BulkDeletePipelineAsync<T>(string tableName, ChannelReader<T> reader, string[] keyColumns, int batchSize = 5000, CancellationToken ct = default)
    {
        return _executor.BulkDeletePipelineAsync(tableName, reader, keyColumns, _instanceName, batchSize, ct);
    }

    #endregion

    #region [확장 메서드] 재개 가능 쿼리 (Resumable Query)

    /// <summary>
    /// 커서 기반 페이지네이션을 사용하여 대용량 데이터를 중단점부터 이어서 조회할 수 있는 '재개 가능 쿼리'를 실행합니다.
    /// </summary>
    /// <typeparam name="TCursor">커서 타입 (예: int, long, DateTime)</typeparam>
    /// <typeparam name="TResult">결과 모델 타입</typeparam>
    /// <param name="queryBuilder">커서 값을 받아 다음 쿼리 SQL을 생성하는 함수</param>
    /// <param name="cursorSelector">조회된 결과 항목에서 커서 값을 추출하는 함수</param>
    /// <param name="initialCursor">첫 실행 시 사용할 초기 커서 값</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>전체 데이터를 순차적으로 스트리밍하는 비동기 열거자</returns>
    public IAsyncEnumerable<TResult> QueryResumableAsync<TCursor, TResult>(
        Func<TCursor, string> queryBuilder, 
        Func<TResult, TCursor> cursorSelector, 
        TCursor initialCursor = default!, 
        CancellationToken ct = default)
    {
        // Fluent API도 이제 IDbExecutor의 영속성(Resumable State) 기능을 완전하게 활용합니다.
        // 이를 통해 커서가 IResumableStateStore에 안전하게 저장됩니다.
        return _executor.QueryResumableAsync(
            queryBuilder,
            cursorSelector,
            _instanceName, // Builder가 보유한 인스턴스 이름 전달
            initialCursor,
            ct);
    }

    #endregion
}

/// <summary>
/// 파라미터가 확정된 상태의 실행 단계(Execution Stage) 구현체입니다.
/// </summary>
/// <typeparam name="TParams">파라미터 타입</typeparam>
internal sealed class ExecutionStage<TParams> : IExecutionStage<TParams>
{
    #region [필드] 내부 필드

    private readonly IDbExecutor _executor;
    private readonly string _instanceName;
    private readonly string _commandText;
    private readonly CommandType _commandType;
    private readonly TParams _parameters;
    private int? _timeout;
    private readonly SchemaResolutionMode? _schemaModeOverride;

    #endregion

    #region [생성자]

    public ExecutionStage(
        IDbExecutor executor, 
        string instanceName, 
        string commandText, 
        CommandType commandType, 
        TParams parameters, 
        int? timeout,
        SchemaResolutionMode? schemaModeOverride)
    {
        _executor = executor;
        _instanceName = instanceName;
        _commandText = commandText;
        _commandType = commandType;
        _parameters = parameters;
        _timeout = timeout;
        _schemaModeOverride = schemaModeOverride;
    }

    #endregion

    #region [헬퍼 메서드]

    /// <summary>
    /// 파라미터를 교체하여 새로운 스테이지를 생성합니다. (불변성 유지)
    /// </summary>
    /// <inheritdoc/>
    public IExecutionStage<TParams> With(TParams parameters)
    {
        // 파라미터를 교체하여 새로운 스테이지 생성 (불변성 유지)
        return new ExecutionStage<TParams>(_executor, _instanceName, _commandText, _commandType, parameters, _timeout, _schemaModeOverride);
    }

    /// <inheritdoc/>
    public IExecutionStage<TParams> Timeout(int seconds)
    {
        _timeout = seconds;
        return this;
    }

    private DbExecutionOptions CreateOptions() => new DbExecutionOptions(_schemaModeOverride, _timeout);

    #endregion

    #region [메서드] 실행 메서드

    /// <inheritdoc/>
    public Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        // ExecuteNonQueryAsync 서명: (commandText, parameters, instanceHash, commandType, options, ct)
        return _executor.ExecuteNonQueryAsync(_commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct);
    }

    /// <inheritdoc/>
    public async Task<List<TResult>> QueryListAsync<TResult>(CancellationToken ct = default)
    {
        // IAsyncEnumerable을 List로 변환하는 헬퍼 메서드
        var list = new List<TResult>();
        await foreach (var item in QueryAsync<TResult>(ct).ConfigureAwait(false))
        {
            list.Add(item);
        }
        return list;
    }
    
    /// <inheritdoc/>
    public IAsyncEnumerable<TResult> QueryAsync<TResult>(CancellationToken ct = default)
    {
         return _executor.QueryAsync<TParams, TResult>(_commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct);
    }

    /// <inheritdoc/>
    public Task<TResult?> QuerySingleAsync<TResult>(CancellationToken ct = default)
    {
        return _executor.QuerySingleAsync<TParams, TResult>(_commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct);
    }

    /// <inheritdoc/>
    public Task<TScalar?> ExecuteScalarAsync<TScalar>(CancellationToken ct = default)
    {
        return _executor.ExecuteScalarAsync<TParams, TScalar>(_commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct);
    }

    /// <inheritdoc/>
    public Task<IMultipleResultReader> QueryMultipleAsync(CancellationToken ct = default)
    {
         return _executor.QueryMultipleAsync<TParams>(_commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct);
    }

    #endregion
}
