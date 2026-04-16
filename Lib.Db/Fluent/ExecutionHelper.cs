// ============================================================================
// 파일: Lib.Db/Fluent/ExecutionHelper.cs
// 설명: ExecutionStage 공통 예외→DbResult 변환 헬퍼
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Core;
using Lib.Db.Diagnostics;

namespace Lib.Db.Fluent;

/// <summary>
/// Fluent API 실행 단계의 공통 예외→DbResult 변환 로직입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>중복 제거</b>: <see cref="ExecutionStage{TParams}"/>의 5개 실행 메서드에서
///   반복되는 SqlException/Exception 처리 블록을 단일 위치로 통합합니다.<br/>
/// - <b>두 가지 래퍼</b>: 동기 반환 경로(<see cref="WrapSync{T}"/>)와
///   비동기 반환 경로(<see cref="WrapAsync{T}"/>)를 각각 제공합니다.<br/>
/// - <b>오버헤드 없음</b>: static 메서드이므로 인스턴스 할당이 없습니다.
/// </para>
/// </summary>
internal static class ExecutionHelper
{
    #region [메서드] 동기 래퍼 (QueryAsync — 스트림 생성 경로)

    /// <summary>
    /// 동기적으로 값을 생성하는 작업을 실행하고 예외를 <see cref="DbResult{T}"/>로 변환합니다.
    /// <para>
    /// <b>[사용 경로]</b>: QueryAsync — IAsyncEnumerable 스트림 참조 반환 시 사용됩니다.
    /// 스트림 자체는 동기 반환이므로 Task.FromResult를 통해 래핑합니다.
    /// </para>
    /// </summary>
    /// <typeparam name="T">DbResult에 담길 값의 타입</typeparam>
    /// <param name="commandText">실행 중인 SQL/SP 이름 (오류 메시지에 포함)</param>
    /// <param name="operation">실행할 동기 함수</param>
    /// <returns>성공 또는 실패를 나타내는 DbResult&lt;T&gt;를 담은 Task</returns>
    internal static Task<DbResult<T>> WrapSync<T>(string commandText, Func<T> operation)
    {
        try
        {
            T value = operation();
            return Task.FromResult(DbResult<T>.Ok(value));
        }
        catch (OperationCanceledException)
        {
            throw; // 취소 예외는 재throw — CancellationToken 계약 준수
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = DbErrorMapper.FromSqlException(ex, commandText);
            return Task.FromResult(DbResult<T>.Fail(error));
        }
        catch (Exception ex)
        {
            DbError error = BuildGeneralError(ex, commandText);
            return Task.FromResult(DbResult<T>.Fail(error));
        }
    }

    #endregion

    #region [메서드] 비동기 래퍼 (QuerySingleAsync / ExecuteScalarAsync / QueryMultipleAsync / ExecuteAsync)

    /// <summary>
    /// 비동기 작업을 실행하고 예외를 <see cref="DbResult{T}"/>로 변환합니다.
    /// <para>
    /// <b>[사용 경로]</b>: QuerySingleAsync, ExecuteScalarAsync, QueryMultipleAsync, ExecuteAsync
    /// </para>
    /// </summary>
    /// <typeparam name="T">DbResult에 담길 값의 타입</typeparam>
    /// <param name="commandText">실행 중인 SQL/SP 이름 (오류 메시지에 포함)</param>
    /// <param name="operation">실행할 비동기 함수</param>
    /// <returns>성공 또는 실패를 나타내는 DbResult&lt;T&gt;</returns>
    internal static async Task<DbResult<T>> WrapAsync<T>(string commandText, Func<Task<DbResult<T>>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // 취소 예외는 재throw — CancellationToken 계약 준수
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            DbError error = DbErrorMapper.FromSqlException(ex, commandText);
            return DbResult<T>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = BuildGeneralError(ex, commandText);
            return DbResult<T>.Fail(error);
        }
    }

    #endregion

    #region [비공개 헬퍼]

    /// <summary>
    /// 분류되지 않은 일반 예외를 <see cref="DbError"/>로 변환합니다.
    /// </summary>
    /// <param name="ex">변환할 예외</param>
    /// <param name="commandText">오류가 발생한 SQL/SP 이름 (ObjectName에 기록)</param>
    /// <returns>DbErrorKind.Unknown 종류의 DbError</returns>
    private static DbError BuildGeneralError(Exception ex, string commandText)
    {
        return new DbError
        {
            Kind = DbErrorKind.Unknown,
            Message = ex.Message,
            ObjectName = commandText,
            InnerException = ex
        };
    }

    #endregion
}
