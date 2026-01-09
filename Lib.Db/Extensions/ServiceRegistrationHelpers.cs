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
using Lib.Db.Execution.Executors;
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
        // Memory Pressure Monitor
        services.TryAddSingleton<IMemoryPressureMonitor, SystemMemoryMonitor>();

        // Chaos Injector
        services.TryAddSingleton<IChaosInjector, ConfigurableChaosInjector>();

        // Resumable State Store (No-Op 구현)
        services.TryAddSingleton<IResumableStateStore, NoOpResumableStateStore>();

        // Interceptor Chain
        services.TryAddSingleton<InterceptorChain>(sp =>
        {
            var interceptors = sp.GetServices<IDbCommandInterceptor>();
            return new InterceptorChain(interceptors);
        });

        // Execution Strategy (Resilient)
        services.TryAddSingleton<IDbExecutionStrategy>(sp =>
        {
            var connFactory = sp.GetRequiredService<IDbConnectionFactory>();
            var pipelineProvider = sp.GetRequiredService<IResiliencePipelineProvider>();
            var schemaService = sp.GetRequiredService<ISchemaService>();
            var logger = sp.GetRequiredService<ILogger<ResilientStrategy>>();

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
        services.AddHybridCache();

        // SpSchema Serializer
        services.TryAddSingleton<IHybridCacheSerializer<SpSchema>>(
            _ => new AotHybridCacheSerializer<SpSchema>(
                LibDbJsonContext.Default.SpSchema));

        // TvpSchema Serializer
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
            var options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
            
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
            var options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<Lib.Db.Hosting.ProcessSlotAllocator>>();
            var keyGenerator = sp.GetRequiredService<Lib.Db.Contracts.Cache.IIsolationKeyGenerator>();

            // 플랫폼별 기본값 결정
            bool enableSharedMemory = options.EnableSharedMemoryCache
                ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            if (!enableSharedMemory)
            {
                logger.LogInformation("[ProcessSlot] 공유 메모리 비활성화 - Passive Mode");
                return new Lib.Db.Hosting.PassiveProcessSlotAllocator();
            }

            // IsolationKey 생성 (DI를 통한 서비스 사용)
            var targetName = options.ConnectionStringName ?? "Default";
            var connectionString = options.ConnectionStrings?.TryGetValue(targetName, out var cs) == true
                ? cs
                : GetFirstConnectionStringOrThrow(options, "ProcessSlotAllocator");

            string isolationKey = keyGenerator.Generate(connectionString) ?? "Shared";

            return new Lib.Db.Hosting.ProcessSlotAllocator(isolationKey, logger);
        });

        // ====================================================================
        // 2. IDistributedCache 등록
        // ====================================================================
        services.TryAddSingleton<IDistributedCache>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<SharedMemoryCache>>();
            var keyGenerator = sp.GetRequiredService<Lib.Db.Contracts.Cache.IIsolationKeyGenerator>();

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
            var targetName = options.ConnectionStringName ?? "Default";
            var connectionString = options.ConnectionStrings?.TryGetValue(targetName, out var cs) == true
                ? cs
                : GetFirstConnectionStringOrThrow(options, "SharedMemoryCache");

            string? isolationKey = keyGenerator.Generate(connectionString);
            var basePath = Path.Combine(Path.GetTempPath(), "LibDbCache");

            logger.LogInformation(
                "[공유메모리캐시] 활성화 - SharedMemoryCache 사용 (플랫폼: {OS}, 격리키: {Key})",
                RuntimeInformation.OSDescription,
                isolationKey ?? "None");

            var cacheOptions = new SharedMemoryCacheOptions
            {
                BasePath = basePath,
                IsolationKey = isolationKey ?? "Shared"
            };

            return new SharedMemoryCache(Microsoft.Extensions.Options.Options.Create(cacheOptions), logger);
        });

        // ====================================================================
        // 3. ICacheLeaderElection 등록
        // ====================================================================
        services.TryAddSingleton<ICacheLeaderElection>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<CacheLeaderElection>>();

            bool enableSharedMemory = options.EnableSharedMemoryCache
                ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            if (!enableSharedMemory)
            {
                return new NoOpCacheLeaderElection();
            }
            
            // 공유 메모리 옵션 (SharedMemoryCache와 동일한 설정 사용)
            var cacheService = sp.GetRequiredService<IDistributedCache>();
            if (cacheService is not SharedMemoryCache)
            {
                // Edge Case: 옵션은 켜졌는데 캐시 등록이 꼬인 경우
                return new NoOpCacheLeaderElection();
            }

            // SharedMemoryCache를 위해 생성된 IsolationKey를 재사용하기 위해
            // 여기서는 독립적으로 CacheInternalHelpers를 통해 옵션을 다시 구성하거나,
            // 별도의 SharedMemoryCacheOptions를 등록해서 공유해야 함.
            // 하지만 현재 구조상 ServiceRegistrationHelpers 내부에서 직접 생성하므로,
            // 여기서도 동일한 로직으로 IsolationKey를 생성해야 함.

            var keyGenerator = sp.GetRequiredService<Lib.Db.Contracts.Cache.IIsolationKeyGenerator>();
            
            var targetName = options.ConnectionStringName ?? "Default";
            var connectionString = options.ConnectionStrings?.TryGetValue(targetName, out var cs) == true
                ? cs
                : GetFirstConnectionStringOrThrow(options, "CacheLeaderElection");

            string? isolationKey = keyGenerator.Generate(connectionString);
            var basePath = Path.Combine(Path.GetTempPath(), "LibDbCache");

            var sharedOptions = new SharedMemoryCacheOptions
            {
                BasePath = basePath,
                IsolationKey = isolationKey ?? "Shared"
            };

            return new CacheLeaderElection(Microsoft.Extensions.Options.Options.Create(sharedOptions), logger);
        });

        // ====================================================================
        // 4. CacheMaintenanceService 등록 (Hosted Service)
        // ====================================================================
        services.AddHostedService<CacheMaintenanceService>();
    }

    #endregion

    #region [헬퍼] IsolationKey 생성 (DEPRECATED)
    // ========================================
    // [v3 -> v4] GenerateIsolationKey 메서드 제거됨
    // ========================================
    // 이전 로직은 Lib.Db.Caching.IsolationKeyGenerator로 이동되었습니다.
    // DI를 통해 IIsolationKeyGenerator를 주입받아 사용하십시오.

    /// <summary>
    /// Connection String을 정규화하고 XxHash128로 해싱하여 IsolationKey를 생성합니다.
    /// </summary>
    /// <param name="options">LibDbOptions</param>
    /// <param name="logger">로거</param>
    /// <returns>
    /// 32자 Hex 문자열 (XxHash128) 또는 null (ConnectionStrings 없음)
    /// </returns>
    /// <remarks>
    /// <para><b>[동작 흐름]</b></para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <b>Step 1</b>: ConnectionStrings["Default"] 또는 첫 번째 값 가져오기
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Step 2</b>: <see cref="SqlConnectionStringBuilder"/>로 정규화 (대소문자/공백 제거)
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Step 3</b>: <c>XxHash128</c>로 해싱 (32자 hex)
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>폴백</b>: SqlConnectionStringBuilder 파싱 실패 시 원본 문자열 해싱
    ///       <br/>(PostgreSQL, MySQL 등 다른 DB 지원)
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>[예시]</b></para>
    /// <code>
    /// // Input: "Server=localhost;Database=Test;User=sa;Password=pass"
    /// // Normalized: "Data Source=localhost;Initial Catalog=Test;User ID=sa;Password=pass"
    /// // Hash: "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6" (32자)
    /// </code>
    /// </remarks>
    private static string? GenerateIsolationKey(LibDbOptions options, ILogger logger)
    {
        if (options.ConnectionStrings == null || options.ConnectionStrings.Count == 0)
        {
            logger.LogWarning("[IsolationKey] ConnectionStrings가 비어있습니다. 기본 키(Shared) 사용");
            return null;
        }

        // ====================================================================
        // Step 1: "Default" 키로 Connection String 가져오기
        // ====================================================================
        var defaultConnString = options.ConnectionStrings.TryGetValue("Default", out var cs)
            ? cs
            : GetFirstConnectionStringOrThrow(options, "IsolationKey");

        try
        {
            // ================================================================
            // Step 2: Connection String 정규화 (대소문자/공백 차이 제거)
            // ================================================================
            var builder = new SqlConnectionStringBuilder(defaultConnString);
            var canonical = builder.ConnectionString;

            // ================================================================
            // Step 3: XxHash128로 해싱 (32자, EpochStore와 동일)
            // ================================================================
            var hash = System.IO.Hashing.XxHash128.Hash(
                Encoding.UTF8.GetBytes(canonical));

            var key = Convert.ToHexString(hash).ToLowerInvariant();

            logger.LogInformation("[IsolationKey] 생성 완료: {Key} (정규화됨)", key);
            return key;
        }
        catch (ArgumentException ex)
        {
            // ================================================================
            // 폴백: PostgreSQL, MySQL 등 다른 DB 사용 시 파싱 실패
            // ================================================================
            logger.LogWarning(ex,
                "[IsolationKey] SqlConnectionStringBuilder 파싱 실패. " +
                "원본 문자열 해싱으로 폴백합니다.");

            // 폴백: 원본 문자열 그대로 해싱 (정규화 없이)
            var hash = System.IO.Hashing.XxHash128.Hash(
                Encoding.UTF8.GetBytes(defaultConnString));

            var key = Convert.ToHexString(hash).ToLowerInvariant();

            logger.LogInformation("[IsolationKey] 생성 완료: {Key} (비정규화)", key);
            return key;
        }
    }

    #endregion

    #region [헬퍼] 내부 유틸리티 메서드

    /// <summary>
    /// ConnectionStrings의 첫 번째 값을 반환합니다.
    /// <para>
    /// <b>[최적화]</b> LINQ 없이 열거자로 직접 접근
    /// </para>
    /// </summary>
    /// <param name="options">LibDbOptions 인스턴스</param>
    /// <param name="context">컨텍스트 정보 (예외 메시지용)</param>
    /// <returns>첫 번째 연결 문자열</returns>
    /// <exception cref="InvalidOperationException">연결 문자열이 없을 때</exception>
    private static string GetFirstConnectionStringOrThrow(LibDbOptions options, string context)
    {
        if (options.ConnectionStrings == null || options.ConnectionStrings.Count == 0)
            throw new InvalidOperationException($"{context}: LibDbOptions.ConnectionStrings에 연결 문자열이 등록되지 않았습니다.");
        
        using var enumerator = options.ConnectionStrings.Values.GetEnumerator();
        enumerator.MoveNext();
        return enumerator.Current;
    }

    #endregion
}
