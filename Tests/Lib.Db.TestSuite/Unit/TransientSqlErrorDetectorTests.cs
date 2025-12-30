using Lib.Db.Infrastructure.Resilience;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Lib.Db.Verification.Tests.Unit;

[Trait("Category", "Unit")]
public class TransientSqlErrorDetectorTests
{
    [Theory]
    [InlineData(1205)]       // Deadlock
    [InlineData(-2)]         // Timeout
    [InlineData(53)]         // Network
    [InlineData(233)]        // Transport
    [InlineData(10053)]      // Transport
    [InlineData(10054)]      // Transport
    [InlineData(10060)]      // Network Timeout
    [InlineData(40613)]      // Azure Throttling
    [InlineData(40197)]      // Azure Processing
    [InlineData(40501)]      // Azure Busy
    [InlineData(49918)]      // Azure Processing
    public void IsTransientError_ShouldReturnTrue_ForKnownCodes(int errorNumber)
    {
        // Act
        // Accessing internal static method via direct call (InternalsVisibleTo enabled)
        var result = DefaultTransientSqlErrorDetector.IsTransientError(errorNumber);

        // Assert
        Assert.True(result, $"Error {errorNumber} should be transient.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50000)] // Custom/User Error
    [InlineData(2627)]  // PK Violation (Not Transient)
    [InlineData(2601)]  // Unique Index Violation (Not Transient)
    public void IsTransientError_ShouldReturnFalse_ForUnknownCodes(int errorNumber)
    {
        // Act
        var result = DefaultTransientSqlErrorDetector.IsTransientError(errorNumber);

        // Assert
        Assert.False(result, $"Error {errorNumber} should NOT be transient.");
    }

    [Fact]
    public void IsTransient_ShouldReturnTrue_ForTimeoutException()
    {
        // Arrange
        var detector = new DefaultTransientSqlErrorDetector();
        var ex = new TimeoutException();

        // Act
        var result = detector.IsTransient(ex);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTransient_ShouldReturnFalse_ForGeneralException()
    {
        // Arrange
        var detector = new DefaultTransientSqlErrorDetector();
        var ex = new Exception("Generic Error");

        // Act
        var result = detector.IsTransient(ex);

        // Assert
        Assert.False(result);
    }
}
