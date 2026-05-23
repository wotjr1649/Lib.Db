// ============================================================================
// 파일: VerificationDb/OutputParameterTests.cs
// 설명: OUTPUT 파라미터 + WithTimeout+QueryAsync 조합 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// OUTPUT 파라미터 값 검증과 WithTimeout+QueryAsync 조합의 동작을 검증하는 테스트.
/// <para><b>[설계 의도]</b> adv.usp_Adv_OutputParameters의 계산 결과를
/// FormattableString SQL로 직접 SELECT하여 OUTPUT 값을 검증하고,
/// WithTimeout과 QueryAsync 체이닝이 올바르게 동작하는지 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class OutputParameterTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region OP01: OUTPUT 파라미터 — 계산 결과 검증

    /// <summary>
    /// adv.usp_Adv_OutputParameters SP를 FormattableString SQL로 호출하여
    /// @OutputVal = @InputVal * 2, @InOutVal = @InOutVal + @InputVal 결과를 검증한다.
    /// </summary>
    [Fact]
    public async Task OP01_Output_MultipleParams_ShouldReturnCalculatedValues()
    {
        // Arrange
        int inputVal = 10;
        int inOutVal = 5;

        // Act — FormattableString SQL로 OUTPUT 파라미터 값을 SELECT로 반환
        DbResult<Dictionary<string, object?>?> result = await _db
            .Sql((FormattableString)$"DECLARE @out INT, @inout INT = {inOutVal}; EXEC adv.usp_Adv_OutputParameters @InputVal = {inputVal}, @OutputVal = @out OUTPUT, @InOutVal = @inout OUTPUT; SELECT @out AS OutputVal, @inout AS InOutVal;")
            .QuerySingleAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        Convert.ToInt32(result.Value!["OutputVal"]).Should().Be(20, "OutputVal = InputVal(10) * 2 = 20");
        Convert.ToInt32(result.Value!["InOutVal"]).Should().Be(15, "InOutVal = InOutVal(5) + InputVal(10) = 15");
    }

    #endregion

    #region OP02: WithTimeout + QueryAsync — 정상 완료

    /// <summary>
    /// resilience.usp_Resilience_Simulate_Delay SP를 WithTimeout(10)과 QueryAsync로 호출하여
    /// 1초 지연이 정상 완료되고 스트리밍 결과를 열거할 수 있는지 검증한다.
    /// </summary>
    [Fact]
    public async Task OP02_WithTimeout_QueryAsync_ShouldSucceed()
    {
        // Act — 1초 지연, 10초 타임아웃, QueryAsync
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("resilience.usp_Resilience_Simulate_Delay")
            .WithTimeout(10)
            .With(new { DelaySeconds = 1 })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert — 성공하고 결과 열거 가능
        result.IsSuccess.Should().BeTrue();

        int count = 0;
        await foreach (Dictionary<string, object?> row in result.Value!)
            count++;
        count.Should().BeGreaterThan(0, "1초 지연 SP는 결과를 반환해야 합니다.");
    }

    #endregion
}
