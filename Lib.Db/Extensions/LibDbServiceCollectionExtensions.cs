// ============================================================================
// 파일: Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs
// 설명: Lib.Db DI 통합 확장 메서드
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Caching;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Schema;
using Lib.Db.Diagnostics;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Tvp;
using Lib.Db.Hosting;
using Lib.Db.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// <see cref="IConfiguration"/>을 통해 Lib.Db 필수 서비스를 일괄 등록합니다.
    /// <para>
    /// appsettings.json의 "LibDb" 섹션과 최상위 "ConnectionStrings" 섹션 바인딩을 처리합니다.
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configuration">Lib.Db 설정을 포함한 애플리케이션 구성</param>
    /// <returns>체이닝을 위한 <see cref="IServiceCollection"/></returns>
    [RequiresUnreferencedCode(
        "ConfigurationBinder-based AddLibDb overload is a configuration convenience API. Use AddLibDb(Action<LibDbOptions>) for Native AOT.")]
    [RequiresDynamicCode(
        "ConfigurationBinder-based AddLibDb overload can require runtime code generation. Use AddLibDb(Action<LibDbOptions>) for Native AOT.")]
    public static IServiceCollection AddLibDb(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddHighPerformanceDb(options =>
        {
            // 1. LibDb 섹션 바인딩 (ConnectionStringNames 등 설정만)
            Lib.Db.Configuration.Internal.LibDbConfig config = new();
            configuration.GetSection("LibDb").Bind(config);
            config.ApplyTo(options);

            // 2. 최상위 ConnectionStrings에서 ConnectionStringNames에 지정된 키만 선택적 로드
            IConfigurationSection rootCs = configuration.GetSection("ConnectionStrings");
            if (rootCs.Exists())
            {
                HashSet<string> allowed = new(options.ConnectionStringNames, StringComparer.OrdinalIgnoreCase);
                foreach (IConfigurationSection child in rootCs.GetChildren())
                {
                    if (child.Value is not null && allowed.Contains(child.Key))
                    {
                        options.ConnectionStrings[child.Key] = child.Value;
                    }
                }
            }
        });
    }

    /// <summary>
    /// 코드 기반 옵션 설정으로 Lib.Db 필수 서비스를 일괄 등록합니다.
    /// <para>
    /// 런타임 TVP fast-path는 <c>services.AddLibDb(o => o.Tvp.Map&lt;TRow&gt;("dbo.Type"))</c>
    /// 형태로 등록할 수 있습니다.
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configure">Lib.Db 옵션 설정 델리게이트</param>
    /// <returns>체이닝을 위한 <see cref="IServiceCollection"/></returns>
    public static IServiceCollection AddLibDb(
        this IServiceCollection services,
        Action<LibDbOptions> configure)
        => services.AddHighPerformanceDb(configure);

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

        // ForceEnable: ConnectionString에 MARS 자동 주입
        // PostConfigure는 모든 Configure/IConfigureOptions 실행 후 마지막으로 호출됩니다.
        services.PostConfigure<LibDbOptions>(options =>
        {
            if (options.Mars != MarsPolicy.ForceEnable)
                return;

            // 기존 딕셔너리를 순회하며 MARS가 누락된 연결 문자열에만 자동 주입
            Dictionary<string, string> corrected = new(options.ConnectionStrings.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kvp in options.ConnectionStrings)
            {
                Microsoft.Data.SqlClient.SqlConnectionStringBuilder builder =
                    new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(kvp.Value);

                if (!builder.MultipleActiveResultSets)
                {
                    // MARS 미설정 항목에 자동 주입 (로그는 런타임 Logger 없이 불가하므로 생략)
                    builder.MultipleActiveResultSets = true;
                    corrected[kvp.Key] = builder.ConnectionString;
                }
                else
                {
                    corrected[kvp.Key] = kvp.Value; // 원본 보존
                }
            }

            options.ConnectionStrings = corrected;
        });

        // Runtime TVP fast-path 등 정적 바인딩 정책을 최종 옵션으로 반영합니다.
        services.PostConfigure<LibDbOptions>(DbBinder.ConfigureTvp);

        // EnableObservability는 ActivitySource뿐 아니라 DbMetrics 전역 게이트까지 제어합니다.
        services.PostConfigure<LibDbOptions>(static options =>
            DbMetrics.IsEnabled = options.EnableObservability);

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
        services.TryAddSingleton<ITvpSchemaProvider, TvpSchemaProvider>();

        // Session (Scoped)
        services.TryAddScoped<DbSession>();
        services.TryAddScoped<IDbSession>(sp => sp.GetRequiredService<DbSession>());
        // IDbContext 제거됨 — IDbSession이 유일한 진입점

        // Mapper
        services.TryAddSingleton<IMapperFactory, MapperFactory>();

        // DbExecutor 의존성 (내부 헬퍼 사용)
        ServiceRegistrationHelpers.RegisterExecutor(services);

        // HybridCache AOT Serializers
        ServiceRegistrationHelpers.RegisterAotSerializers(services);

        // 조건부 공유 메모리 캐시 (크로스 플랫폼 지원)
        ServiceRegistrationHelpers.RegisterConditionalSharedMemoryCache(services);

        // Epoch 기반 Schema Flush Coordination
        services.AddSchemaFlushCoordination();

        return services;
    }

    /// <summary>
    /// Lib.Db의 내장 SharedMemoryCache를 명시적으로 L2 캐시 provider로 등록합니다.
    /// </summary>
    /// <remarks>
    /// 기본 <see cref="AddLibDb(IServiceCollection, Action{LibDbOptions})"/> 경로는
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>를 등록하지 않습니다.
    /// Redis, SQL Server, Postgres 등 외부 provider-backed L2를 사용할 경우 이 메서드를 호출하지 말고
    /// 해당 provider를 애플리케이션에서 직접 등록하세요.
    /// </remarks>
    public static IServiceCollection AddLibDbSharedMemoryCache(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ServiceRegistrationHelpers.RegisterSharedMemoryCacheOptIn(services);
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
    /// <b>[설계 의도]</b><br/>
    /// BuildServiceProvider() 호출을 제거하여 중간 ServiceProvider 생성 비용과 싱글턴 중복 경고를 방지합니다.<br/>
    /// SchemaWarmupService가 <see cref="LibDbOptions"/>를 직접 주입받아 <c>ExecuteAsync</c> 진입 시
    /// <c>EnableSchemaCaching</c> 및 <c>PrewarmSchemas.Count</c>를 스스로 확인하므로,
    /// 조건이 미충족되면 서비스는 즉시 종료합니다.
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>체이닝을 위한 IServiceCollection</returns>
    public static IServiceCollection AddLibDbHostedServices(
        this IServiceCollection services)
    {
        // [Anti-pattern 제거] BuildServiceProvider() 대신 서비스 내부에서 옵션 확인
        // SchemaWarmupService.ExecuteAsync()가 PrewarmSchemas/EnableSchemaCaching을 직접 검사합니다.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, SchemaWarmupService>());

        return services;
    }

    /// <summary>
    /// Epoch 기반 스키마 캐시 조정 서비스를 등록합니다.
    /// <para>
    /// <b>[Provider-neutral 기본값]</b><br/>
    /// <see cref="LibDbOptions.EnableEpochCoordination"/>가 <c>null</c>이면:<br/>
    /// - <see cref="LibDbOptions.EnableSharedMemoryCache"/>가 <c>true</c>일 때만 활성화<br/>
    /// - OS에 따라 자동 활성화하지 않음
    /// </para>
    /// <para>
    /// <b>[등록 서비스]</b><br/>
    /// - <see cref="EpochStore"/> (Singleton)<br/>
    /// - <see cref="ISchemaFlushCoordinator"/> → <see cref="SchemaFlushService"/><br/>
    /// - <see cref="EpochWatcherService"/> (등록은 항상 수행, 감시 대상이 없으면 실행 시 종료)
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="epochBasePath">Epoch 파일 저장 경로 (기본값: %TEMP%/Lib.Db.Epochs)</param>
    /// <returns>체이닝을 위한 IServiceCollection</returns>
    /// <remarks>
    /// <b>오류 조건:</b><br/>
    /// <see cref="LibDbOptions.EnableSharedMemoryCache"/>가 <c>true</c>가 아닌데<br/>
    /// <see cref="LibDbOptions.EnableEpochCoordination"/> = <c>true</c>인 경우:<br/>
    /// 조정할 shared-memory 캐시가 없으므로 fail-fast합니다.
    /// </remarks>
    public static IServiceCollection AddSchemaFlushCoordination(
        this IServiceCollection services,
        string? epochBasePath = null)
    {
        // [Anti-pattern 제거] BuildServiceProvider() 호출 없이 팩토리 람다에서 런타임 옵션 확인
        // 1. EpochStore - 팩토리 내부에서 EnableEpochCoordination 판정 후 실제/Noop 동작을 결정
        services.TryAddSingleton(sp =>
        {
            LibDbOptions options = sp.GetRequiredService<LibDbOptions>();

            bool enableSharedMemory = options.EnableSharedMemoryCache is true;
            if (options.EnableEpochCoordination is true && !enableSharedMemory)
            {
                throw new InvalidOperationException(
                    "Lib.Db: EnableEpochCoordination=true requires explicit shared-memory cache opt-in. " +
                    "Call services.AddLibDbSharedMemoryCache(), or disable EnableEpochCoordination.");
            }

            bool enableEpoch = options.EnableEpochCoordination.GetValueOrDefault(enableSharedMemory);

            ILogger<EpochStore> logger = sp.GetRequiredService<ILogger<EpochStore>>();

            if (!enableEpoch)
            {
                logger.LogInformation(
                    "[Epoch] 비활성화됨 - EpochStore를 파일 시스템 없는 Noop 모드로 생성합니다 (명시적 설정: {ExplicitSetting})",
                    options.EnableEpochCoordination?.ToString() ?? "null (provider-neutral default)");

                return EpochStore.Disabled(logger);
            }

            string basePath = epochBasePath ?? Path.Combine(
                Path.GetTempPath(), "Lib.Db.Epochs");

            return new EpochStore(basePath, logger);
        });

        // 2. SchemaFlushService 등록
        services.TryAddSingleton<ISchemaFlushCoordinator, SchemaFlushService>();

        // 3. EpochWatcherService 등록
        // (EpochWatcherService.ExecuteAsync는 비활성 옵션 또는 WatchedInstances 공백 시 즉시 종료)
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, EpochWatcherService>());

        return services;
    }

    #endregion

    #region [확장 메서드] 쿼리 인터셉터 등록

    /// <summary>
    /// Lib.Db 사용자 수준 쿼리 인터셉터를 등록합니다.
    /// <para>
    /// <b>[사용법]</b><br/>
    /// <code>services.AddLibDbInterceptor&lt;MyLoggingInterceptor&gt;();</code>
    /// </para>
    /// <para>
    /// 다중 인터셉터를 등록하면 DI 등록 순서대로 체인이 실행됩니다.
    /// </para>
    /// </summary>
    /// <typeparam name="TInterceptor">인터셉터 구현 타입</typeparam>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>체이닝을 위한 IServiceCollection</returns>
    public static IServiceCollection AddLibDbInterceptor<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TInterceptor>(
        this IServiceCollection services)
        where TInterceptor : class, Lib.Db.Contracts.Infrastructure.IDbInterceptor
    {
        services.AddSingleton<Lib.Db.Contracts.Infrastructure.IDbInterceptor, TInterceptor>();
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
