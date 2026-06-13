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
    /// <param name="commandText">실행 중인 SQL/SP 원문입니다. public 오류에는 노출하지 않습니다.</param>
    /// <param name="commandType">실행 명령 종류입니다.</param>
    /// <param name="operation">실행할 동기 함수</param>
    /// <returns>성공 또는 실패를 나타내는 DbResult&lt;T&gt;를 담은 Task</returns>
    internal static Task<DbResult<T>> WrapSync<T>(string commandText, CommandType commandType, Func<T> operation)
    {
        _ = commandText;
        string publicObjectName = GetPublicObjectName(commandType);

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
            DbError error = DbErrorMapper.FromSqlException(ex, publicObjectName);
            return Task.FromResult(DbResult<T>.Fail(error));
        }
        catch (Exception ex)
        {
            DbError error = BuildGeneralError(ex, publicObjectName);
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
    /// <param name="commandText">실행 중인 SQL/SP 원문입니다. public 오류에는 노출하지 않습니다.</param>
    /// <param name="commandType">실행 명령 종류입니다.</param>
    /// <param name="operation">실행할 비동기 함수</param>
    /// <returns>성공 또는 실패를 나타내는 DbResult&lt;T&gt;</returns>
    internal static async Task<DbResult<T>> WrapAsync<T>(
        string commandText,
        CommandType commandType,
        Func<Task<DbResult<T>>> operation)
    {
        _ = commandText;
        string publicObjectName = GetPublicObjectName(commandType);

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
            DbError error = DbErrorMapper.FromSqlException(ex, publicObjectName);
            return DbResult<T>.Fail(error);
        }
        catch (Exception ex)
        {
            DbError error = BuildGeneralError(ex, publicObjectName);
            return DbResult<T>.Fail(error);
        }
    }

    #endregion

    #region [비공개 헬퍼]

    /// <summary>
    /// 분류되지 않은 일반 예외를 <see cref="DbError"/>로 변환합니다.
    /// </summary>
    /// <param name="ex">변환할 예외입니다. public 오류에는 보관하지 않습니다.</param>
    /// <param name="objectName">공개해도 되는 명령 종류 라벨입니다.</param>
    /// <returns>DbErrorKind.Unknown 종류의 DbError</returns>
    private static DbError BuildGeneralError(Exception ex, string objectName)
    {
        _ = ex;

        return new DbError
        {
            Kind = DbErrorKind.Unknown,
            Message = "명령 실행 중 오류가 발생했습니다.",
            ObjectName = objectName,
            InnerException = null
        };
    }

    private static string GetPublicObjectName(CommandType commandType)
        => commandType == CommandType.StoredProcedure
            ? "stored procedure"
            : "SQL command";

    #endregion
}
