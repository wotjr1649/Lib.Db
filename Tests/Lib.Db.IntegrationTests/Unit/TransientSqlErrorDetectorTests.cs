// ============================================================================
// 파일: Unit/TransientSqlErrorDetectorTests.cs
// 설명: TransientSqlErrorDetector 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Infrastructure.Resilience;

namespace Lib.Db.IntegrationTests.Unit;

[Trait("Category", "Unit")]
public sealed class TransientSqlErrorDetectorTests
{
    [Theory]
    [InlineData(1205)]
    [InlineData(-2)]
    [InlineData(53)]
    [InlineData(233)]
    [InlineData(10053)]
    [InlineData(10054)]
    [InlineData(10060)]
    [InlineData(40613)]
    [InlineData(40197)]
    [InlineData(40501)]
    [InlineData(49918)]
    public void IsTransientError_ShouldReturnTrue_ForKnownCodes(int errorNumber)
    {
        bool result = DefaultTransientSqlErrorDetector.IsTransientError(errorNumber);
        Assert.True(result, $"Error {errorNumber} should be transient.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50000)]
    [InlineData(2627)]
    [InlineData(2601)]
    public void IsTransientError_ShouldReturnFalse_ForUnknownCodes(int errorNumber)
    {
        bool result = DefaultTransientSqlErrorDetector.IsTransientError(errorNumber);
        Assert.False(result, $"Error {errorNumber} should NOT be transient.");
    }

    [Fact]
    public void IsTransient_ShouldReturnTrue_ForTimeoutException()
    {
        DefaultTransientSqlErrorDetector detector = new();
        TimeoutException ex = new();

        bool result = detector.IsTransient(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsTransient_ShouldReturnFalse_ForGeneralException()
    {
        DefaultTransientSqlErrorDetector detector = new();
        Exception ex = new("Generic Error");

        bool result = detector.IsTransient(ex);

        Assert.False(result);
    }
}
