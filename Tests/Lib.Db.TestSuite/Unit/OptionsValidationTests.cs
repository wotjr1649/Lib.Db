using Lib.Db.Core;
using Lib.Db.Verification.Tests.Helpers;
using Xunit;

namespace Lib.Db.Verification.Tests.Unit;

[Trait("Category", "Unit")]
public class OptionsValidationTests
{
    // =========================================================================
    // LibDbOptions Validation Tests
    // =========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(600)]
    [InlineData(30)]
    public void DefaultCommandTimeoutSeconds_ValidRange_ShouldSet(int value)
    {
        var options = TestOptionsFactory.CreateMinimal();
        options.DefaultCommandTimeoutSeconds = value;
        Assert.Equal(value, options.DefaultCommandTimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    [InlineData(-1)]
    public void DefaultCommandTimeoutSeconds_InvalidRange_ShouldThrow(int value)
    {
        var options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DefaultCommandTimeoutSeconds = value);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(100_000)]
    [InlineData(5000)]
    public void BulkBatchSize_ValidRange_ShouldSet(int value)
    {
        var options = TestOptionsFactory.CreateMinimal();
        options.BulkBatchSize = value;
        Assert.Equal(value, options.BulkBatchSize);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100_001)]
    public void BulkBatchSize_InvalidRange_ShouldThrow(int value)
    {
        var options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.BulkBatchSize = value);
    }

    [Fact]
    public void ConnectionStrings_SetNull_ShouldThrow()
    {
        var options = TestOptionsFactory.CreateMinimal();
        // C# property setter validation
        Assert.Throws<ArgumentNullException>(() => options.ConnectionStrings = null!);
    }

    [Fact]
    public void PrewarmSchemas_SetNull_ShouldThrow()
    {
        var options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentNullException>(() => options.PrewarmSchemas = null!);
    }

    // =========================================================================
    // ChaosOptions Validation Tests
    // =========================================================================

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public void Chaos_ExceptionRate_ValidRange_ShouldSet(double value)
    {
        var options = new ChaosOptions();
        options.ExceptionRate = value;
        Assert.Equal(value, options.ExceptionRate);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Chaos_ExceptionRate_InvalidRange_ShouldThrow(double value)
    {
        var options = new ChaosOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ExceptionRate = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60000)]
    public void Chaos_MinLatencyMs_ValidRange_ShouldSet(int value)
    {
        var options = new ChaosOptions();
        options.MinLatencyMs = value;
        Assert.Equal(value, options.MinLatencyMs);
    }

    [Theory]
    [InlineData(-1)]
    public void Chaos_MinLatencyMs_InvalidRange_ShouldThrow(int value)
    {
        var options = new ChaosOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MinLatencyMs = value);
    }

    // =========================================================================
    // ResilienceOptions Validation Tests
    // =========================================================================
    
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void CircuitBreakerFailureRatio_ValidRange_ShouldSet(double value)
    {
        var options = TestOptionsFactory.CreateMinimal();
        options.Resilience.CircuitBreakerFailureRatio = value;
        Assert.Equal(value, options.Resilience.CircuitBreakerFailureRatio);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]

    public void CircuitBreakerFailureRatio_InvalidRange_ShouldThrow(double value)
    {
        // ResilienceOptions now has strict setter validation
        var options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Resilience.CircuitBreakerFailureRatio = value);
    }
}
