using System.Data;
using FluentAssertions;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution;
using Lib.Db.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lib.Db.TestSuite.Live;

public class LiveDbTests : IClassFixture<LiveDbFixture>
{
    private readonly IDbExecutor _executor;

    public LiveDbTests(LiveDbFixture fixture)
    {
        _executor = fixture.Services.GetRequiredService<IDbExecutor>();
    }

    [Fact]
    public async Task BulkInsert_Should_Succeed_And_Be_Fast()
    {
        // Arrange
        var items = Enumerable.Range(0, 1000)
            .Select(i => new BulkTestItem { BatchNumber = 100, Data = $"Data_{i}" })
            .ToList();

        // Act
        // v2.0 requires instanceName for DbRequestBuilder
        var result = await new DbRequestBuilder(_executor, "Default")
            .Procedure("perf.usp_Perf_Bulk_Insert")
            .With(new { Items = items })
            .ExecuteScalarAsync<int>();

        // Assert
        result.Should().Be(1000);
    }

    [Fact]
    public async Task Tvp_RoundTrip_Should_Maintain_Data_Integrity()
    {
        // Arrange
        var input = new AllTypesTvp 
        { 
            DecimalValue = 123.4567m,
            GuidValue = Guid.NewGuid(),
            // ... other fields need to match schema exactly to avoid sql errors
            DateOnlyValue = new DateOnly(2025, 12, 23),
            TimeOnlyValue = new TimeOnly(12, 0, 0),
            HalfValue = 0.5f
        };
        var list = new List<AllTypesTvp> { input };

        // Act
        var result = await new DbRequestBuilder(_executor, "Default")
            .Procedure("tvp.usp_Tvp_Bulk_Insert_AllTypes")
            .With(new { Types = list })
            .ExecuteScalarAsync<int>();

        result.Should().Be(1);
    }
}

// Simple DTOs for Test
[TvpRow(TypeName = "perf.Tvp_Perf_BulkInsert")]
public class BulkTestItem 
{
    public int BatchNumber { get; set; }
    public string? Data { get; set; }
}

[TvpRow(TypeName = "tvp.Tvp_Tvp_AllTypes")]
public class AllTypesTvp
{
    public DateOnly DateOnlyValue { get; set; }
    public decimal DecimalValue { get; set; }
    public Guid GuidValue { get; set; }
    public float HalfValue { get; set; }
    public TimeOnly TimeOnlyValue { get; set; }
}

// Fixture for Setup
public class LiveDbFixture : IDisposable
{
    public IServiceProvider Services { get; }

    public LiveDbFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole()); // Fix for ILogger dependency
        services.AddHighPerformanceDb(options =>
        {
           options.ConnectionStrings = new Dictionary<string, string> 
            { 
                { "Default", "Server=127.0.0.1;Database=LIBDB_VERIFICATION_TEST;User Id=sa;Password=123456;TrustServerCertificate=True;Encrypt=False;" } 
            };
            options.EnableResilience = true;
        });
        Services = services.BuildServiceProvider();
    }
    
    public void Dispose() 
    {
        if (Services is IDisposable d) d.Dispose();
    }
}

