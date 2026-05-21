// ============================================================================
// 파일: Infrastructure/MultiDbFixture.cs
// 설명: Lib.Db 통합 테스트 DB 인스턴스를 중앙에서 관리하는 테스트 픽스처
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// Lib.Db 통합 테스트 DB 인스턴스를 중앙에서 관리하는 xUnit 테스트 픽스처.
/// <para><b>[설계 의도]</b> 모든 테스트가 이 픽스처를 공유하여 DB 연결을 재사용한다.</para>
/// </summary>
public sealed class MultiDbFixture : IAsyncLifetime
{
    private static readonly DatabaseScript[] s_databaseScripts =
    [
        new(TestConnectionStrings.Verification, "setup-libdb-verification-test.sql"),
        new(TestConnectionStrings.Stress, "setup-libdb-stress-test.sql", "verify-libdb-stress-test.sql"),
        new(TestConnectionStrings.Chaos, "setup-libdb-chaos-test.sql", "verify-libdb-chaos-test.sql"),
        new(TestConnectionStrings.Benchmark, "setup-libdb-bench-test.sql", "verify-libdb-bench-test.sql")
    ];

    /// <summary>Lib.Db 세션 (멀티 인스턴스)</summary>
    public IDbSession Session { get; private set; } = null!;

    /// <summary>DI 서비스 프로바이더</summary>
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>테스트 구성</summary>
    public IConfiguration Configuration { get; private set; } = null!;

    /// <summary>LIBDB_VERIFICATION_TEST 인스턴스 접근</summary>
    public IProcedureStage Verification => UseConfigured(TestConnectionStrings.Verification);

    /// <summary>LV_ANP_SORTER 인스턴스 접근</summary>
    public IProcedureStage Sorter => UseConfigured(TestConnectionStrings.Sorter);

    /// <summary>LIBDB_STRESS_TEST 인스턴스 접근</summary>
    public IProcedureStage Stress => UseConfigured(TestConnectionStrings.Stress);

    /// <summary>LIBDB_CHAOS_TEST 인스턴스 접근</summary>
    public IProcedureStage Chaos => UseConfigured(TestConnectionStrings.Chaos);

    /// <summary>LIBDB_BENCH_TEST 인스턴스 접근</summary>
    public IProcedureStage Benchmark => UseConfigured(TestConnectionStrings.Benchmark);

    public async ValueTask InitializeAsync()
    {
        Configuration = TestConnectionStrings.CreateConfiguration();
        _ = TestConnectionStrings.Require(Configuration, TestConnectionStrings.Verification);
        _ = TestConnectionStrings.Require(Configuration, TestConnectionStrings.Sorter);

        TestConnectionStrings.RequireSafeSchemaInitialization(Configuration, TestConnectionStrings.Verification);
        TestConnectionStrings.RequireSafeSchemaInitialization(Configuration, TestConnectionStrings.Sorter);
        await EnsureConfiguredDatabasesAsync().ConfigureAwait(false);

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddLibDb(Configuration);

        Services = services.BuildServiceProvider();
        Session = Services.GetRequiredService<IDbSession>();

        // 기존 스키마 + [test] 스키마 + 추가 SP 생성 (Verification DB 전용)
        await SchemaInitializer.EnsureAllSchemasAsync(Verification).ConfigureAwait(false);

        // Sorter 별칭용 조회 테이블 + 로그/흐름 SP 생성
        await SchemaInitializer.EnsureSorterSchemaAsync(Sorter).ConfigureAwait(false);

        // 기본 시드 데이터 (Alice, Bob, Charlie + Products 3개)
        await SchemaInitializer.SeedBaseDataAsync(Verification).ConfigureAwait(false);
    }

    public string GetConnectionString(string name)
        => TestConnectionStrings.Require(Configuration, name);

    private IProcedureStage UseConfigured(string name)
    {
        _ = TestConnectionStrings.Require(Configuration, name);
        return Session.Use(name);
    }

    private async Task EnsureConfiguredDatabasesAsync()
    {
        foreach (DatabaseScript database in s_databaseScripts)
        {
            if (!TestConnectionStrings.TryGet(Configuration, database.ConnectionName, out string connectionString))
                continue;

            TestConnectionStrings.RequireSafeSchemaInitialization(Configuration, database.ConnectionName);
            await SqlScriptRunner.ExecuteScriptAsync(connectionString, database.SetupScript, CancellationToken.None)
                .ConfigureAwait(false);

            if (database.VerifyScript is not null)
            {
                await SqlScriptRunner.ExecuteScriptAsync(connectionString, database.VerifyScript, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Session is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        if (Services is IDisposable disposable)
            disposable.Dispose();
    }

    private sealed record DatabaseScript(string ConnectionName, string SetupScript, string? VerifyScript = null);
}
