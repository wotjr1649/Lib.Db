// ============================================================================
// 파일: Diagnostics/DbDiagnosticsTests.cs
// 설명: DbDiagnostics 단위 테스트 (ExceptionFactory, FastLogger, Metrics)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Diagnostics;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;
using Lib.Db.Core;

namespace Lib.Db.IntegrationTests.Diagnostics;

public sealed class DbDiagnosticsTests
{
    private readonly Mock<ILogger> _mockLogger;

    public DbDiagnosticsTests()
    {
        _mockLogger = new Mock<ILogger>();
        DbMetrics.ResetForTesting();
    }

    [Fact]
    public void DD01_ExceptionFactory_ShouldCreateCorrectExceptions()
    {
        Exception inner = new("Native Error");

        Exception ex = LibDbExceptionFactory.CreateCommandExecutionFailed(inner);

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("오류가 발생했습니다", ex.Message);
        Assert.DoesNotContain("SELECT * FROM", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void DD02_FastLogger_ShouldLog_WhenEnabled()
    {
        _mockLogger.Setup(x => x.IsEnabled(LogLevel.Debug)).Returns(true);
        int value = 123;

        _mockLogger.Object.LogFastDebug($"Test Value: {value}");

        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Test Value: 123")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void DD03_FastLogger_ShouldSkip_WhenDisabled()
    {
        _mockLogger.Setup(x => x.IsEnabled(LogLevel.Debug)).Returns(false);

        _mockLogger.Object.LogFastDebug($"Should Not Log");

        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void DD04_DbMetrics_ShouldRecord_Duration()
    {
        using TelemetryTestHarness harness = new("Lib.Db");

        DbMetrics.TrackDuration(TimeSpan.FromMilliseconds(500));

        IReadOnlyList<CapturedMeasurement<double>> measurements = harness.GetDoubles("db.client.operation.duration");
        Assert.NotEmpty(measurements);
        Assert.Equal(500, measurements.Last().Value);
    }

    [Fact]
    public void DD05_DbMetrics_ShouldFillTags_Correctly()
    {
        using TelemetryTestHarness harness = new("Lib.Db");
        DbRequestInfo info = new(
            InstanceId: "TestInst",
            Operation: "EXEC",
            CommandKind: "StoredProcedure"
        );

        DbMetrics.TrackRetry("Deadlock", in info);

        IReadOnlyList<CapturedMeasurement<int>> measurements = harness.GetInts("db.client.resilience.retries");
        Assert.NotEmpty(measurements);

        CapturedMeasurement<int> captured = Assert.Single(measurements, m =>
            m.Tags.Any(t => t.Key == "libdb.retry.reason" && (string)t.Value! == "Deadlock") &&
            m.Tags.Any(t => t.Key == "libdb.instance.id" && (string)t.Value! == "TestInst"));
        Assert.Equal(1, captured.Value);

        KeyValuePair<string, object?>[] tags = captured.Tags.ToArray();
        Assert.Contains(tags, t => t.Key == "libdb.retry.reason" && (string)t.Value! == "Deadlock");
        Assert.Contains(tags, t => t.Key == "libdb.instance.id" && (string)t.Value! == "TestInst");
        Assert.Contains(tags, t => t.Key == "db.operation" && (string)t.Value! == "EXEC");
        Assert.Contains(tags, t => t.Key == "libdb.command.kind" && (string)t.Value! == "StoredProcedure");

        Assert.DoesNotContain(tags, t => t.Key == "db.name");
    }

    [Fact]
    public void DD06_FromExecutionContext_ShouldNotExposeCommandTextAsTarget()
    {
        DbExecutionContext context = DbExecutionContext.ForCommand(
            "TestInst",
            "SELECT * FROM SensitiveTable WHERE Secret = @secret",
            CommandType.Text);

        DbRequestInfo info = DbRequestInfo.FromExecutionContext(context);

        Assert.Null(info.Target);
        Assert.Equal("Text", info.CommandKind);
    }

    [Fact]
    public void DD07_FromExecutionContext_ShouldRedactRawConnectionStringInstance()
    {
        const string rawInstance = "Raw:InstanceMaterialForDiagnosticsTest;Segment=Zeta";

        DbExecutionContext context = DbExecutionContext.ForCommand(
            rawInstance,
            "dbo.usp_Test",
            CommandType.StoredProcedure);

        DbRequestInfo info = DbRequestInfo.FromExecutionContext(context);

        Assert.Equal("Raw:[redacted]", info.InstanceId);
        Assert.DoesNotContain("InstanceMaterialForDiagnosticsTest", info.InstanceId);
        Assert.DoesNotContain("Segment=Zeta", info.InstanceId);
    }

    [Fact]
    public void DD08_DbMetrics_ShouldRedactRawInstanceTag()
    {
        using TelemetryTestHarness harness = new("Lib.Db");
        DbRequestInfo info = new(
            InstanceId: "Raw:InstanceMaterialForMetricsTest;Segment=Eta",
            Operation: "EXEC",
            CommandKind: "StoredProcedure"
        );

        DbMetrics.TrackRetry("Deadlock", in info);

        CapturedMeasurement<int> captured = Assert.Single(
            harness.GetInts("db.client.resilience.retries"),
            m => m.Tags.Any(t => t.Key == "libdb.retry.reason" && (string)t.Value! == "Deadlock"));

        KeyValuePair<string, object?> instanceTag = Assert.Single(
            captured.Tags,
            t => t.Key == "libdb.instance.id");

        Assert.Equal("Raw:[redacted]", instanceTag.Value);
    }
}
