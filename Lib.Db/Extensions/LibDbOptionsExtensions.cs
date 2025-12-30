// ============================================================================
// 파일: Lib.Db/Extensions/LibDbOptionsExtensions.cs
// 설명: LibDbOptions 설정을 위한 확장 메서드
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using Lib.Db.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// LibDbOptions 설정을 위한 확장 메서드입니다.
/// <para>
/// <b>[표준 패턴]</b> .NET Options Pattern (OptionsBuilder) 활용
/// </para>
/// </summary>
public static class LibDbOptionsExtensions
{
    /// <summary>
    /// LibDbOptions를 구성하고 등록합니다. (표준 OptionsBuilder 패턴)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configure">Options 설정 델리게이트</param>
    /// <returns>추가 구성을 위한 OptionsBuilder</returns>
    public static OptionsBuilder<LibDbOptions> AddLibDbOptions(
        this IServiceCollection services,
        Action<LibDbOptions>? configure = null)
    {
        var builder = services.AddOptions<LibDbOptions>()
                              .ValidateOnStart();

        if (configure != null)
        {
            builder.Configure(configure);
        }

        // ✅ v2.0: Options Validator 등록
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LibDbOptions>, Lib.Db.Configuration.LibDbOptionsValidator>());

        // Options → Singleton도 등록 (역호환성)
        // 기존 코드에서 LibDbOptions를 직접 주입받는 경우 대비
        services.TryAddSingleton(sp =>
            sp.GetRequiredService<IOptions<LibDbOptions>>().Value);

        return builder;
    }

    /// <summary>
    /// IConfiguration에서 LibDbOptions를 바인딩합니다.
    /// <para>
    /// <b>[사용 예시]</b><br/>
    /// appsettings.json에 "LibDb" 섹션 정의 후 자동 바인딩
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configuration">구성 객체</param>
    /// <param name="sectionName">섹션 이름 (기본: "LibDb")</param>
    /// <returns>추가 구성을 위한 OptionsBuilder</returns>
    public static OptionsBuilder<LibDbOptions> AddLibDbOptionsFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "LibDb")
    {
        var builder = services.AddOptions<LibDbOptions>()
                              .Configure(options => BindLibDbOptions(options, configuration.GetSection(sectionName)))
                              .ValidateOnStart();

        // ✅ v2.0: Options Validator 등록
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LibDbOptions>, Lib.Db.Configuration.LibDbOptionsValidator>());

        // Singleton 등록 (역호환성)
        services.TryAddSingleton(sp =>
            sp.GetRequiredService<IOptions<LibDbOptions>>().Value);

        return builder;
    }

    /// <summary>
    /// Manual Binding Helper to replace reflection-based Bind and avoid AOT warnings (SYSLIB1100).
    /// </summary>
    private static void BindLibDbOptions(LibDbOptions options, IConfigurationSection section)
    {
        // 1. General
        if (bool.TryParse(section["EnableSchemaCaching"], out var esc)) options.EnableSchemaCaching = esc;
        if (int.TryParse(section["SchemaRefreshIntervalSeconds"], out var sris)) options.SchemaRefreshIntervalSeconds = sris;
        if (bool.TryParse(section["EnableDryRun"], out var edr)) options.EnableDryRun = edr;
        
        // ConnectionStrings (Dictionary)
        var connSection = section.GetSection("ConnectionStrings");
        foreach (var child in connSection.GetChildren())
        {
            options.ConnectionStrings[child.Key] = child.Value ?? "";
        }

        // PrewarmSchemas (List)
        var prewarmSection = section.GetSection("PrewarmSchemas");
        if (prewarmSection.Exists())
        {
            options.PrewarmSchemas.Clear(); // Override default
            foreach (var child in prewarmSection.GetChildren())
            {
                if (child.Value != null) options.PrewarmSchemas.Add(child.Value);
            }
        }
        
        // Resilience (Complex)
        var resSection = section.GetSection("Resilience");
        if (resSection.Exists())
        {
             // ... Simple binding for key props
             if (bool.TryParse(section["EnableResilience"], out var er)) options.EnableResilience = er;
             
             // ResilienceOptions
             if (int.TryParse(resSection["MaxRetryCount"], out var mrc)) options.Resilience.MaxRetryCount = mrc;
             if (int.TryParse(resSection["BaseRetryDelayMs"], out var brd)) options.Resilience.BaseRetryDelayMs = brd;
        }

        // SharedMemoryCache (Complex)
        var smcSection = section.GetSection("SharedMemoryCache");
        if (smcSection.Exists())
        {
             // Bind relevant props
        }
        
        // Skip JsonOptions (SYSLIB1100 Trigger)
    }

    /// <summary>
    /// Options 구성 후 추가 검증을 수행합니다.
    /// </summary>
    /// <param name="builder">OptionsBuilder</param>
    /// <param name="validation">검증 조건</param>
    /// <param name="failureMessage">실패 시 메시지</param>
    /// <returns>체이닝을 위한 OptionsBuilder</returns>
    public static OptionsBuilder<LibDbOptions> WithValidation(
        this OptionsBuilder<LibDbOptions> builder,
        Func<LibDbOptions, bool> validation,
        string failureMessage)
    {
        return builder.Validate(validation, failureMessage);
    }

    /// <summary>
    /// Options 구성 후 PostConfigure를 수행합니다.
    /// <para>
    /// <b>[사용 시나리오]</b> 다른 서비스 기반 동적 설정
    /// </para>
    /// </summary>
    /// <param name="builder">OptionsBuilder</param>
    /// <param name="postConfigure">PostConfigure 델리게이트</param>
    /// <returns>체이닝을 위한 OptionsBuilder</returns>
    public static OptionsBuilder<LibDbOptions> WithPostConfigure(
        this OptionsBuilder<LibDbOptions> builder,
        Action<LibDbOptions> postConfigure)
    {
        builder.PostConfigure(postConfigure);
        return builder;
    }
}
