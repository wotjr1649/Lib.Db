// ============================================================================
// 파일: VerificationDb/PoolMetricsTests.cs
// 설명: 연결 풀 메트릭 계측 검증 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using Lib.Db.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// 연결 풀 메트릭(ConnectionAcquireDuration, ConnectionPoolWaits)이
/// 에러 없이 정상 동작하는지 검증하는 테스트입니다.
/// <para><b>[설계 의도]</b> OpenTelemetry 메트릭 직접 검증은 어렵지만,
/// 메트릭 코드가 예외 없이 동작하는지 통합적으로 확인합니다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class PoolMetricsTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;
    private readonly IDbSession _session = fixture.Session;

    #endregion

    #region PM01: 연결 획득 시 메트릭 기록 — 에러 없이 완료

    /// <summary>
    /// SELECT 1 실행 시 연결 획득 메트릭 코드가 에러 없이 동작하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task PM01_ConnectionAcquire_ShouldRecordDuration()
    {
        // Act
        DbResult<int> result = await _db
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>();

        // Assert — 메트릭 기록 코드가 에러 없이 실행되었으므로 쿼리 성공 확인
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    #endregion

    #region PM02: 동시 50개 쿼리 — 풀 대기 발생 시 메트릭 기록

    /// <summary>
    /// 동시 50개 쿼리를 실행하여 연결 풀 대기가 발생할 수 있는 상황에서
    /// 메트릭 코드가 에러 없이 동작하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task PM02_ConnectionPool_UnderLoad_ShouldRecordWaits()
    {
        // Arrange — 동시 50개 쿼리
        List<Task<DbResult<int>>> tasks = [];
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(_db.Sql("SELECT 1 AS Val").ExecuteScalarAsync<int>());
        }

        // Act
        DbResult<int>[] results = await Task.WhenAll(tasks);

        // Assert — 모든 쿼리가 메트릭 예외 없이 성공
        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Value.Should().Be(1);
        });
    }

    #endregion

    #region PM03: 관측성 비활성화 시 연결 성공 메트릭 미기록

    /// <summary>
    /// 연결 성공 메트릭이 DbMetrics 게이트를 따르는지 검증합니다.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PM03_ConnectionMetrics_ShouldHonorDbMetricsGate(bool enabled)
    {
        bool previous = DbMetrics.IsEnabled;
        DbMetrics.IsEnabled = enabled;

        try
        {
            using TelemetryTestHarness harness = new("Lib.Db");

            DbResult<int> result = await _db
                .Sql("SELECT 1")
                .ExecuteScalarAsync<int>();

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(1);

            IReadOnlyList<CapturedMeasurement<double>> acquireMeasurements =
                harness.GetDoubles("libdb.connection.acquire_duration_ms");
            IReadOnlyList<CapturedMeasurement<long>> poolWaits =
                harness.GetLongs("libdb.connection.pool_waits");

            if (enabled)
            {
                Assert.Contains(acquireMeasurements, m =>
                    m.Tags.Any(t => t.Key == "instance" && t.Value is string { Length: > 0 }));
            }
            else
            {
                acquireMeasurements.Should().BeEmpty();
                poolWaits.Should().BeEmpty();
            }
        }
        finally
        {
            DbMetrics.IsEnabled = previous;
        }
    }

    #endregion

    #region PM04: 관측성 비활성화 시 연결 실패 메트릭 미기록

    /// <summary>
    /// 연결 실패 메트릭이 DbMetrics 게이트를 따르는지 검증합니다.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PM04_ConnectionFailureMetrics_ShouldHonorDbMetricsGate(bool enabled)
    {
        bool previous = DbMetrics.IsEnabled;
        DbMetrics.IsEnabled = enabled;

        try
        {
            LibDbOptions options = TestOptionsFactory.CreateValidWithOverrides(o =>
            {
                o.ConnectionStrings["BrokenLocal"] =
                    "Server=127.0.0.1,1;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1";
            });
            Lib.Db.Repository.DbConnectionFactory factory = new(options);

            using TelemetryTestHarness harness = new("Lib.Db");

            Func<Task> act = async () =>
            {
                await using SqlConnection connection = await factory.CreateConnectionAsync(
                    "BrokenLocal",
                    TestContext.Current.CancellationToken);
            };

            await act.Should().ThrowAsync<Exception>();

            IReadOnlyList<CapturedMeasurement<long>> timeouts =
                harness.GetLongs("libdb.connection.pool_timeouts");

            if (enabled)
            {
                Assert.Contains(timeouts, m =>
                    m.Tags.Any(t => t.Key == "instance" && (string?)t.Value == "BrokenLocal") &&
                    m.Tags.Any(t => t.Key == "reason" && t.Value is string { Length: > 0 }));
            }
            else
            {
                timeouts.Should().BeEmpty();
            }
        }
        finally
        {
            DbMetrics.IsEnabled = previous;
        }
    }

    #endregion
}
