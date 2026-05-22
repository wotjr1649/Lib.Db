// ============================================================================
// 파일: VerificationDb/JsonMappingTests.cs
// 설명: JSON 자동 매핑 확장 메서드 검증 테스트 2개
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Extensions;
using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// JSON 컬럼 자동 매핑 확장 메서드 검증 테스트.
/// <para><b>[설계 의도]</b> gap.usp_Json_Insert/Query를 통해
/// JSON 삽입 후 MapJsonColumn 확장 메서드로 역직렬화를 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class JsonMappingTests(MultiDbFixture fixture, ITestOutputHelper output)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;
    private readonly ITestOutputHelper _output = output;

    #endregion

    #region JM01: JSON 삽입 및 역직렬화

    /// <summary>
    /// JSON 데이터를 삽입한 후 MapJsonColumn으로 역직렬화하여 검증한다.
    /// </summary>
    [Fact]
    public async Task JM01_JsonColumn_InsertAndDeserialize_ShouldWork()
    {
        // Arrange
        JsonTestPayload original = new() { Name = "JsonTest", Score = 95, Active = true };
        string jsonPayload = original.ToJson();

        DbResult<int> insertResult = await _db
            .Procedure("gap.usp_Json_Insert")
            .With(new { JsonPayload = jsonPayload })
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        insertResult.IsSuccess.Should().BeTrue("JSON 삽입이 성공해야 합니다.");
        int newId = insertResult.Value;
        newId.Should().BeGreaterThan(0);

        // Act — Dictionary로 조회 후 MapJsonColumn으로 역직렬화
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> queryResult = await _db
            .Procedure("gap.usp_Json_Query")
            .With(new { Key = "name" })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        queryResult.IsSuccess.Should().BeTrue("JSON 쿼리가 성공해야 합니다.");
        List<Dictionary<string, object?>> rows = await queryResult.Value!.ToListAsync(TestContext.Current.CancellationToken);

        // 삽입한 행 찾기
        Dictionary<string, object?>? targetRow = rows.FirstOrDefault(r =>
            r.TryGetValue("Id", out object? id) && Convert.ToInt32(id) == newId);

        targetRow.Should().NotBeNull("삽입한 행이 조회되어야 합니다.");

        // MapJsonColumn으로 역직렬화
        JsonTestPayload? deserialized = targetRow!.MapJsonColumn<JsonTestPayload>("Payload");

        // Assert
        deserialized.Should().NotBeNull("JSON 역직렬화가 성공해야 합니다.");
        deserialized!.Name.Should().Be("JsonTest");
        deserialized.Score.Should().Be(95);
        deserialized.Active.Should().BeTrue();

        _output.WriteLine($"=== JM01: JSON 역직렬화 성공 ===");
        _output.WriteLine($"원본: {jsonPayload}");
        _output.WriteLine($"역직렬화: Name={deserialized.Name}, Score={deserialized.Score}, Active={deserialized.Active}");

        // Cleanup
        await _db
            .Sql($"DELETE FROM [gap].[JsonData] WHERE Id = {newId}")
            .ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region JM02: NULL JSON 값

    /// <summary>
    /// NULL JSON 값에 대해 MapJsonColumn이 default(T)를 반환하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task JM02_JsonColumn_NullValue_ShouldReturnDefault()
    {
        // Arrange — MapJsonColumn에 존재하지 않는 컬럼명 사용
        Dictionary<string, object?> row = new()
        {
            ["Id"] = 1,
            ["Payload"] = null  // null 값
        };

        // Act
        JsonTestPayload? result = row.MapJsonColumn<JsonTestPayload>("Payload");

        // Assert
        result.Should().BeNull("NULL JSON은 default(T)를 반환해야 합니다.");

        // 존재하지 않는 컬럼
        JsonTestPayload? missing = row.MapJsonColumn<JsonTestPayload>("NonExistentColumn");
        missing.Should().BeNull("존재하지 않는 컬럼은 default(T)를 반환해야 합니다.");

        _output.WriteLine("=== JM02: NULL/Missing JSON → default(T) 확인 완료 ===");
    }

    #endregion
}

#region JSON 테스트용 DTO

/// <summary>
/// JSON 매핑 테스트용 DTO.
/// </summary>
public sealed class JsonTestPayload
{
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public bool Active { get; set; }
}

#endregion
