// ============================================================================
// 파일: VerificationDb/ErrorHandlingTests.cs
// 설명: DbResult 에러 시나리오 테스트 (FK위반, 0나누기, 없는SP/테이블, 유니크위반)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class ErrorHandlingTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _db = fixture.Verification;

    [Fact]
    public async Task V09_ForeignKeyViolation_ReturnsConstraintViolation()
    {
        DbResult<int> result = await _db
            .Procedure("exception.usp_Exception_ForeignKeyViolation")
            .With(new { NonExistentParentId = 99999 })
            .ExecuteAsync();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.ConstraintViolation);
    }

    [Fact]
    public async Task V10_DivideByZero_ReturnsDataConversion()
    {
        DbResult<int> result = await _db
            .Procedure("exception.usp_Exception_DivideByZero")
            .ExecuteAsync();
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.DataConversion);
    }

    [Fact]
    public async Task E01_NonExistentSP_ReturnsFailure()
    {
        DbResult<int> result = await _db
            .Procedure("dbo.usp_NonExistent_QA_Test_12345")
            .ExecuteAsync();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        // 스키마 캐시 경로에 따라 SchemaNotFound 또는 Unknown이 될 수 있음
        result.Error!.Value.Kind.Should().BeOneOf(
            DbErrorKind.SchemaNotFound, DbErrorKind.Unknown);
    }

    [Fact]
    public async Task E02_NonExistentTable_ReturnsSchemaNotFound()
    {
        // QueryAsync는 스트림을 즉시 반환하므로, 반복(iterate)해야 SQL 오류가 발생
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Sql("SELECT * FROM dbo.QA_NonExistent_Table_99999")
            .QueryAsync<Dictionary<string, object?>>();

        if (result.IsSuccess)
        {
            // 스트림 생성은 성공했으나 반복 시 오류 발생 예상
            Func<Task> iterateAction = async () =>
            {
                await foreach (Dictionary<string, object?> _ in result.Value!)
                {
                    // 반복 중 SqlException 발생 예상
                }
            };
            await iterateAction.Should().ThrowAsync<Exception>();
        }
        else
        {
            // 스트림 생성 시점에서 바로 오류가 반환된 경우
            result.Error!.Value.Kind.Should().Be(DbErrorKind.SchemaNotFound);
        }
    }

    [Fact]
    public async Task E03_UniqueViolation_ReturnsConstraintViolation()
    {
        DbResult<int> result = await _db
            .Procedure("exception.usp_Exception_UniqueViolation")
            .With(new { DuplicateValue = "DUPLICATE_TEST" })
            .ExecuteAsync();
        // First call might succeed, second should fail
        DbResult<int> result2 = await _db
            .Procedure("exception.usp_Exception_UniqueViolation")
            .With(new { DuplicateValue = "DUPLICATE_TEST" })
            .ExecuteAsync();
        result2.IsSuccess.Should().BeFalse();
        result2.Error!.Value.Kind.Should().Be(DbErrorKind.ConstraintViolation);
    }

    [Fact]
    public async Task E04_InvalidObjectName_ReturnsSchemaNotFound()
    {
        DbResult<int> result = await _db
            .Procedure("exception.usp_Exception_InvalidObjectName")
            .ExecuteAsync();
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.SchemaNotFound);
    }
}
