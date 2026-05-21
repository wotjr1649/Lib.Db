// ============================================================================
// 파일: Lib.Db/Extensions/QueryCacheExtensions.cs
// 설명: DB 쿼리 결과 캐싱을 위한 확장 메서드
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Lib.Db.Contracts.Core;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;

namespace Lib.Db.Extensions;

#region 쿼리 캐시 확장 메서드

/// <summary>
/// DB 쿼리 결과를 캐시하는 확장 메서드 모음입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>비침범적</b>: 기존 Fluent API를 변경하지 않고, Task&lt;DbResult&lt;T&gt;&gt; 결과에 체이닝합니다.<br/>
/// - <b>IDistributedCache 호환</b>: Microsoft.Extensions.Caching.Distributed 인터페이스를 사용하여
///   MemoryCache, Redis, SQL Server Cache 등 모든 구현체와 호환됩니다.<br/>
/// - <b>HybridCache 호환</b>: 프로젝트에 이미 등록된 HybridCache를 직접 활용할 수도 있습니다.<br/>
/// - <b>DbResult 패턴 유지</b>: 캐시 히트/미스 모두 DbResult로 반환하여 일관된 에러 처리를 보장합니다.
/// </para>
/// <para>
/// <b>[사용 예시]</b><br/>
/// <code>
/// DbResult&lt;UserDto?&gt; result = await _db
///     .Procedure("sp_GetUser")
///     .With(new { UserId = 1 })
///     .QuerySingleAsync&lt;UserDto&gt;()
///     .WithCacheAsync(cache, "user:1", TimeSpan.FromMinutes(5));
/// </code>
/// </para>
/// </summary>
public static class QueryCacheExtensions
{
    private const string JsonCacheRequiresUnreferencedCodeMessage =
        "JSON cache convenience overloads use JsonSerializerOptions-based serialization. Use source-generated JsonTypeInfo overloads for Native AOT.";

    private const string JsonCacheRequiresDynamicCodeMessage =
        "JSON cache convenience overloads can require runtime code generation. Use source-generated JsonTypeInfo overloads for Native AOT.";

    #region IDistributedCache 기반 캐싱

    /// <summary>
    /// DB 쿼리 결과를 IDistributedCache에 캐시합니다.
    /// <para>
    /// 1. 캐시에서 키를 조회합니다.<br/>
    /// 2. 캐시 히트 → 역직렬화하여 DbResult.Ok로 반환합니다.<br/>
    /// 3. 캐시 미스 → 원본 쿼리를 실행하고, 성공 시 캐시에 저장합니다.
    /// </para>
    /// </summary>
    /// <typeparam name="T">결과 타입</typeparam>
    /// <param name="resultTask">원본 DB 쿼리 Task</param>
    /// <param name="cache">IDistributedCache 인스턴스</param>
    /// <param name="cacheKey">캐시 키</param>
    /// <param name="duration">캐시 유효 시간</param>
    /// <param name="jsonOptions">JSON 직렬화 옵션 (null 시 기본값 사용)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>캐시된 또는 새로 조회된 결과</returns>
    /// <remarks>
    /// <b>[주의]</b> 이 메서드는 Task가 이미 시작된 후 호출되므로, 캐시 히트 시에도 DB 쿼리가 실행될 수 있습니다.
    /// 캐시 히트 시 DB 호출을 완전히 건너뛰려면 <see cref="GetOrQueryAsync{T}"/> 팩토리 패턴을 사용하세요.
    /// </remarks>
    [RequiresUnreferencedCode(JsonCacheRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(JsonCacheRequiresDynamicCodeMessage)]
    public static async Task<DbResult<T?>> WithCacheAsync<T>(
        this Task<DbResult<T?>> resultTask,
        IDistributedCache cache,
        string cacheKey,
        TimeSpan duration,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken ct = default)
    {
        // 1. 캐시 히트 확인
        byte[]? cached = await cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached is { Length: > 0 })
        {
            T? value = JsonSerializer.Deserialize<T>(cached, jsonOptions);
            return DbResult<T?>.Ok(value);
        }

        // 2. 캐시 미스 → 원본 실행
        DbResult<T?> result = await resultTask.ConfigureAwait(false);

        // 3. 성공 시 캐시에 저장
        if (result.IsSuccess && result.Value is not null)
        {
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(result.Value, jsonOptions);
            DistributedCacheEntryOptions entryOptions = new()
            {
                AbsoluteExpirationRelativeToNow = duration
            };
            await cache.SetAsync(cacheKey, serialized, entryOptions, ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// 비동기 스트림(IAsyncEnumerable) 쿼리 결과를 IDistributedCache에 캐시합니다.
    /// <para>
    /// 캐시 히트 시 List로 역직렬화한 결과를 IAsyncEnumerable로 래핑하여 반환합니다.
    /// 캐시 미스 시 원본 스트림을 소비하여 List로 구체화한 후 캐시에 저장합니다.
    /// </para>
    /// </summary>
    /// <typeparam name="T">결과 행 타입</typeparam>
    /// <param name="resultTask">원본 DB 쿼리 Task</param>
    /// <param name="cache">IDistributedCache 인스턴스</param>
    /// <param name="cacheKey">캐시 키</param>
    /// <param name="duration">캐시 유효 시간</param>
    /// <param name="jsonOptions">JSON 직렬화 옵션</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>캐시된 또는 새로 조회된 결과 리스트</returns>
    [RequiresUnreferencedCode(JsonCacheRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(JsonCacheRequiresDynamicCodeMessage)]
    public static async Task<DbResult<List<T>>> WithCacheListAsync<T>(
        this Task<DbResult<IAsyncEnumerable<T>>> resultTask,
        IDistributedCache cache,
        string cacheKey,
        TimeSpan duration,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken ct = default)
    {
        // 1. 캐시 히트 확인
        byte[]? cached = await cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached is { Length: > 0 })
        {
            List<T>? list = JsonSerializer.Deserialize<List<T>>(cached, jsonOptions);
            return DbResult<List<T>>.Ok(list ?? []);
        }

        // 2. 캐시 미스 → 원본 실행
        DbResult<IAsyncEnumerable<T>> streamResult = await resultTask.ConfigureAwait(false);

        if (!streamResult.IsSuccess)
        {
            return DbResult<List<T>>.Fail(streamResult.Error!.Value);
        }

        // 3. 스트림을 List로 구체화
        List<T> items = [];
        if (streamResult.Value is not null)
        {
            await foreach (T item in streamResult.Value.WithCancellation(ct).ConfigureAwait(false))
            {
                items.Add(item);
            }
        }

        // 4. 성공 시 캐시에 저장
        if (items.Count > 0)
        {
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(items, jsonOptions);
            DistributedCacheEntryOptions entryOptions = new()
            {
                AbsoluteExpirationRelativeToNow = duration
            };
            await cache.SetAsync(cacheKey, serialized, entryOptions, ct).ConfigureAwait(false);
        }

        return DbResult<List<T>>.Ok(items);
    }

    /// <summary>
    /// 캐시 미스일 때만 DB 쿼리를 실행하는 팩토리 패턴 캐시.
    /// <para>기존 WithCacheAsync와 달리 캐시 히트 시 DB 호출을 완전히 건너뜁니다.</para>
    /// </summary>
    /// <typeparam name="T">결과 타입</typeparam>
    /// <param name="cache">IDistributedCache 인스턴스</param>
    /// <param name="cacheKey">캐시 키</param>
    /// <param name="duration">캐시 유효 시간</param>
    /// <param name="queryFactory">캐시 미스 시에만 호출되는 DB 쿼리 팩토리</param>
    /// <param name="jsonOptions">JSON 직렬화 옵션 (null 시 기본값 사용)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>캐시된 또는 새로 조회된 결과</returns>
    [RequiresUnreferencedCode(JsonCacheRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(JsonCacheRequiresDynamicCodeMessage)]
    public static async Task<DbResult<T?>> GetOrQueryAsync<T>(
        IDistributedCache cache,
        string cacheKey,
        TimeSpan duration,
        Func<Task<DbResult<T?>>> queryFactory,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken ct = default)
    {
        // 1. 캐시 히트 확인
        byte[]? cached = await cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached is { Length: > 0 })
        {
            T? value = JsonSerializer.Deserialize<T>(cached, jsonOptions);
            return DbResult<T?>.Ok(value);
        }

        // 2. 캐시 미스 → 팩토리를 통해 DB 쿼리 실행 (이 시점에서만 Task 생성)
        DbResult<T?> result = await queryFactory().ConfigureAwait(false);

        // 3. 성공 시 캐시에 저장
        if (result.IsSuccess && result.Value is not null)
        {
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(result.Value, jsonOptions);
            DistributedCacheEntryOptions entryOptions = new()
            {
                AbsoluteExpirationRelativeToNow = duration
            };
            await cache.SetAsync(cacheKey, serialized, entryOptions, ct).ConfigureAwait(false);
        }

        return result;
    }

    #endregion

    #region HybridCache 기반 캐싱

    /// <summary>
    /// DB 쿼리 결과를 HybridCache에 캐시합니다.
    /// <para>
    /// <b>[설계 의도]</b><br/>
    /// 프로젝트에 이미 등록된 HybridCache를 활용하여 L1(메모리)/L2(분산) 계층 캐시를 자동으로 적용합니다.
    /// </para>
    /// </summary>
    /// <typeparam name="T">결과 타입</typeparam>
    /// <param name="resultTask">원본 DB 쿼리 Task</param>
    /// <param name="hybridCache">HybridCache 인스턴스</param>
    /// <param name="cacheKey">캐시 키</param>
    /// <param name="duration">캐시 유효 시간</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>캐시된 또는 새로 조회된 결과</returns>
    public static async Task<DbResult<T?>> WithHybridCacheAsync<T>(
        this Task<DbResult<T?>> resultTask,
        HybridCache hybridCache,
        string cacheKey,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        HybridCacheEntryOptions entryOptions = new()
        {
            Expiration = duration,
            LocalCacheExpiration = duration
        };

        // HybridCache.GetOrCreateAsync: 캐시 미스 시 팩토리 실행
        // 단, 팩토리에서 예외 발생 시 캐시되지 않음
        T? cachedValue = await hybridCache.GetOrCreateAsync(
            cacheKey,
            async (token) =>
            {
                DbResult<T?> result = await resultTask.ConfigureAwait(false);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.Error?.Message ?? "DB 쿼리 실패");

                return result.Value;
            },
            entryOptions,
            cancellationToken: ct).ConfigureAwait(false);

        return DbResult<T?>.Ok(cachedValue);
    }

    #endregion

    #region 캐시 무효화

    /// <summary>
    /// 지정된 캐시 키를 무효화(삭제)합니다.
    /// </summary>
    /// <param name="cache">IDistributedCache 인스턴스</param>
    /// <param name="cacheKey">삭제할 캐시 키</param>
    /// <param name="ct">취소 토큰</param>
    public static Task InvalidateCacheAsync(
        this IDistributedCache cache,
        string cacheKey,
        CancellationToken ct = default)
    {
        return cache.RemoveAsync(cacheKey, ct);
    }

    #endregion
}

#endregion
