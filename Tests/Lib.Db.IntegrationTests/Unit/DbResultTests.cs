// ============================================================================
// 파일: Unit/DbResultTests.cs
// 설명: DbResult<T> 단위 테스트 -- 성공/실패 팩토리, Deconstruct, 패턴 매칭 검증
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.IntegrationTests.Unit;

public sealed class DbResultTests
{
    [Fact]
    public void Ok_ShouldSetIsSuccessTrue()
    {
        DbResult<int> result = DbResult<int>.Ok(42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_ShouldSetIsSuccessFalse()
    {
        DbError error = new()
        {
            Kind = DbErrorKind.SchemaNotFound,
            SqlErrorCode = 2812,
            Message = "SP를 찾을 수 없습니다."
        };
        DbResult<int> result = DbResult<int>.Fail(error);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(DbErrorKind.SchemaNotFound, result.Error.Value.Kind);
    }

    [Fact]
    public void Deconstruct_ShouldSupportPatternMatching()
    {
        DbResult<string> result = DbResult<string>.Ok("hello");
        (bool success, string? value, DbError? error) = result;
        Assert.True(success);
        Assert.Equal("hello", value);
        Assert.Null(error);
    }

    [Fact]
    public void PatternMatching_ShouldWorkWithIsExpression()
    {
        DbResult<int> result = DbResult<int>.Ok(99, affectedRows: 5);
        Assert.True(result is { IsSuccess: true, Value: 99, AffectedRows: 5 });
    }

    [Fact]
    public void Ok_WithNullValue_ShouldStillBeSuccess()
    {
        DbResult<string?> result = DbResult<string?>.Ok(null);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Fail_ShouldHaveDefaultValue()
    {
        DbError error = new() { Kind = DbErrorKind.Timeout, Message = "타임아웃" };
        DbResult<int> result = DbResult<int>.Fail(error);
        Assert.Equal(0, result.Value);
        Assert.Equal(0, result.AffectedRows);
    }

    [Fact]
    public void Ok_WithAffectedRows_ShouldSetBoth()
    {
        DbResult<int> result = DbResult<int>.Ok(42, affectedRows: 10);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(10, result.AffectedRows);
    }
}
