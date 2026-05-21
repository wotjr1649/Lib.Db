// ============================================================================
// 파일: VerificationDb/TvpSchemaFlushTests.cs
// 설명: fluent schema maintenance API 및 targeted TVP flush 라우팅 검증
// ============================================================================

using Lib.Db.Contracts.Schema;
using Lib.Db.Core;
using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

public sealed class TvpSchemaFlushTests
{
    [Fact]
    public async Task FlushTvpAsync_ShouldRouteThroughCoordinatorWithNormalizedTvpName()
    {
        RecordingFlushCoordinator coordinator = new();
        SchemaMaintenanceStage stage = new(
            tvpSchemaProvider: null,
            flushCoordinator: coordinator,
            instanceHash: "Verification");

        await stage.FlushTvpAsync("[tvp].[Tvp_Tvp_AllTypes]", TestContext.Current.CancellationToken);

        coordinator.TvpFlushCalls.Should().Be(1);
        coordinator.SchemaFlushCalls.Should().Be(0);
        coordinator.LastInstanceHash.Should().Be("Verification");
        coordinator.LastTvpName.Should().Be("tvp.Tvp_Tvp_AllTypes");
    }

    [Fact]
    public async Task FlushSchemaAsync_ShouldRouteThroughCoordinator()
    {
        RecordingFlushCoordinator coordinator = new();
        SchemaMaintenanceStage stage = new(
            tvpSchemaProvider: null,
            flushCoordinator: coordinator,
            instanceHash: "Verification");

        await stage.FlushSchemaAsync(TestContext.Current.CancellationToken);

        coordinator.SchemaFlushCalls.Should().Be(1);
        coordinator.TvpFlushCalls.Should().Be(0);
        coordinator.LastInstanceHash.Should().Be("Verification");
    }

    [Fact]
    public async Task FlushTvpAsync_InvalidTvpName_ShouldThrowBeforeCoordinator()
    {
        RecordingFlushCoordinator coordinator = new();
        SchemaMaintenanceStage stage = new(
            tvpSchemaProvider: null,
            flushCoordinator: coordinator,
            instanceHash: "Verification");

        Func<Task> act = () => stage.FlushTvpAsync("dbo.Bad-Name", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
        coordinator.TvpFlushCalls.Should().Be(0);
    }

    private sealed class RecordingFlushCoordinator : ISchemaFlushCoordinator
    {
        public int SchemaFlushCalls { get; private set; }

        public int TvpFlushCalls { get; private set; }

        public string? LastInstanceHash { get; private set; }

        public string? LastTvpName { get; private set; }

        public Task FlushAsync(string instanceHash, CancellationToken ct = default)
        {
            SchemaFlushCalls++;
            LastInstanceHash = instanceHash;
            return Task.CompletedTask;
        }

        public Task FlushTvpAsync(string instanceHash, string tvpName, CancellationToken ct = default)
        {
            TvpFlushCalls++;
            LastInstanceHash = instanceHash;
            LastTvpName = tvpName;
            return Task.CompletedTask;
        }

        public long GetCurrentEpoch(string instanceHash) => 0;

        public Task<bool> CheckAndSyncEpochAsync(string instanceHash, CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}

[Collection("MultiDb")]
public sealed class TvpSchemaFlushSessionTests(MultiDbFixture fixture)
{
    [Fact]
    public async Task SessionUseSchema_FlushTvpAsync_ShouldFlushKnownVerificationTvp()
    {
        await fixture.Session
            .UseSchema("Verification")
            .FlushTvpAsync("tvp.Tvp_Tvp_AllTypes", TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Session_ShouldExposeDefaultSchemaStage()
    {
        fixture.Session.Schema.Should().NotBeNull();
    }
}
