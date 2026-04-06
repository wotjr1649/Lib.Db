// ============================================================================
// 파일: Lib.Db/Fluent/DbRequestBuilder.cs
// 설명: Fluent API 빌더 구현체 (Stateful Builder)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Globalization;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using Lib.Db.Diagnostics;

namespace Lib.Db.Fluent;

/// <summary>
/// DB 명령을 생성하고 실행하는 Fluent API 빌더입니다. (상태 보유)
/// <para>
/// <b>[사용 예시]</b><br/>
/// builder.Procedure("usp_GetUsers").With(new { Id = 1 }).QueryAsync&lt;User&gt;();
/// </para>
/// </summary>
internal sealed class DbRequestBuilder : IProcedureStage, IParameterStage
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
    /// FormattableString(보간된 문자열)을 사용하여 SQL을 지정합니다.
    /// <para>보간 인수는 자동으로 파라미터화되어 SQL Injection을 방지합니다.</para>
    /// </summary>
    /// <param name="sql">보간된 SQL 문자열</param>
    /// <returns>파라미터 설정 단계로 이동</returns>
    public IParameterStage Sql(FormattableString sql)
    {
        // FormattableString에서 파라미터를 추출하여 Dictionary로 변환
        Dictionary<string, object?> parameters = new(sql.ArgumentCount);
        object?[] args = sql.GetArguments();
        string format = sql.Format;

        // 포맷 인수를 @p0, @p1, ... 파라미터로 변환
        object[] paramNames = new object[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            string paramName = $"@p{i}";
            parameters[paramName] = args[i];
            paramNames[i] = paramName;
        }

        _commandText = string.Format(CultureInfo.InvariantCulture, format, paramNames);
        _commandType = CommandType.Text;

        // IParameterStage를 반환하되, 이미 파라미터가 확정된 ExecutionStage를 내부에서 사용
        // With(parameters)를 통해 ExecutionStage로 전환
        return new FormattableStringParameterStage(
            _executor, _instanceName, _commandText, _commandType, parameters, _timeout, _schemaModeOverride);
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
    public IExecutionStage<Dictionary<string, object?>> Sql(
        ref SqlInterpolatedStringHandler handler)
    {
        (string sql, Dictionary<string, object?> parameters) = handler.GetResult();

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
    // 파라미터 없이 직접 실행 시 빈 객체로 처리

    private IExecutionStage<object> AsExecutionStage()
        => new ExecutionStage<object>(_executor, _instanceName, _commandText, _commandType, null!, _timeout, _schemaModeOverride);

    /// <inheritdoc/>
    public Task<DbResult<IAsyncEnumerable<TResult>>> QueryAsync<TResult>(CancellationToken ct = default)
        => AsExecutionStage().QueryAsync<TResult>(ct);

    /// <inheritdoc/>
    public Task<DbResult<TResult?>> QuerySingleAsync<TResult>(CancellationToken ct = default)
        => AsExecutionStage().QuerySingleAsync<TResult>(ct);

    /// <inheritdoc/>
    public Task<DbResult<TScalar?>> ExecuteScalarAsync<TScalar>(CancellationToken ct = default)
        => AsExecutionStage().ExecuteScalarAsync<TScalar>(ct);

    /// <inheritdoc/>
    public Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(CancellationToken ct = default)
        => AsExecutionStage().QueryMultipleAsync(ct);

    /// <inheritdoc/>
    public Task<DbResult<int>> ExecuteAsync(CancellationToken ct = default)
        => AsExecutionStage().ExecuteAsync(ct);

    #endregion

}

#region FormattableStringParameterStage

/// <summary>
/// FormattableString Sql() 호출 시 이미 파라미터가 확정된 상태를 나타내는 스테이지입니다.
/// <para>IParameterStage를 구현하여 추가 With() 호출도 허용합니다.</para>
/// </summary>
internal sealed class FormattableStringParameterStage : IParameterStage
{
    private readonly IDbExecutor _executor;
    private readonly string _instanceName;
    private readonly string _commandText;
    private readonly CommandType _commandType;
    private readonly Dictionary<string, object?> _parameters;
    private int? _timeout;
    private readonly SchemaResolutionMode? _schemaModeOverride;

    public FormattableStringParameterStage(
        IDbExecutor executor,
        string instanceName,
        string commandText,
        CommandType commandType,
        Dictionary<string, object?> parameters,
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

    private ExecutionStage<Dictionary<string, object?>> AsExecutionStage()
        => new(_executor, _instanceName, _commandText, _commandType, _parameters, _timeout, _schemaModeOverride);

    /// <inheritdoc/>
    public IExecutionStage<TParams> With<TParams>(TParams parameters)
        => new ExecutionStage<TParams>(_executor, _instanceName, _commandText, _commandType, parameters, _timeout, _schemaModeOverride);

    /// <inheritdoc/>
    public IParameterStage WithTimeout(int timeoutSeconds)
    {
        _timeout = timeoutSeconds;
        return this;
    }

    /// <inheritdoc/>
    public Task<DbResult<IAsyncEnumerable<TResult>>> QueryAsync<TResult>(CancellationToken ct = default)
        => AsExecutionStage().QueryAsync<TResult>(ct);

    /// <inheritdoc/>
    public Task<DbResult<TResult?>> QuerySingleAsync<TResult>(CancellationToken ct = default)
        => AsExecutionStage().QuerySingleAsync<TResult>(ct);

    /// <inheritdoc/>
    public Task<DbResult<TScalar?>> ExecuteScalarAsync<TScalar>(CancellationToken ct = default)
        => AsExecutionStage().ExecuteScalarAsync<TScalar>(ct);

    /// <inheritdoc/>
    public Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(CancellationToken ct = default)
        => AsExecutionStage().QueryMultipleAsync(ct);

    /// <inheritdoc/>
    public Task<DbResult<int>> ExecuteAsync(CancellationToken ct = default)
        => AsExecutionStage().ExecuteAsync(ct);
}

#endregion

#region ExecutionStage

/// <summary>
/// 파라미터가 확정된 상태의 실행 단계(Execution Stage) 구현체입니다.
/// <para>모든 실행 메서드는 DbResult&lt;T&gt;로 래핑하여 성공/실패를 명시적으로 전달합니다.</para>
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

    private DbExecutionOptions CreateOptions() => new DbExecutionOptions(_schemaModeOverride, _timeout);

    /// <summary>
    /// SqlException을 DbError로 변환합니다.
    /// </summary>
    private static DbError MapSqlException(Microsoft.Data.SqlClient.SqlException ex, string commandText)
    {
        return DbErrorMapper.FromSqlException(ex, commandText);
    }

    /// <summary>
    /// 일반 예외를 DbError로 변환합니다.
    /// </summary>
    private static DbError MapGeneralException(Exception ex)
    {
        return new DbError
        {
            Kind = DbErrorKind.Unknown,
            Message = ex.Message,
            InnerException = ex
        };
    }

    #endregion

    #region [메서드] 실행 메서드 — DbResult 래핑

    /// <summary>
    /// 결과를 비동기 스트림으로 조회합니다. 스트림 생성 시점의 오류만 DbResult로 래핑합니다.
    /// </summary>
    public Task<DbResult<IAsyncEnumerable<TResult>>> QueryAsync<TResult>(CancellationToken ct = default)
    {
        try
        {
            IAsyncEnumerable<TResult> stream = _executor.QueryAsync<TParams, TResult>(
                _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct);
            return Task.FromResult(DbResult<IAsyncEnumerable<TResult>>.Ok(stream));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = MapSqlException(ex, _commandText);
            return Task.FromResult(DbResult<IAsyncEnumerable<TResult>>.Fail(error));
        }
        catch (Exception ex)
        {
            DbError error = MapGeneralException(ex);
            return Task.FromResult(DbResult<IAsyncEnumerable<TResult>>.Fail(error));
        }
    }

    /// <summary>
    /// 단일 결과를 조회합니다.
    /// </summary>
    public async Task<DbResult<TResult?>> QuerySingleAsync<TResult>(CancellationToken ct = default)
    {
        try
        {
            TResult? value = await _executor.QuerySingleAsync<TParams, TResult>(
                _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct).ConfigureAwait(false);
            return DbResult<TResult?>.Ok(value);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = MapSqlException(ex, _commandText);
            return DbResult<TResult?>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = MapGeneralException(ex);
            return DbResult<TResult?>.Fail(error);
        }
    }

    /// <summary>
    /// 단일 스칼라 값(1행 1열)을 조회합니다.
    /// </summary>
    public async Task<DbResult<TScalar?>> ExecuteScalarAsync<TScalar>(CancellationToken ct = default)
    {
        try
        {
            TScalar? value = await _executor.ExecuteScalarAsync<TParams, TScalar>(
                _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct).ConfigureAwait(false);
            return DbResult<TScalar?>.Ok(value);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = MapSqlException(ex, _commandText);
            return DbResult<TScalar?>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = MapGeneralException(ex);
            return DbResult<TScalar?>.Fail(error);
        }
    }

    /// <summary>
    /// 다중 결과 셋(GridReader)을 조회합니다.
    /// </summary>
    public async Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(CancellationToken ct = default)
    {
        try
        {
            IMultipleResultReader reader = await _executor.QueryMultipleAsync<TParams>(
                _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct).ConfigureAwait(false);
            return DbResult<IMultipleResultReader>.Ok(reader);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = MapSqlException(ex, _commandText);
            return DbResult<IMultipleResultReader>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = MapGeneralException(ex);
            return DbResult<IMultipleResultReader>.Fail(error);
        }
    }

    /// <summary>
    /// 결과 조회 없이 명령을 실행하고 영향 받은 행 수를 반환합니다.
    /// </summary>
    public async Task<DbResult<int>> ExecuteAsync(CancellationToken ct = default)
    {
        try
        {
            int rows = await _executor.ExecuteNonQueryAsync(
                _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct).ConfigureAwait(false);
            return DbResult<int>.Ok(rows, rows);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = MapSqlException(ex, _commandText);
            return DbResult<int>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = MapGeneralException(ex);
            return DbResult<int>.Fail(error);
        }
    }

    #endregion
}

#endregion
