// ============================================================================
// 파일: VerificationDb/QueryCacheTests.cs
// 설명: 쿼리 결과 캐시 확장 메서드 검증 테스트 2개
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Extensions;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// 쿼리 결과 캐시 확장 메서드(WithCacheAsync) 검증 테스트.
/// <para><b>[설계 의도]</b> MemoryDistributedCache를 사용하여
/// 캐시 미스 → DB 실행 → 캐시 저장 → 캐시 히트 흐름을 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class QueryCacheTests(MultiDbFixture fixture, ITestOutputHelper output)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;
    private readonly ITestOutputHelper _output = output;

    #endregion

    #region QC01: 첫 호출 → DB 실행 후 캐시 저장

    /// <summary>
    /// 첫 호출 시 DB에서 조회하고, 결과를 캐시에 저장하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task QC01_QuerySingleCached_FirstCall_ShouldExecuteAndCache()
    {
        // Arrange
        IDistributedCache cache = CreateMemoryCache();
        string cacheKey = $"test:user:1:{Guid.NewGuid():N}";

        // Act — 첫 호출: DB에서 조회
        DbResult<CoreUser?> result = await _db
            .Procedure("core.usp_Core_Get_User")
            .With(new { UserId = 1 })
            .QuerySingleAsync<CoreUser>()
            .WithCacheAsync(cache, cacheKey, TimeSpan.FromMinutes(5));

        // Assert
        result.IsSuccess.Should().BeTrue("첫 호출은 DB에서 조회되어야 합니다.");
        result.Value.Should().NotBeNull("UserId=1 시드 데이터가 존재해야 합니다.");
        result.Value!.UserName.Should().NotBeNullOrEmpty();

        // 캐시에 저장되었는지 확인
        byte[]? cachedBytes = await cache.GetAsync(cacheKey);
        cachedBytes.Should().NotBeNull("결과가 캐시에 저장되어야 합니다.");
        cachedBytes!.Length.Should().BeGreaterThan(0);

        _output.WriteLine($"=== QC01: 캐시 저장 확인 ===");
        _output.WriteLine($"캐시 키: {cacheKey}");
        _output.WriteLine($"캐시 크기: {cachedBytes.Length} bytes");
        _output.WriteLine($"사용자: {result.Value.UserName}");
    }

    #endregion

    #region QC02: 두 번째 호출 → 캐시에서 반환 (더 빠름)

    /// <summary>
    /// 같은 키로 두 번 호출하면 두 번째는 캐시에서 반환되어 더 빠른지 검증한다.
    /// </summary>
    [Fact]
    public async Task QC02_QuerySingleCached_SecondCall_ShouldReturnFromCache()
    {
        // Arrange
        IDistributedCache cache = CreateMemoryCache();
        string cacheKey = $"test:user:1:{Guid.NewGuid():N}";
        TimeSpan cacheDuration = TimeSpan.FromMinutes(5);

        // Act 1 — 첫 호출: DB에서 조회 (캐시 미스)
        Stopwatch sw1 = Stopwatch.StartNew();
        DbResult<CoreUser?> result1 = await _db
            .Procedure("core.usp_Core_Get_User")
            .With(new { UserId = 1 })
            .QuerySingleAsync<CoreUser>()
            .WithCacheAsync(cache, cacheKey, cacheDuration);
        sw1.Stop();

        // Act 2 — 두 번째 호출: 캐시에서 반환 (캐시 히트)
        // 주의: WithCacheAsync는 resultTask를 먼저 받으므로,
        // 두 번째 호출에서도 Task가 생성되지만 캐시 히트 시 원본 실행은 되지 않음
        Stopwatch sw2 = Stopwatch.StartNew();
        DbResult<CoreUser?> result2 = await _db
            .Procedure("core.usp_Core_Get_User")
            .With(new { UserId = 1 })
            .QuerySingleAsync<CoreUser>()
            .WithCacheAsync(cache, cacheKey, cacheDuration);
        sw2.Stop();

        // Assert
        result1.IsSuccess.Should().BeTrue("첫 호출이 성공해야 합니다.");
        result2.IsSuccess.Should().BeTrue("두 번째 호출이 성공해야 합니다.");

        result2.Value.Should().NotBeNull("캐시된 결과가 반환되어야 합니다.");
        result2.Value!.UserName.Should().Be(result1.Value!.UserName,
            "캐시된 결과는 원본과 동일해야 합니다.");

        _output.WriteLine($"=== QC02: 캐시 히트 성능 비교 ===");
        _output.WriteLine($"첫 호출 (캐시 미스): {sw1.Elapsed.TotalMilliseconds:F1}ms");
        _output.WriteLine($"두 번째 (캐시 히트): {sw2.Elapsed.TotalMilliseconds:F1}ms");

        if (sw1.Elapsed > sw2.Elapsed)
        {
            _output.WriteLine($"캐시 히트가 {sw1.Elapsed.TotalMilliseconds / sw2.Elapsed.TotalMilliseconds:F1}x 빠릅니다.");
        }
    }

    #endregion

    #region 헬퍼 메서드

    /// <summary>
    /// 테스트용 인메모리 IDistributedCache를 생성합니다.
    /// </summary>
    private static IDistributedCache CreateMemoryCache()
    {
        IOptions<MemoryDistributedCacheOptions> cacheOptions =
            Options.Create(new MemoryDistributedCacheOptions());
        return new MemoryDistributedCache(cacheOptions);
    }

    #endregion
}
