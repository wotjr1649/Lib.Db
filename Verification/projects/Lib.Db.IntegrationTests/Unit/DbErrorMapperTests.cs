// ============================================================================
// 파일: Unit/DbErrorMapperTests.cs
// 설명: DbErrorMapper 단위 테스트 -- SqlException 오류 코드 -> DbError 변환 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Diagnostics;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class DbErrorMapperTests
{
    [Theory]
    [InlineData(2812, DbErrorKind.SchemaNotFound, false)]
    [InlineData(208, DbErrorKind.SchemaNotFound, false)]
    [InlineData(207, DbErrorKind.SchemaNotFound, false)]
    [InlineData(1205, DbErrorKind.Deadlock, true)]
    [InlineData(18456, DbErrorKind.AuthenticationFailed, false)]
    [InlineData(10054, DbErrorKind.ConnectionLost, true)]
    [InlineData(2627, DbErrorKind.ConstraintViolation, false)]
    [InlineData(547, DbErrorKind.ConstraintViolation, false)]
    [InlineData(8115, DbErrorKind.DataConversion, false)]
    [InlineData(8152, DbErrorKind.DataConversion, false)]
    [InlineData(229, DbErrorKind.PermissionDenied, false)]
    [InlineData(701, DbErrorKind.ResourceExhausted, true)]
    [InlineData(102, DbErrorKind.QuerySyntax, false)]
    [InlineData(40501, DbErrorKind.CloudTransient, true)]
    [InlineData(-2, DbErrorKind.Timeout, true)]
    [InlineData(201, DbErrorKind.ParameterMismatch, false)]
    [InlineData(3930, DbErrorKind.TransactionAborted, false)]
    public void FromSqlErrorCode_ShouldMapCorrectly(
        int errorCode, DbErrorKind expectedKind, bool expectedTransient)
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(errorCode, "test_object");
        Assert.Equal(expectedKind, error.Kind);
        Assert.Equal(expectedTransient, error.IsTransient);
        Assert.Equal(errorCode, error.SqlErrorCode);
        Assert.False(string.IsNullOrEmpty(error.Message));
    }

    [Fact]
    public void FromSqlErrorCode_UserDefined_ShouldMapAbove50000()
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(50001, "usp_Test");
        Assert.Equal(DbErrorKind.UserDefined, error.Kind);
        Assert.False(error.IsTransient);
        Assert.Equal("usp_Test", error.ObjectName);
    }

    [Fact]
    public void FromSqlErrorCode_Unknown_ShouldReturnUnknown()
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(49999);
        Assert.Equal(DbErrorKind.Unknown, error.Kind);
        Assert.False(error.IsTransient);
    }

    [Fact]
    public void FromSqlErrorCode_WithObjectName_ShouldIncludeInMessage()
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(2812, "usp_GetUser");
        Assert.Contains("usp_GetUser", error.Message);
    }

    [Fact]
    public void FromSqlErrorCode_ShouldNotRetainInnerException()
    {
        var inner = new InvalidOperationException("provider detail");

        DbError error = DbErrorMapper.FromSqlErrorCode(50001, "stored procedure", innerException: inner);

        error.InnerException.Should().BeNull();
        error.Message.Should().NotContain("provider detail");
    }

    [Fact]
    public void FromSqlErrorCode_SchemaNotFound_ShouldHaveHint()
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(2812, "usp_GetUser");
        Assert.NotNull(error.Hint);
        Assert.False(string.IsNullOrEmpty(error.Hint));
    }

    [Fact]
    public void FromSqlErrorCode_Timeout_ShouldHaveHint()
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(-2, "usp_SlowQuery");
        Assert.NotNull(error.Hint);
        Assert.Contains("CommandTimeout", error.Hint);
    }

    [Theory]
    [InlineData(50000)]
    [InlineData(50001)]
    [InlineData(55555)]
    [InlineData(99999)]
    public void FromSqlErrorCode_AllAbove50000_ShouldBeUserDefined(int code)
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(code);
        Assert.Equal(DbErrorKind.UserDefined, error.Kind);
    }
}
