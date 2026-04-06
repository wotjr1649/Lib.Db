// ============================================================================
// 파일: Live/LiveDbTests.cs
// 설명: 실제 DB 대상 라이브 테스트 (BulkInsert, TVP RoundTrip)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution;
using Lib.Db.Fluent;

namespace Lib.Db.IntegrationTests.Live;

public sealed class LiveDbTests : IClassFixture<LiveDbFixture>
{
    private readonly IDbExecutor _executor;

    public LiveDbTests(LiveDbFixture fixture)
    {
        _executor = fixture.Services.GetRequiredService<IDbExecutor>();
    }

    [Fact]
    public async Task BulkInsert_Should_Succeed_And_Be_Fast()
    {
        List<BulkTestItem> items = Enumerable.Range(0, 1000)
            .Select(i => new BulkTestItem { BatchNumber = 100, Data = $"Data_{i}" })
            .ToList();

        DbResult<int> result = await new DbRequestBuilder(_executor, "Default")
            .Procedure("perf.usp_Perf_Bulk_Insert")
            .With(new { Items = items })
            .ExecuteScalarAsync<int>();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1000);
    }

    [Fact]
    public async Task Tvp_RoundTrip_Should_Maintain_Data_Integrity()
    {
        AllTypesTvp input = new()
        {
            DecimalValue = 123.4567m,
            GuidValue = Guid.NewGuid(),
            DateOnlyValue = new DateOnly(2025, 12, 23),
            TimeOnlyValue = new TimeOnly(12, 0, 0),
            HalfValue = 0.5f
        };
        List<AllTypesTvp> list = [input];

        DbResult<int> result = await new DbRequestBuilder(_executor, "Default")
            .Procedure("tvp.usp_Tvp_Bulk_Insert_AllTypes")
            .With(new { Types = list })
            .ExecuteScalarAsync<int>();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }
}

/// <summary>
/// BulkInsert 테스트용 TVP DTO.
/// </summary>
[TvpRow(TypeName = "perf.Tvp_Perf_BulkInsert")]
public sealed class BulkTestItem
{
    public int BatchNumber { get; set; }
    public string? Data { get; set; }
}

/// <summary>
/// AllTypes TVP 라운드트립 테스트용 DTO.
/// </summary>
[TvpRow(TypeName = "tvp.Tvp_Tvp_AllTypes")]
public sealed class AllTypesTvp
{
    public DateOnly DateOnlyValue { get; set; }
    public decimal DecimalValue { get; set; }
    public Guid GuidValue { get; set; }
    public float HalfValue { get; set; }
    public TimeOnly TimeOnlyValue { get; set; }
}

/// <summary>
/// Live 테스트용 픽스처 (독자 DI 구성).
/// </summary>
public sealed class LiveDbFixture : IDisposable
{
    public IServiceProvider Services { get; }

    public LiveDbFixture()
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.AddConsole());
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
