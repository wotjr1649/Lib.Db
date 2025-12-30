using Lib.Db.Diagnostics;
using Lib.Db.Verification.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;

namespace Lib.Db.Verification.Tests.Diagnostics;

public class DbDiagnosticsTests
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
        // Arrange
        var inner = new Exception("Native Error");
        var cmdText = "SELECT * FROM Users";

        // Act
        var ex = LibDbExceptionFactory.CreateCommandExecutionFailed(cmdText, inner);

        // Assert
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("오류가 발생했습니다", ex.Message);
        Assert.Contains("SELECT * FROM", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void DD02_FastLogger_ShouldLog_WhenEnabled()
    {
        // Arrange
        _mockLogger.Setup(x => x.IsEnabled(LogLevel.Debug)).Returns(true);
        int value = 123;

        // Act
        // This invokes the InterpolatedStringHandler logic
        _mockLogger.Object.LogFastDebug($"Test Value: {value}");

        // Assert
        // Verify Log was called with Debug level and formatted message
        _mockLogger.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Test Value: 123")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
            Times.Once);
    }

    [Fact]
    public void DD03_FastLogger_ShouldSkip_WhenDisabled()
    {
        // Arrange
        _mockLogger.Setup(x => x.IsEnabled(LogLevel.Debug)).Returns(false);

        // Act
        _mockLogger.Object.LogFastDebug($"Should Not Log");

        // Assert
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
        // Arrange
        using var harness = new TelemetryTestHarness("Lib.Db");

        // Act
        DbMetrics.TrackDuration(TimeSpan.FromMilliseconds(500));

        // Allow explicit small delay for listener callback (though usually synchronous in test harness if using MeterListener correctly)
        // MeterListener callbacks are sync.

        // Assert
        var measurements = harness.GetDoubles("db.client.operation.duration");
        Assert.NotEmpty(measurements);
        Assert.Equal(500, measurements.Last().Value);
    }

    [Fact]
    public void DD05_DbMetrics_ShouldFillTags_Correctly()
    {
        // Arrange
        using var harness = new TelemetryTestHarness("Lib.Db");
        var info = new DbRequestInfo(
            InstanceId: "TestInst",
            Operation: "EXEC",
            CommandKind: "StoredProcedure"
        );

        // Act
        DbMetrics.TrackRetry("Deadlock", in info);

        // Assert
        var measurements = harness.GetInts("db.client.resilience.retries");
        Assert.NotEmpty(measurements);
        
        var last = measurements.Last();
        Assert.Equal(1, last.Value); // Counter increments by 1
        
        var tags = last.Tags.ToArray();
        Assert.Contains(tags, t => t.Key == "libdb.retry.reason" && (string)t.Value == "Deadlock");
        Assert.Contains(tags, t => t.Key == "libdb.instance.id" && (string)t.Value == "TestInst");
        Assert.Contains(tags, t => t.Key == "db.operation" && (string)t.Value == "EXEC");
        Assert.Contains(tags, t => t.Key == "libdb.command.kind" && (string)t.Value == "StoredProcedure");
        
        // Null property (DbName) should NOT be present
        Assert.DoesNotContain(tags, t => t.Key == "db.name");
    }
}
