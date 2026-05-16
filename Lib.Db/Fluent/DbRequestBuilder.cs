// ============================================================================
// 파일: Lib.Db/Fluent/DbRequestBuilder.cs
// 설명: Fluent API 빌더 구현체 (Stateful Builder)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Collections;
using System.Globalization;
using System.Reflection;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;

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
    /// <param name="sqlText">SQL 쿼리 문장. 사용자 입력은 문자열 결합하지 말고 파라미터로 전달해야 합니다.</param>
    /// <returns>파라미터 설정 단계로 이동</returns>
    public IParameterStage Sql(string sqlText)
    {
        _commandText = sqlText;
        _commandType = CommandType.Text;
        return this;
    }

    /// <summary>
    /// FormattableString(보간된 문자열)을 사용하여 SQL을 지정합니다.
    /// <para>보간 값 인수는 자동으로 파라미터화되어 값 기반 SQL injection 위험을 줄입니다.</para>
    /// </summary>
    /// <param name="sql">보간된 SQL 문자열</param>
    /// <returns>보간 인수로 생성된 파라미터를 유지하는 단계로 이동합니다. 추가 <c>With(...)</c> 호출은 충돌 없는 명명 파라미터를 병합합니다.</returns>
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

        // IParameterStage를 반환하되, 보간식에서 추출한 파라미터를 보존한다.
        // FormattableStringParameterStage는 추가 With(...) 호출 시 충돌 없는 명명 파라미터만 병합한다.
        return new FormattableStringParameterStage(
            _executor, _instanceName, _commandText, _commandType, parameters, _timeout, _schemaModeOverride);
    }

    /// <summary>
    /// 명시적인 보간 SQL API입니다. <see cref="Sql(FormattableString)"/>와 동일하게 보간 값 인수를 파라미터화합니다.
    /// </summary>
    public IParameterStage SqlInterpolated(FormattableString sql)
    {
        return Sql(sql);
    }

    /// <summary>
    /// SqlInterpolatedStringHandler를 사용한 SQL 생성 및 보간 값 인수 파라미터화
    /// <para>
    /// <b>[Zero-Allocation 전략]</b><br/>
    /// - ArrayPool 기반 버퍼 관리<br/>
    /// - Span&lt;char&gt; 기반 문자열 조합<br/>
    /// - 자동 파라미터 수집 (@p0, @p1, ...)
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
/// <para>추가 With() 호출은 보간식에서 추출한 파라미터와 충돌하지 않는 명명 파라미터만 병합합니다.</para>
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
    {
        Dictionary<string, object?> mergedParameters = MergeParameters(_parameters, parameters);
        return new FormattableStringMergedExecutionStage<TParams>(
            _executor,
            _instanceName,
            _commandText,
            _commandType,
            mergedParameters,
            _timeout,
            _schemaModeOverride);
    }

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

    private static Dictionary<string, object?> MergeParameters<TParams>(
        Dictionary<string, object?> generatedParameters,
        TParams additionalParameters)
    {
        Dictionary<string, object?> merged = new(generatedParameters, StringComparer.OrdinalIgnoreCase);

        if (additionalParameters is null)
            return merged;

        foreach (KeyValuePair<string, object?> parameter in EnumerateNamedParameters(additionalParameters))
            AddParameter(merged, parameter.Key, parameter.Value);

        return merged;
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateNamedParameters(object parameters)
    {
        if (parameters is IEnumerable<KeyValuePair<string, object?>> typedPairs)
        {
            foreach (KeyValuePair<string, object?> pair in typedPairs)
                yield return pair;

            yield break;
        }

        if (parameters is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                    throw new InvalidOperationException("FormattableString 기반 Sql() 뒤의 With()는 문자열 이름을 가진 파라미터만 병합할 수 있습니다.");

                yield return new KeyValuePair<string, object?>(key, entry.Value);
            }

            yield break;
        }

        Type parameterType = parameters.GetType();
        if (IsScalarParameterObject(parameterType))
            throw new InvalidOperationException("FormattableString 기반 Sql() 뒤의 With()는 이름 있는 파라미터 객체 또는 Dictionary만 사용할 수 있습니다.");

        bool hasReadableProperty = false;
        foreach (PropertyInfo property in parameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            hasReadableProperty = true;
            yield return new KeyValuePair<string, object?>(property.Name, property.GetValue(parameters));
        }

        if (!hasReadableProperty)
            throw new InvalidOperationException("FormattableString 기반 Sql() 뒤의 With()는 이름 있는 파라미터 객체 또는 Dictionary만 사용할 수 있습니다.");
    }

    private static void AddParameter(Dictionary<string, object?> parameters, string? name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("파라미터 이름은 비어 있을 수 없습니다.");

        string candidateName = name.Trim();
        string normalizedName = NormalizeParameterName(candidateName);
        foreach (string existingName in parameters.Keys)
        {
            if (NormalizeParameterName(existingName).Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"FormattableString 기반 Sql()의 자동 파라미터와 With() 파라미터 이름이 충돌합니다: '{candidateName}'.");
            }
        }

        parameters[candidateName] = value;
    }

    private static string NormalizeParameterName(string name)
        => name.Trim().TrimStart('@');

    private static bool IsScalarParameterObject(Type type)
        => type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid)
            || type == typeof(TimeSpan);
}

internal sealed class FormattableStringMergedExecutionStage<TParams> : IExecutionStage<TParams>
{
    private readonly ExecutionStage<Dictionary<string, object?>> _inner;

    public FormattableStringMergedExecutionStage(
        IDbExecutor executor,
        string instanceName,
        string commandText,
        CommandType commandType,
        Dictionary<string, object?> parameters,
        int? timeout,
        SchemaResolutionMode? schemaModeOverride)
    {
        _inner = new ExecutionStage<Dictionary<string, object?>>(
            executor,
            instanceName,
            commandText,
            commandType,
            parameters,
            timeout,
            schemaModeOverride);
    }

    /// <inheritdoc/>
    public Task<DbResult<IAsyncEnumerable<TResult>>> QueryAsync<TResult>(CancellationToken ct = default)
        => _inner.QueryAsync<TResult>(ct);

    /// <inheritdoc/>
    public Task<DbResult<TResult?>> QuerySingleAsync<TResult>(CancellationToken ct = default)
        => _inner.QuerySingleAsync<TResult>(ct);

    /// <inheritdoc/>
    public Task<DbResult<TScalar?>> ExecuteScalarAsync<TScalar>(CancellationToken ct = default)
        => _inner.ExecuteScalarAsync<TScalar>(ct);

    /// <inheritdoc/>
    public Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(CancellationToken ct = default)
        => _inner.QueryMultipleAsync(ct);

    /// <inheritdoc/>
    public Task<DbResult<int>> ExecuteAsync(CancellationToken ct = default)
        => _inner.ExecuteAsync(ct);
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

    #endregion

    #region [메서드] 실행 메서드 — DbResult 래핑

    /// <summary>
    /// 결과를 비동기 스트림으로 조회합니다. 스트림 생성 시점의 오류만 DbResult로 래핑합니다.
    /// </summary>
    public Task<DbResult<IAsyncEnumerable<TResult>>> QueryAsync<TResult>(CancellationToken ct = default)
        => ExecutionHelper.WrapSync(
            _commandText,
            () => _executor.QueryAsync<TParams, TResult>(
                _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct));

    /// <summary>
    /// 단일 결과를 조회합니다.
    /// </summary>
    public Task<DbResult<TResult?>> QuerySingleAsync<TResult>(CancellationToken ct = default)
        => ExecutionHelper.WrapAsync(
            _commandText,
            async () =>
            {
                TResult? value = await _executor.QuerySingleAsync<TParams, TResult>(
                    _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct).ConfigureAwait(false);
                return DbResult<TResult?>.Ok(value);
            });

    /// <summary>
    /// 단일 스칼라 값(1행 1열)을 조회합니다.
    /// </summary>
    public Task<DbResult<TScalar?>> ExecuteScalarAsync<TScalar>(CancellationToken ct = default)
        => ExecutionHelper.WrapAsync(
            _commandText,
            async () =>
            {
                TScalar? value = await _executor.ExecuteScalarAsync<TParams, TScalar>(
                    _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct).ConfigureAwait(false);
                return DbResult<TScalar?>.Ok(value);
            });

    /// <summary>
    /// 다중 결과 셋(GridReader)을 조회합니다.
    /// </summary>
    public Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(CancellationToken ct = default)
        => ExecutionHelper.WrapAsync(
            _commandText,
            async () =>
            {
                IMultipleResultReader reader = await _executor.QueryMultipleAsync<TParams>(
                    _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct).ConfigureAwait(false);
                return DbResult<IMultipleResultReader>.Ok(reader);
            });

    /// <summary>
    /// 결과 조회 없이 명령을 실행하고 영향 받은 행 수를 반환합니다.
    /// </summary>
    public Task<DbResult<int>> ExecuteAsync(CancellationToken ct = default)
        => ExecutionHelper.WrapAsync(
            _commandText,
            async () =>
            {
                int rows = await _executor.ExecuteNonQueryAsync(
                    _commandText, _parameters, _instanceName, _commandType, CreateOptions(), ct).ConfigureAwait(false);
                return DbResult<int>.Ok(rows, rows);
            });

    #endregion
}

#endregion
