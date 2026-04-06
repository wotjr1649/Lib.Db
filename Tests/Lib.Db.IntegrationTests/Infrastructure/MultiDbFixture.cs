// ============================================================================
// 파일: Infrastructure/MultiDbFixture.cs
// 설명: 2개 DB 인스턴스를 중앙에서 관리하는 테스트 픽스처
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// 2개 DB(Verification + Sorter) 인스턴스를 중앙에서 관리하는 xUnit 테스트 픽스처.
/// <para><b>[설계 의도]</b> 모든 테스트가 이 픽스처를 공유하여 DB 연결을 재사용한다.</para>
/// </summary>
public sealed class MultiDbFixture : IAsyncLifetime
{
    /// <summary>Lib.Db 세션 (멀티 인스턴스)</summary>
    public IDbSession Session { get; private set; } = null!;

    /// <summary>DI 서비스 프로바이더</summary>
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>LIBDB_VERIFICATION_TEST 인스턴스 접근</summary>
    public IProcedureStage Verification => Session.Use("Verification");

    /// <summary>LV_ANP_SORTER 인스턴스 접근</summary>
    public IProcedureStage Sorter => Session.Use("Sorter");

    public async ValueTask InitializeAsync()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddLibDb(configuration);

        Services = services.BuildServiceProvider();
        Session = Services.GetRequiredService<IDbSession>();

        // 기존 스키마 + [test] 스키마 + 추가 SP 생성 (Verification DB 전용)
        await SchemaInitializer.EnsureAllSchemasAsync(Session.Use("Verification")).ConfigureAwait(false);

        // 기본 시드 데이터 (Alice, Bob, Charlie + Products 3개)
        await SchemaInitializer.SeedBaseDataAsync(Session.Use("Verification")).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Session is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        if (Services is IDisposable disposable)
            disposable.Dispose();
    }
}
