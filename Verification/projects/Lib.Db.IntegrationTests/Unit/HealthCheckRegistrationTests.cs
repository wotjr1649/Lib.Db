// ============================================================================
// 파일: Unit/HealthCheckRegistrationTests.cs
// 설명: Lib.Db HealthCheck DI 등록 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Contracts.Infrastructure;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Reflection;

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

    [Fact]
    public async Task AddLibDbHealthCheck_ShouldReportTrustedCustomDistributedCacheAsVerifiedL2()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IDbConnectionFactory, RecordingConnectionFactory>();
        services.AddSingleton<IDistributedCache, RecordingDistributedCache>();
        services.AddLibDbTrustedDistributedCacheProvider<RecordingDistributedCache>();
        services.AddSingleton(new LibDbOptions
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] = TestConnectionStrings.Placeholder("LIBDB_HEALTH_TRUSTED_CACHE_TEST")
            },
            ConnectionStringNames = ["Default"],
            HealthCheckThrottleSeconds = 1
        });
        services.AddHealthChecks().AddLibDbHealthCheck();

        using ServiceProvider provider = services.BuildServiceProvider();
        HealthCheckService service = provider.GetRequiredService<HealthCheckService>();

        HealthReport report = await service.CheckHealthAsync(TestContext.Current.CancellationToken);

        HealthReportEntry entry = report.Entries["sql_db"];
        entry.Data["libdb.cache.has_verified_provider_l2"].Should().Be(true);
        entry.Data["libdb.cache.warnings"].Should().BeEquivalentTo(Array.Empty<string>());
    }

    [Fact]
    public void AddLibDbHealthCheck_ShouldRedactSensitiveMissingDefaultInstanceName()
    {
        const string sensitiveInstanceName = "Raw:Server=(localdb)\\MSSQLLocalDB;Database=MissingHealthDb;Encrypt=True";
        ServiceCollection services = new();
        using ServiceProvider provider = services.BuildServiceProvider();
        LibDbOptions options = new()
        {
            ConnectionStringNames = [sensitiveInstanceName],
            HealthCheckThrottleSeconds = 1
        };
        object healthCheck = CreateThrottledDbHealthCheck(new RecordingConnectionFactory(), options, provider);

        TargetInvocationException exception = InvokeGetDefaultInstanceName(healthCheck).Should()
            .Throw<TargetInvocationException>()
            .Which;

        exception.InnerException.Should().BeOfType<InvalidOperationException>();
        string message = exception.InnerException!.Message;
        message.Should().Contain("Raw:[redacted]");
        message.Should().NotContain("MissingHealthDb");
    }

    private static object CreateThrottledDbHealthCheck(
        IDbConnectionFactory factory,
        LibDbOptions options,
        IServiceProvider services)
    {
        Type healthCheckType = typeof(LibDbHealthCheckExtensions).GetNestedType(
            "ThrottledDbHealthCheck",
            BindingFlags.NonPublic) ?? throw new InvalidOperationException("HealthCheck type was not found.");

        return Activator.CreateInstance(
            healthCheckType,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            args: [factory, options, services],
            culture: null) ?? throw new InvalidOperationException("HealthCheck instance was not created.");
    }

    private static Action InvokeGetDefaultInstanceName(object healthCheck)
    {
        MethodInfo method = healthCheck.GetType().GetMethod(
            "GetDefaultInstanceName",
            BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("HealthCheck helper was not found.");

        return () => method.Invoke(healthCheck, parameters: null);
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

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public byte[]? Get(string key)
            => _values.TryGetValue(key, out byte[]? value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _values[key] = value;

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
            => Task.CompletedTask;

        public void Remove(string key)
            => _values.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
