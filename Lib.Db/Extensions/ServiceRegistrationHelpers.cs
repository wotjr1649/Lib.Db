// ============================================================================
// 파일: Lib.Db/Extensions/ServiceRegistrationHelpers.cs
// 설명: 서비스 등록을 위한 내부 헬퍼 클래스
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Runtime.InteropServices;
using Lib.Db.Caching;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.Execution;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Executors;
using Lib.Db.Hosting;
using Lib.Db.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 서비스 등록을 위한 내부 헬퍼 클래스입니다.
/// <para>
/// <b>[내부 전용]</b> 복잡한 등록 로직을 분리하여 가독성 향상
/// </para>
/// </summary>
internal static class ServiceRegistrationHelpers
{
    #region [헬퍼] DbExecutor 등록

    /// <summary>
    /// DbExecutor 및 모든 의존성을 등록합니다.
    /// </summary>
    internal static void RegisterExecutor(IServiceCollection services)
    {
        // Interceptor Chain
        services.TryAddSingleton<InterceptorChain>(sp =>
        {
            IEnumerable<IDbCommandInterceptor> interceptors = sp.GetServices<IDbCommandInterceptor>();
            return new InterceptorChain(interceptors);
        });

        // Execution Strategy (Resilient)
        services.TryAddSingleton<IDbExecutionStrategy>(sp =>
        {
            IDbConnectionFactory connFactory = sp.GetRequiredService<IDbConnectionFactory>();
            IResiliencePipelineProvider pipelineProvider = sp.GetRequiredService<IResiliencePipelineProvider>();
            ISchemaService schemaService = sp.GetRequiredService<ISchemaService>();
            ILogger<ResilientStrategy> logger = sp.GetRequiredService<ILogger<ResilientStrategy>>();

            return new ResilientStrategy(
                connFactory,
                pipelineProvider,
                schemaService,
                logger);
        });

        // Executor
        services.TryAddSingleton<IDbExecutor, SqlDbExecutor>();

        // Executor Factory (Fluent API용)
        services.TryAddSingleton<IDbExecutorFactory, DbExecutorFactory>();
    }

    #endregion

    #region [헬퍼] AOT Serializers 등록

    /// <summary>
    /// HybridCache용 AOT Serializers를 등록합니다.
    /// <para>
    /// <b>[AOT 호환]</b> Reflection 없이 JSON 직렬화
    /// </para>
    /// </summary>
    internal static void RegisterAotSerializers(IServiceCollection services)
    {
        if (RuntimeFeatureSwitch.IsRuntimeDynamicCodeSupported &&
            RuntimeFeatureSwitch.DynamicCodeSupportedOverride is not false)
        {
            IHybridCacheBuilder builder = services.AddHybridCache();

            builder
                .AddSerializer<SpSchema>(new AotHybridCacheSerializer<SpSchema>(
                    LibDbJsonContext.Default.SpSchema))
                .AddSerializer<TvpSchema>(new AotHybridCacheSerializer<TvpSchema>(
                    LibDbJsonContext.Default.TvpSchema));
            return;
        }

        services.TryAddSingleton<HybridCache, LibDbAotHybridCache>();
        services.TryAddSingleton<IHybridCacheSerializer<SpSchema>>(
            _ => new AotHybridCacheSerializer<SpSchema>(
                LibDbJsonContext.Default.SpSchema));
        services.TryAddSingleton<IHybridCacheSerializer<TvpSchema>>(
            _ => new AotHybridCacheSerializer<TvpSchema>(
                LibDbJsonContext.Default.TvpSchema));
    }

    #endregion

    #region [헬퍼] Resilience Pipeline 등록

    /// <summary>
    /// Polly Resilience 파이프라인을 등록합니다.
    /// <para>
    /// <b>[구성]</b> CircuitBreaker + Retry + Timeout
    /// </para>
    /// </summary>
    /// <summary>
    /// Polly Resilience Pipeline Provider를 등록합니다.
    /// <para>
    /// <b>[조건부 등록]</b> Options.EnableResilience에 따라 RealProvider 또는 NoOpProvider를 등록하여
    /// OFF 상태일 때 Zero-Overhead를 보장합니다.
    /// </para>
    /// </summary>
    internal static void RegisterResiliencePipeline(IServiceCollection services)
    {
        // 1. Transient Error Detector
        services.TryAddSingleton<ITransientSqlErrorDetector, Lib.Db.Infrastructure.Resilience.DefaultTransientSqlErrorDetector>();

        // 2. Resilience Pipeline Provider (Conditional)
        services.TryAddSingleton<IResiliencePipelineProvider>(sp =>
        {
            LibDbOptions options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;

            if (options.EnableResilience)
            {
                return ActivatorUtilities.CreateInstance<Lib.Db.Infrastructure.Resilience.DefaultResiliencePipelineProvider>(sp);
            }
            else
            {
                return new Lib.Db.Infrastructure.Resilience.NoOpResiliencePipelineProvider();
            }
        });
    }

    #endregion

    #region [헬퍼] 공유 메모리 캐시 및 프로세스 슬롯 등록

    /// <summary>
    /// 옵션 및 플랫폼 기반으로 IDistributedCache와 IProcessSlotAllocator를 등록합니다.
    /// </summary>
    /// <remarks>
    /// <para><b>[v3 개선사항]</b></para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <b>BuildServiceProvider() 제거</b>: DI 안티패턴 제거, Factory Pattern 적용
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>자동 격리 (Isolation)</b>: Connection String 기반 IsolationKey 자동 생성
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>프로세스 슬롯 할당</b>: IProcessSlotAllocator 자동 등록 (Leader Election 지원)
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Passive Mode</b>: 비활성화 시 PassiveProcessSlotAllocator 반환 (Null Object Pattern)
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>[플랫폼 자동 감지]</b></para>
    /// <para>
    /// <see cref="LibDbOptions.EnableSharedMemoryCache"/>가 <c>null</c>이면:
    /// <br/>- Windows: <c>true</c> (공유 메모리 사용)
    /// <br/>- Linux/macOS/기타: <c>false</c> (프로세스 내 메모리 캐시 사용)
    /// </para>
    /// </remarks>
    internal static void RegisterConditionalSharedMemoryCache(IServiceCollection services)
    {
        // ====================================================================
        // 0. IIsolationKeyGenerator 등록 (DI)
        // ====================================================================
        services.TryAddSingleton<Lib.Db.Contracts.Cache.IIsolationKeyGenerator, Lib.Db.Caching.IsolationKeyGenerator>();

        // ====================================================================
        // 1. IProcessSlotAllocator 등록
        // ====================================================================
        services.TryAddSingleton<IProcessSlotAllocator>(sp =>
        {
            LibDbOptions options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
            ILogger<ProcessSlotAllocator> logger = sp.GetRequiredService<ILogger<Lib.Db.Hosting.ProcessSlotAllocator>>();
            IIsolationKeyGenerator keyGenerator = sp.GetRequiredService<Lib.Db.Contracts.Cache.IIsolationKeyGenerator>();

            // 플랫폼별 기본값 결정
            bool enableSharedMemory = options.EnableSharedMemoryCache
                ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            if (!enableSharedMemory)
            {
                logger.LogInformation("[ProcessSlot] 공유 메모리 비활성화 - Passive Mode");
                return new Lib.Db.Hosting.PassiveProcessSlotAllocator();
            }

            // IsolationKey 생성 (DI를 통한 서비스 사용)
            string connectionString = GetPrimaryConnectionStringOrThrow(options, "ProcessSlotAllocator");

            string isolationKey = keyGenerator.Generate(connectionString) ?? "Shared";

            return new Lib.Db.Hosting.ProcessSlotAllocator(isolationKey, logger);
        });

        // ====================================================================
        // 2. IDistributedCache 등록
        // ====================================================================
        services.TryAddSingleton<IDistributedCache>(sp =>
        {
            LibDbOptions options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
            ILogger<SharedMemoryCache> logger = sp.GetRequiredService<ILogger<SharedMemoryCache>>();
            IIsolationKeyGenerator keyGenerator = sp.GetRequiredService<Lib.Db.Contracts.Cache.IIsolationKeyGenerator>();

            bool enableSharedMemory = options.EnableSharedMemoryCache
                ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            if (!enableSharedMemory)
            {
                logger.LogInformation(
                    "[공유메모리캐시] 비활성화 - MemoryDistributedCache 사용 (플랫폼: {OS}, 명시적 설정: {ExplicitSetting})",
                    RuntimeInformation.OSDescription,
                    options.EnableSharedMemoryCache?.ToString() ?? "null (auto-detect)");

                return new MemoryDistributedCache(
                    Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
            }

            // 공유 메모리 활성화 - IsolationKey 생성 (DI 사용)
            string connectionString = GetPrimaryConnectionStringOrThrow(options, "SharedMemoryCache");

            string? isolationKey = keyGenerator.Generate(connectionString);
            string basePath = Path.Combine(Path.GetTempPath(), "LibDbCache");

            logger.LogInformation(
                "[공유메모리캐시] 활성화 - SharedMemoryCache 사용 (플랫폼: {OS}, 격리키: {Key})",
                RuntimeInformation.OSDescription,
                isolationKey ?? "None");

            SharedMemoryCacheOptions cacheOptions = new SharedMemoryCacheOptions
            {
                BasePath = basePath,
                IsolationKey = isolationKey ?? "Shared",
                EnableObservability = options.EnableObservability
            };

            return new SharedMemoryCache(Microsoft.Extensions.Options.Options.Create(cacheOptions), logger);
        });

        // ====================================================================
        // 3. CacheMaintenanceService 등록 (Hosted Service)
        // ====================================================================
        services.AddHostedService<CacheMaintenanceService>();
    }

    #endregion

    #region [헬퍼] 내부 유틸리티 메서드

    /// <summary>
    /// <see cref="LibDbOptions.ConnectionStringNames"/>의 첫 번째 키에 해당하는 연결 문자열을 반환합니다.
    /// <para>
    /// 명시된 기본 인스턴스 키가 <see cref="LibDbOptions.ConnectionStrings"/>에 없으면 다른 연결 문자열로
    /// 폴백하지 않고 실패합니다. 이는 multi-instance 환경에서 잘못된 DB 인스턴스의 격리 키가 사용되는 것을
    /// 방지하기 위한 fail-closed 정책입니다.
    /// </para>
    /// </summary>
    /// <param name="options">LibDbOptions 인스턴스</param>
    /// <param name="context">컨텍스트 정보 (예외 메시지용)</param>
    /// <returns>명시된 기본 인스턴스의 연결 문자열</returns>
    /// <exception cref="InvalidOperationException">기본 인스턴스 이름 또는 연결 문자열이 유효하지 않을 때</exception>
    private static string GetPrimaryConnectionStringOrThrow(LibDbOptions options, string context)
    {
        if (options.ConnectionStringNames is not { Count: > 0 })
            throw new InvalidOperationException($"{context}: LibDbOptions.ConnectionStringNames에 기본 인스턴스 이름이 등록되지 않았습니다.");

        string targetName = options.ConnectionStringNames[0];
        if (string.IsNullOrWhiteSpace(targetName))
            throw new InvalidOperationException($"{context}: LibDbOptions.ConnectionStringNames[0]이 비어있습니다.");

        if (options.ConnectionStrings == null || options.ConnectionStrings.Count == 0)
            throw new InvalidOperationException($"{context}: LibDbOptions.ConnectionStrings에 연결 문자열이 등록되지 않았습니다.");

        if (!options.ConnectionStrings.TryGetValue(targetName, out string? connectionString)
            || string.IsNullOrWhiteSpace(connectionString))
        {
            string registeredKeys = string.Join(", ", options.ConnectionStrings.Keys);
            throw new InvalidOperationException(
                $"{context}: 기본 인스턴스 '{targetName}'이(가) ConnectionStrings에 없거나 비어있습니다. 등록된 키: [{registeredKeys}]");
        }

        return connectionString;
    }

    #endregion
}
