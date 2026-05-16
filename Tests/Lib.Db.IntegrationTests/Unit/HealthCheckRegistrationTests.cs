// ============================================================================
// 파일: Unit/HealthCheckRegistrationTests.cs
// 설명: Lib.Db HealthCheck DI 등록 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Contracts.Infrastructure;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class HealthCheckRegistrationTests
{
    [Fact]
    public async Task AddLibDbHealthCheck_ShouldResolveWithoutDistributedCache()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IDbConnectionFactory, RecordingConnectionFactory>();
        services.AddSingleton(new LibDbOptions
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] = TestConnectionStrings.Placeholder("LIBDB_HEALTH_TEST")
            },
            ConnectionStringNames = ["Default"],
            HealthCheckThrottleSeconds = 1
        });
        services.AddHealthChecks().AddLibDbHealthCheck();

        using ServiceProvider provider = services.BuildServiceProvider();
        HealthCheckService service = provider.GetRequiredService<HealthCheckService>();

        HealthReport report = await service.CheckHealthAsync(TestContext.Current.CancellationToken);

        HealthReportEntry entry = report.Entries["sql_db"];
        entry.Data["libdb.cache.mode"].Should().Be("unregistered");
        entry.Data["libdb.cache.fallback_active"].Should().Be(false);
    }

    [Fact]
    public async Task AddLibDbHealthCheck_ShouldProbeConfiguredDefaultInstance()
    {
        ServiceCollection services = new();
        RecordingConnectionFactory factory = new();

        services.AddLogging();
        services.AddSingleton<IDbConnectionFactory>(factory);
        services.AddSingleton(new LibDbOptions
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                ["Secondary"] = TestConnectionStrings.Placeholder("LIBDB_HEALTH_SECONDARY_TEST"),
                ["Primary"] = TestConnectionStrings.Placeholder("LIBDB_HEALTH_PRIMARY_TEST")
            },
            ConnectionStringNames = ["Primary", "Secondary"],
            HealthCheckThrottleSeconds = 1
        });
        services.AddHealthChecks().AddLibDbHealthCheck();

        using ServiceProvider provider = services.BuildServiceProvider();
        HealthCheckService service = provider.GetRequiredService<HealthCheckService>();

        await service.CheckHealthAsync(TestContext.Current.CancellationToken);

        factory.LastInstanceName.Should().Be("Primary");
    }

    private sealed class RecordingConnectionFactory : IDbConnectionFactory
    {
        public string? LastInstanceName { get; private set; }

        public Task<SqlConnection> CreateConnectionAsync(string instanceHash, CancellationToken ct)
        {
            LastInstanceName = instanceHash;
            return Task.FromResult(new SqlConnection());
        }

        public void RegisterAdHocInstance(string instanceName, string connectionString)
        {
        }

        public void UnregisterAdHocInstance(string instanceName)
        {
        }
    }
}
