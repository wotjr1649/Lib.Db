// ============================================================================
// 파일: Diagnostics/DbDiagnosticsTests.cs
// 설명: DbDiagnostics 단위 테스트 (ExceptionFactory, FastLogger, Metrics)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Diagnostics;
using Lib.Db.Configuration;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;

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
        string cmdText = "SELECT * FROM Users";

        Exception ex = LibDbExceptionFactory.CreateCommandExecutionFailed(cmdText, inner);

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("오류가 발생했습니다", ex.Message);
        Assert.Contains("SELECT * FROM", ex.Message);
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

        CapturedMeasurement<int> last = measurements.Last();
        Assert.Equal(1, last.Value);

        KeyValuePair<string, object?>[] tags = last.Tags.ToArray();
        Assert.Contains(tags, t => t.Key == "libdb.retry.reason" && (string)t.Value! == "Deadlock");
        Assert.Contains(tags, t => t.Key == "libdb.instance.id" && (string)t.Value! == "TestInst");
        Assert.Contains(tags, t => t.Key == "db.operation" && (string)t.Value! == "EXEC");
        Assert.Contains(tags, t => t.Key == "libdb.command.kind" && (string)t.Value! == "StoredProcedure");

        Assert.DoesNotContain(tags, t => t.Key == "db.name");
    }

    [Fact]
    public void DD06_CommandTextPolicy_ShouldSuppressRawSqlText_ByDefault()
    {
        const string sql = "SELECT * FROM Users WHERE Email = 'secret@example.com'";
        LibDbOptions options = new();

        DbCommandTextDiagnostic diagnostic = DbCommandTextPolicy.CreateDiagnostic(sql, CommandType.Text, options);

        Assert.Equal("SQL Text", diagnostic.Summary);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Hash));
        Assert.Null(diagnostic.SensitiveText);
        Assert.DoesNotContain("secret@example.com", diagnostic.Summary);
        Assert.DoesNotContain("secret@example.com", diagnostic.Hash);

        IReadOnlyList<KeyValuePair<string, object?>> tags =
            DbCommandTextPolicy.BuildActivityTags(sql, CommandType.Text, "test-instance", options);

        Assert.Contains(tags, t => t.Key == "db.query.summary" && (string)t.Value! == "SQL Text");
        Assert.Contains(tags, t => t.Key == "libdb.command.hash" && !string.IsNullOrWhiteSpace((string?)t.Value));
        Assert.DoesNotContain(tags, t => t.Key == "db.statement");
        Assert.DoesNotContain(tags, t => t.Key == "db.query.text");
        Assert.DoesNotContain(tags, t => t.Value is string value && value.Contains("secret@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public void DD07_CommandTextPolicy_ShouldExposeBoundedRawText_OnlyWhenOptedIn()
    {
        const string sql = "SELECT * FROM Users WHERE Email = 'secret@example.com'";
        LibDbOptions options = new()
        {
            EnableSensitiveCommandTextLogging = true,
            MaxSensitiveCommandTextLength = 17
        };

        DbCommandTextDiagnostic diagnostic = DbCommandTextPolicy.CreateDiagnostic(sql, CommandType.Text, options);

        Assert.Equal("SELECT * FROM Use...", diagnostic.SensitiveText);

        IReadOnlyList<KeyValuePair<string, object?>> tags =
            DbCommandTextPolicy.BuildActivityTags(sql, CommandType.Text, "test-instance", options);

        Assert.Contains(tags, t => t.Key == "db.statement" && (string)t.Value! == "SELECT * FROM Use...");
        Assert.Contains(tags, t => t.Key == "db.query.text" && (string)t.Value! == "SELECT * FROM Use...");
    }
}
