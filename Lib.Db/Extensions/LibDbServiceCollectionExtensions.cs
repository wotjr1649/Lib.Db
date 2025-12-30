// ============================================================================
// 파일: Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs
// 설명: Lib.Db DI 통합 확장 메서드
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using System.IO;
using System.Linq;
using Lib.Db.Caching;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Schema;
using Lib.Db.Core;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Executors;
using Lib.Db.Hosting;
using Lib.Db.Repository;
using Lib.Db.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Lib.Db 라이브러리를 DI 컨테이너에 통합하기 위한 확장 메서드입니다.
/// <para>
/// <b>[사용 방법]</b><br/>
/// - 기본: <see cref="AddHighPerformanceDb"/> (모든 서비스 일괄 등록)<br/>
/// - 모듈화: <see cref="RegisterLibDbCoreServices"/> (서비스만 등록)<br/>
/// - Options: <see cref="LibDbOptionsExtensions.AddLibDbOptions"/> (옵션만 설정)
/// </para>
/// </summary>
public static class LibDbServiceCollectionExtensions
{
    #region [확장 메서드] 서비스 등록 - 통합

    /// <summary>
    /// 고성능 DB 라이브러리의 모든 서비스를 한 번에 등록합니다. (기본 사용자용)
    /// <para>
    /// 내부적으로 다음을 순차 호출합니다:<br/>
    /// - <see cref="LibDbOptionsExtensions.AddLibDbOptions"/><br/>
    /// - <see cref="RegisterLibDbCoreServices"/><br/>
    /// - <see cref="AddLibDbResilience"/><br/>
    /// - <see cref="AddLibDbHostedServices"/>
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configure">LibDbOptions 설정 델리게이트</param>
    /// <returns>체이닝을 위한 IServiceCollection</returns>
    /// <summary>
    /// [권장] IConfiguration을 통해 Lib.Db 필수 서비스를 일괄 등록합니다.
    /// <para>
    /// appsettings.json의 "LibDb" 섹션 바인딩을 자동으로 처리합니다.
    /// </para>
    /// </summary>
    public static IServiceCollection AddLibDb(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddHighPerformanceDb(options =>
        {
            // [AOT Safe Strategy]
            // 직접 LibDbOptions를 바인딩하면 JsonSerializerOptions 등 런타임 객체로 인해 AOT 경고가 발생합니다.
            // 따라서 순수 데이터 객체인 Shadow DTO(LibDbConfig)로 바인딩 후 값을 매핑합니다.
            var config = new Lib.Db.Configuration.Internal.LibDbConfig();
            configuration.Bind(config);
            config.ApplyTo(options);
        });
    }

    /// <summary>
    /// Lib.Db 필수 서비스를 일괄 등록합니다.
    /// </summary>
    public static IServiceCollection AddHighPerformanceDb(
        this IServiceCollection services,
        Action<LibDbOptions> configure)
    {
        // UTF-8 인코딩 설정
        TrySetConsoleEncodingToUtf8();

        // 1. Options 설정
        services.AddLibDbOptions(configure);

        // 2. 핵심 서비스 등록
        services.RegisterLibDbCoreServices();

        // 3. Resilience 파이프라인
        services.AddLibDbResilience();

        // 4. Hosted Services (Warmup)
        services.AddLibDbHostedServices();

        return services;
    }

    #endregion

    #region [확장 메서드] 서비스 등록 - 모듈별

    /// <summary>
    /// Lib.Db 핵심 서비스만 등록합니다. (테스트/고급 사용자용)
    /// <para>
    /// <b>[주의]</b> Options는 별도로 <see cref="LibDbOptionsExtensions.AddLibDbOptions"/>로 설정해야 합니다.
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>체이닝을 위한 IServiceCollection</returns>
    public static IServiceCollection RegisterLibDbCoreServices(
        this IServiceCollection services)
    {
        // ConnectionFactory
        services.TryAddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        // Schema 계층
        services.TryAddSingleton<ISchemaRepository, SqlSchemaRepository>();
        services.TryAddSingleton<ISchemaService, SchemaService>();
        services.TryAddSingleton<ITvpSchemaValidator, TvpSchemaValidator>();

        // Session (Scoped)
        services.TryAddScoped<DbSession>();
        services.TryAddScoped<IDbSession>(sp => sp.GetRequiredService<DbSession>());
        services.TryAddScoped<IDbContext>(sp => sp.GetRequiredService<DbSession>());

        // Mapper
        services.TryAddSingleton<IMapperFactory, MapperFactory>();

        // DbExecutor 의존성 (내부 헬퍼 사용)
        ServiceRegistrationHelpers.RegisterExecutor(services);

        // HybridCache AOT Serializers
        ServiceRegistrationHelpers.RegisterAotSerializers(services);

        // v9 FINAL+: 조건부 공유 메모리 캐시 (크로스 플랫폼 지원)
        ServiceRegistrationHelpers.RegisterConditionalSharedMemoryCache(services);

        // v9: Epoch-based Schema Flush Coordination
        services.AddSchemaFlushCoordination();

        return services;
    }

    /// <summary>
    /// Polly Resilience 파이프라인을 등록합니다.
    /// <para>
    /// CircuitBreaker + Retry + Timeout 조합으로 DB 연결 안정성을 확보합니다.
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>체이닝을 위한 IServiceCollection</returns>
    public static IServiceCollection AddLibDbResilience(
        this IServiceCollection services)
    {
        ServiceRegistrationHelpers.RegisterResiliencePipeline(services);
        return services;
    }

    /// <summary>
    /// Hosted Services (Schema Warmup)를 등록합니다.
    /// <para>
    /// <b>[조건]</b> Options.EnableSchemaCaching == true<br/>
    /// <b>[조건]</b> Options.PrewarmSchemas.Count > 0
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>체이닝을 위한 IServiceCollection</returns>
    public static IServiceCollection AddLibDbHostedServices(
        this IServiceCollection services)
    {
        // ServiceProvider를 임시로 빌드하여 Options 확인
        using var sp = services.BuildServiceProvider();
        var options = sp.GetService<LibDbOptions>();

        if (options?.EnableSchemaCaching == true && options.PrewarmSchemas.Count > 0)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, SchemaWarmupService>());
        }

        return services;
    }

    /// <summary>
    /// v9 FINAL+: Epoch 기반 분산 스키마 캐시 조정 서비스를 조건부로 등록합니다.
    /// <para>
    /// <b>[플랫폼 자동 감지]</b><br/>
    /// <see cref="LibDbOptions.EnableEpochCoordination"/>가 <c>null</c>이면:<br/>
    /// - <see cref="LibDbOptions.EnableSharedMemoryCache"/>와 동일하게 설정<br/>
    /// - Windows 공유 메모리 ON이면 Epoch도 ON, 그외 OFF
    /// </para>
    /// <para>
    /// <b>[등록 서비스]</b><br/>
    /// - <see cref="EpochStore"/> (Singleton)<br/>
    /// - <see cref="ISchemaFlushCoordinator"/> → <see cref="SchemaFlushService"/><br/>
    /// - <see cref="EpochWatcherService"/> (조건부: WatchedInstances 설정 시)
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컨렉션</param>
    /// <param name="epochBasePath">Epoch 파일 저장 경로 (기본값: %TEMP%/Lib.Db.Epochs)</param>
    /// <returns>체이닝을 위한 IServiceCollection</returns>
    /// <remarks>
    /// <b>⚠️ 경고 조건:</b><br/>
    /// <see cref="LibDbOptions.EnableSharedMemoryCache"/> = <c>false</c>인데<br/>
    /// <see cref="LibDbOptions.EnableEpochCoordination"/> = <c>true</c>인 경우:<br/>
    /// Epoch 파일 기반 동기화는 되지만 실제 캐시는 프로세스마다 독립적이므로 비효율적.
    /// </remarks>
    public static IServiceCollection AddSchemaFlushCoordination(
        this IServiceCollection services,
        string? epochBasePath = null)
    {
        // ServiceProvider 임시 빌드로 옵션 확인
        using var sp = services.BuildServiceProvider();
        var options = sp.GetService<LibDbOptions>();
        
        // 플랫폼 자동 감지
        bool enableSharedMemory = options?.EnableSharedMemoryCache
            ?? System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
        
        bool enableEpoch = options?.EnableEpochCoordination ?? enableSharedMemory;
        
        // Epoch 비활성화 시 조기 리턴
        if (!enableEpoch)
        {
            var logger = sp.GetService<ILogger<SchemaFlushService>>();
            logger?.LogInformation(
                "[Epoch] 비활성화됨 - EpochStore 및 SchemaFlushService 등록 건너뛀 (명시적 설정: {ExplicitSetting})",
                options?.EnableEpochCoordination?.ToString() ?? "null (auto-detect)");
            return services;
        }
        
        // 경고: 공유 메모리 없이 Epoch만 사용
        if (!enableSharedMemory && enableEpoch)
        {
            var logger = sp.GetService<ILogger<EpochStore>>();
            logger?.LogWarning(
                "[Epoch] 경고: 공유 메모리 비활성화 상태에서 Epoch 사용 - " +
                "프로세스 간 스키마 동기화 불가. " +
                "권장: EnableSharedMemoryCache=true 또는 EnableEpochCoordination=false");
        }
        
        // 1. EpochStore 등록 (Singleton)
        services.TryAddSingleton(sp =>
        {
            var basePath = epochBasePath ?? Path.Combine(
                Path.GetTempPath(), "Lib.Db.Epochs");
            var logger = sp.GetRequiredService<ILogger<EpochStore>>();
            return new EpochStore(basePath, logger);
        });

        // 2. SchemaFlushService 등록
        services.TryAddSingleton<ISchemaFlushCoordinator, SchemaFlushService>();

        // 3. EpochWatcherService 조건부 등록
        // (WatchedInstances가 설정된 경우만)
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, EpochWatcherService>());

        return services;
    }

    #endregion

    #region [헬퍼] 내부 유틸리티

    /// <summary>
    /// 콘솔 출력 인코딩을 UTF-8로 설정합니다.
    /// </summary>
    private static void TrySetConsoleEncodingToUtf8()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // 일부 제한된 환경에서는 설정 불가 (무시)
        }
    }

    #endregion
}
