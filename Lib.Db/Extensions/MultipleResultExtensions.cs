// ============================================================================
// 파일: Lib.Db/Extensions/MultipleResultExtensions.cs
// 설명: IMultipleResultReader 기반 typed QueryMultiple 편의 확장
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Execution;

namespace Lib.Db.Extensions;

/// <summary>
/// 두 개의 순차 결과 셋을 타입별 리스트로 담는 값입니다.
/// </summary>
/// <typeparam name="T1">첫 번째 결과 셋 행 타입입니다.</typeparam>
/// <typeparam name="T2">두 번째 결과 셋 행 타입입니다.</typeparam>
/// <param name="First">첫 번째 결과 셋입니다.</param>
/// <param name="Second">두 번째 결과 셋입니다.</param>
public readonly record struct DbMultiple<T1, T2>(
    List<T1> First,
    List<T2> Second);

/// <summary>
/// 세 개의 순차 결과 셋을 타입별 리스트로 담는 값입니다.
/// </summary>
/// <typeparam name="T1">첫 번째 결과 셋 행 타입입니다.</typeparam>
/// <typeparam name="T2">두 번째 결과 셋 행 타입입니다.</typeparam>
/// <typeparam name="T3">세 번째 결과 셋 행 타입입니다.</typeparam>
/// <param name="First">첫 번째 결과 셋입니다.</param>
/// <param name="Second">두 번째 결과 셋입니다.</param>
/// <param name="Third">세 번째 결과 셋입니다.</param>
public readonly record struct DbMultiple<T1, T2, T3>(
    List<T1> First,
    List<T2> Second,
    List<T3> Third);

/// <summary>
/// 네 개의 순차 결과 셋을 타입별 리스트로 담는 값입니다.
/// </summary>
/// <typeparam name="T1">첫 번째 결과 셋 행 타입입니다.</typeparam>
/// <typeparam name="T2">두 번째 결과 셋 행 타입입니다.</typeparam>
/// <typeparam name="T3">세 번째 결과 셋 행 타입입니다.</typeparam>
/// <typeparam name="T4">네 번째 결과 셋 행 타입입니다.</typeparam>
/// <param name="First">첫 번째 결과 셋입니다.</param>
/// <param name="Second">두 번째 결과 셋입니다.</param>
/// <param name="Third">세 번째 결과 셋입니다.</param>
/// <param name="Fourth">네 번째 결과 셋입니다.</param>
public readonly record struct DbMultiple<T1, T2, T3, T4>(
    List<T1> First,
    List<T2> Second,
    List<T3> Third,
    List<T4> Fourth);

/// <summary>
/// <see cref="IMultipleResultReader"/>를 typed result container로 읽는 확장 메서드입니다.
/// </summary>
public static class MultipleResultExtensions
{
    private const string ReadFailureMessage = "Reading multiple result sets failed.";

    /// <summary>
    /// 두 개의 결과 셋을 순서대로 읽고 reader를 해제합니다.
    /// </summary>
    /// <typeparam name="T1">첫 번째 결과 셋 행 타입입니다.</typeparam>
    /// <typeparam name="T2">두 번째 결과 셋 행 타입입니다.</typeparam>
    /// <param name="readerTask">다중 결과 셋 reader를 반환하는 작업입니다.</param>
    /// <param name="ct">작업 취소 토큰입니다.</param>
    /// <returns>typed 결과 셋 컨테이너 또는 실패 정보를 반환합니다.</returns>
    public static async Task<DbResult<DbMultiple<T1, T2>>> ReadMultipleAsync<T1, T2>(
        this Task<DbResult<IMultipleResultReader>> readerTask,
        CancellationToken ct = default)
    {
        try
        {
            DbResult<IMultipleResultReader> result = await readerTask.ConfigureAwait(false);
            if (!result.IsSuccess)
                return ToReadFailure<DbMultiple<T1, T2>>(result.Error);

            await using IMultipleResultReader reader = result.Value!;
            List<T1> first = await reader.ReadAsync<T1>(ct).ConfigureAwait(false);
            List<T2> second = await reader.ReadAsync<T2>(ct).ConfigureAwait(false);

            return DbResult<DbMultiple<T1, T2>>.Ok(new DbMultiple<T1, T2>(first, second));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ToReadFailure<DbMultiple<T1, T2>>();
        }
    }

    /// <summary>
    /// 세 개의 결과 셋을 순서대로 읽고 reader를 해제합니다.
    /// </summary>
    /// <typeparam name="T1">첫 번째 결과 셋 행 타입입니다.</typeparam>
    /// <typeparam name="T2">두 번째 결과 셋 행 타입입니다.</typeparam>
    /// <typeparam name="T3">세 번째 결과 셋 행 타입입니다.</typeparam>
    /// <param name="readerTask">다중 결과 셋 reader를 반환하는 작업입니다.</param>
    /// <param name="ct">작업 취소 토큰입니다.</param>
    /// <returns>typed 결과 셋 컨테이너 또는 실패 정보를 반환합니다.</returns>
    public static async Task<DbResult<DbMultiple<T1, T2, T3>>> ReadMultipleAsync<T1, T2, T3>(
        this Task<DbResult<IMultipleResultReader>> readerTask,
        CancellationToken ct = default)
    {
        try
        {
            DbResult<IMultipleResultReader> result = await readerTask.ConfigureAwait(false);
            if (!result.IsSuccess)
                return ToReadFailure<DbMultiple<T1, T2, T3>>(result.Error);

            await using IMultipleResultReader reader = result.Value!;
            List<T1> first = await reader.ReadAsync<T1>(ct).ConfigureAwait(false);
            List<T2> second = await reader.ReadAsync<T2>(ct).ConfigureAwait(false);
            List<T3> third = await reader.ReadAsync<T3>(ct).ConfigureAwait(false);

            return DbResult<DbMultiple<T1, T2, T3>>.Ok(new DbMultiple<T1, T2, T3>(first, second, third));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ToReadFailure<DbMultiple<T1, T2, T3>>();
        }
    }

    /// <summary>
    /// 네 개의 결과 셋을 순서대로 읽고 reader를 해제합니다.
    /// </summary>
    /// <typeparam name="T1">첫 번째 결과 셋 행 타입입니다.</typeparam>
    /// <typeparam name="T2">두 번째 결과 셋 행 타입입니다.</typeparam>
    /// <typeparam name="T3">세 번째 결과 셋 행 타입입니다.</typeparam>
    /// <typeparam name="T4">네 번째 결과 셋 행 타입입니다.</typeparam>
    /// <param name="readerTask">다중 결과 셋 reader를 반환하는 작업입니다.</param>
    /// <param name="ct">작업 취소 토큰입니다.</param>
    /// <returns>typed 결과 셋 컨테이너 또는 실패 정보를 반환합니다.</returns>
    public static async Task<DbResult<DbMultiple<T1, T2, T3, T4>>> ReadMultipleAsync<T1, T2, T3, T4>(
        this Task<DbResult<IMultipleResultReader>> readerTask,
        CancellationToken ct = default)
    {
        try
        {
            DbResult<IMultipleResultReader> result = await readerTask.ConfigureAwait(false);
            if (!result.IsSuccess)
                return ToReadFailure<DbMultiple<T1, T2, T3, T4>>(result.Error);

            await using IMultipleResultReader reader = result.Value!;
            List<T1> first = await reader.ReadAsync<T1>(ct).ConfigureAwait(false);
            List<T2> second = await reader.ReadAsync<T2>(ct).ConfigureAwait(false);
            List<T3> third = await reader.ReadAsync<T3>(ct).ConfigureAwait(false);
            List<T4> fourth = await reader.ReadAsync<T4>(ct).ConfigureAwait(false);

            return DbResult<DbMultiple<T1, T2, T3, T4>>.Ok(new DbMultiple<T1, T2, T3, T4>(first, second, third, fourth));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ToReadFailure<DbMultiple<T1, T2, T3, T4>>();
        }
    }

    private static DbResult<T> ToReadFailure<T>()
        => DbResult<T>.Fail(new DbError
        {
            Kind = DbErrorKind.Unknown,
            Message = ReadFailureMessage
        });

    private static DbResult<T> ToReadFailure<T>(DbError? source)
        => DbResult<T>.Fail(new DbError
        {
            Kind = source?.Kind ?? DbErrorKind.Unknown,
            SqlErrorCode = source?.SqlErrorCode ?? 0,
            Severity = source?.Severity ?? 0,
            IsTransient = source?.IsTransient ?? false,
            Message = ReadFailureMessage
        });
}
