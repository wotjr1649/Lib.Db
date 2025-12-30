// ============================================================================
// 파일: Lib.Db/Extensions/HybridCacheExtensions.cs
// 설명: .NET 9+ HybridCache 통합 확장 메서드
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Lib.Db.Extensions;

#region [확장 메서드] HybridCache 서비스 등록

/// <summary>
/// .NET 9+ HybridCache 통합을 위한 확장 메서드입니다.
/// </summary>
/// <remarks>
/// <para><strong>📋 설계 의도</strong></para>
/// <list type="bullet">
/// <item><description><strong>L1+L2 계층화</strong>: In-Process 메모리(L1)와 Out-of-Process 분산 캐시(L2)를 결합하여 성능을 극대화합니다.</description></item>
/// <item><description><strong>Stampede 방지</strong>: 내부적인 Locking 메커니즘을 통해 동일 키에 대한 중복 연산을 방지합니다.</description></item>
/// </list>
/// </remarks>
public static class HybridCacheExtensions
{
    /// <summary>
    /// Lib.Db에 HybridCache를 통합하고 기본 설정을 구성합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configure">HybridCache 추가 설정 액션 (선택)</param>
    /// <returns>서비스 컬렉션 (체이닝용)</returns>
    public static IServiceCollection AddLibDbHybridCache(
        this IServiceCollection services,
        Action<HybridCacheOptions>? configure = null)
    {
        // .NET 9+ HybridCache 등록
        // 참고: 이미 IDistributedCache가 등록되어 있어야 L2로 작동합니다.
        // Lib.Db.Caching.SharedMemoryCache가 그 역할을 수행할 수 있습니다.
        
        services.AddHybridCache(options =>
        {
            // 기본값: 5분 만료
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1) // L1은 짧게
            };

            configure?.Invoke(options);
        });

        return services;
    }
}

#endregion
