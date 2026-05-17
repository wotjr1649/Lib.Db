// ============================================================================
// 파일: Unit/ServiceRegistrationHelpersTests.cs
// 설명: 서비스 등록 경로의 multi-instance fail-closed 회귀 테스트
// ============================================================================

using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class ServiceRegistrationHelpersTests
{
    [Fact]
    public void ProcessSlotAllocator_ShouldFailClosed_WhenPrimaryConnectionNameIsMissing()
    {
        using ServiceProvider provider = BuildProviderWithMissingPrimaryConnection();

        Action act = () => provider.GetRequiredService<IProcessSlotAllocator>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ProcessSlotAllocator*Primary*ConnectionStrings*");
    }

    [Fact]
    public void SharedMemoryCache_ShouldFailClosed_WhenPrimaryConnectionNameIsMissing()
    {
        using ServiceProvider provider = BuildProviderWithMissingPrimaryConnection();

        Action act = () => provider.GetRequiredService<IDistributedCache>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SharedMemoryCache*Primary*ConnectionStrings*");
    }

    private static ServiceProvider BuildProviderWithMissingPrimaryConnection()
    {
        LibDbOptions options = new()
        {
            ConnectionStringNames = ["Primary"],
            EnableSharedMemoryCache = true
        };

        options.ConnectionStrings["Secondary"] =
            "Server=(localdb)\\MSSQLLocalDB;Database=SecondaryDb;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IOptions<LibDbOptions>>(Options.Create(options));

        ServiceRegistrationHelpers.RegisterConditionalSharedMemoryCache(services);

        return services.BuildServiceProvider();
    }
}
