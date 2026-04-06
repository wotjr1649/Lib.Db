// ============================================================================
// 파일: Unit/ResilienceOptionsValidationTests.cs
// 설명: ResilienceOptions 검증 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Core;
using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.Unit;

[Trait("Category", "Unit")]
public sealed class ResilienceOptionsValidationTests
{
    #region LibDbOptions Nested Resilience Properties

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void CircuitBreakerFailureRatio_ValidRange_ShouldSet(double value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.Resilience.CircuitBreakerFailureRatio = value;
        Assert.Equal(value, options.Resilience.CircuitBreakerFailureRatio);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void CircuitBreakerFailureRatio_InvalidRange_ShouldThrow(double value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Resilience.CircuitBreakerFailureRatio = value);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(60000)]
    public void CircuitBreakerBreakDurationMs_ValidRange_ShouldSet(int value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.Resilience.CircuitBreakerBreakDurationMs = value;
        Assert.Equal(value, options.Resilience.CircuitBreakerBreakDurationMs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CircuitBreakerBreakDurationMs_InvalidRange_ShouldThrow(int value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Resilience.CircuitBreakerBreakDurationMs = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void MaxRetryCount_ValidRange_ShouldSet(int value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.Resilience.MaxRetryCount = value;
        Assert.Equal(value, options.Resilience.MaxRetryCount);
    }

    [Theory]
    [InlineData(-1)]
    public void MaxRetryCount_InvalidRange_ShouldThrow(int value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Resilience.MaxRetryCount = value);
    }

    #endregion

    #region ResilienceOptions Defaults

    [Fact]
    public void ResilienceOptions_Defaults_ShouldBeCorrect()
    {
        LibDbOptions.ResilienceOptions options = new();

        Assert.Equal(3, options.MaxRetryCount);
        Assert.Equal(100, options.BaseRetryDelayMs);
        Assert.Equal(2000, options.MaxRetryDelayMs);
        Assert.True(options.UseRetryJitter);
        Assert.Equal(LibDbOptions.RetryBackoffType.Exponential, options.RetryBackoffType);

        Assert.Equal(5, options.CircuitBreakerThreshold);
        Assert.Equal(30000, options.CircuitBreakerSamplingDurationMs);
        Assert.Equal(30000, options.CircuitBreakerBreakDurationMs);
        Assert.Equal(0.5, options.CircuitBreakerFailureRatio);
    }

    #endregion
}
