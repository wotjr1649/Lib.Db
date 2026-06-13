// ============================================================================
// 파일: VerificationDb/OutputParameterTests.cs
// 설명: OUTPUT 파라미터 + WithTimeout+QueryAsync 조합 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.Contracts.Execution;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

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

    #region OP02: Procedure + SqlParameter OUTPUT — 호출자 파라미터 참조 갱신

    /// <summary>
    /// Lib.Db Procedure API에 명시적 SqlParameter OUTPUT/INPUTOUTPUT 파라미터를 전달하면,
    /// SP 실행 후 동일 파라미터 객체에 계산 결과가 반영되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task OP02_Procedure_WithExplicitSqlParameterOutput_ShouldPopulateValues()
    {
        // Arrange
        AdvancedOutputParameters output = CreateAdvancedOutputParameters();

        // Act
        DbResult<int> result = await _db
            .Procedure("adv.usp_Adv_OutputParameters")
            .With(new
            {
                InputVal = 10,
                output.OutputVal,
                output.InOutVal,
                output.ReturnValue
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        AssertAdvancedOutputValues(output);
    }

    #endregion

    #region OP03: Procedure + SqlParameter NVARCHAR OUTPUT — 문자열 출력값 갱신

    /// <summary>
    /// NVARCHAR OUTPUT 파라미터가 필요한 SP에서도 명시적 SqlParameter 참조가 실행 후 갱신되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task OP03_Procedure_WithExplicitStringOutput_ShouldPopulateValues()
    {
        // Arrange
        var outputName = new SqlParameter("@OutputName", SqlDbType.NVarChar, 100)
        {
            Direction = ParameterDirection.Output
        };
        var outputAge = new SqlParameter("@OutputAge", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };

        // Act
        DbResult<int> result = await _db
            .Procedure("test.usp_Output_With_Error")
            .With(new
            {
                InputId = 1,
                OutputName = outputName,
                OutputAge = outputAge
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        outputName.Value.Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
        Convert.ToInt32(outputAge.Value).Should().BeGreaterThan(0);
    }

    #endregion

    #region OP04: Dictionary + SqlParameter OUTPUT — 동적 파라미터 출력값 갱신

    /// <summary>
    /// Dictionary 파라미터에서도 명시적 SqlParameter OUTPUT/INPUTOUTPUT 값이 정상 반영되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task OP04_Dictionary_WithExplicitSqlParameterOutput_ShouldPopulateValues()
    {
        // Arrange
        AdvancedOutputParameters output = CreateAdvancedOutputParameters();
        var parameters = new Dictionary<string, object?>
        {
            ["InputVal"] = 10,
            ["OutputVal"] = output.OutputVal,
            ["InOutVal"] = output.InOutVal,
            ["ReturnValue"] = output.ReturnValue
        };

        // Act
        DbResult<int> result = await _db
            .Procedure("adv.usp_Adv_OutputParameters")
            .With(parameters)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        AssertAdvancedOutputValues(output);
        parameters["OutputVal"].Should().Be(20);
        parameters["InOutVal"].Should().Be(15);
        parameters["ReturnValue"].Should().BeSameAs(output.ReturnValue);
    }

    #endregion

    #region OP05: Procedure + QuerySingleAsync — OUTPUT 역매핑

    /// <summary>
    /// 결과 행을 반환하지 않는 저장 프로시저를 QuerySingleAsync로 호출해도
    /// reader가 닫힌 뒤 OUTPUT/RETURN_VALUE가 호출자 SqlParameter에 역매핑되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task OP05_QuerySingle_WithExplicitSqlParameterOutput_ShouldPopulateValues()
    {
        // Arrange
        AdvancedOutputParameters output = CreateAdvancedOutputParameters();

        // Act
        DbResult<Dictionary<string, object?>?> result = await _db
            .Procedure("adv.usp_Adv_OutputParameters")
            .With(new
            {
                InputVal = 10,
                output.OutputVal,
                output.InOutVal,
                output.ReturnValue
            })
            .QuerySingleAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        AssertAdvancedOutputValues(output);
    }

    #endregion

    #region OP06: Procedure + ExecuteScalarAsync — OUTPUT 역매핑

    /// <summary>
    /// 결과 스칼라를 반환하지 않는 저장 프로시저를 ExecuteScalarAsync로 호출해도
    /// command 완료 후 OUTPUT/RETURN_VALUE가 호출자 SqlParameter에 역매핑되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task OP06_ExecuteScalar_WithExplicitSqlParameterOutput_ShouldPopulateValues()
    {
        // Arrange
        AdvancedOutputParameters output = CreateAdvancedOutputParameters();

        // Act
        DbResult<int?> result = await _db
            .Procedure("adv.usp_Adv_OutputParameters")
            .With(new
            {
                InputVal = 10,
                output.OutputVal,
                output.InOutVal,
                output.ReturnValue
            })
            .ExecuteScalarAsync<int?>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        AssertAdvancedOutputValues(output);
    }

    #endregion

    #region OP07: WithTimeout + QueryAsync — 정상 완료

    /// <summary>
    /// resilience.usp_Resilience_Simulate_Delay SP를 WithTimeout(10)과 QueryAsync로 호출하여
    /// 1초 지연이 정상 완료되고 스트리밍 결과를 열거할 수 있는지 검증한다.
    /// </summary>
    [Fact]
    public async Task OP07_WithTimeout_QueryAsync_ShouldSucceed()
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

    #region OP08: Procedure + QueryAsync full enumeration — OUTPUT 역매핑

    /// <summary>
    /// 스트리밍 QueryAsync는 enumerable 반환 시점에는 OUTPUT을 갱신하지 않고,
    /// 전체 열거로 reader가 닫힌 뒤 OUTPUT/RETURN_VALUE를 역매핑한다.
    /// </summary>
    [Fact]
    public async Task OP08_QueryAsync_FullEnumeration_WithExplicitSqlParameterOutput_ShouldPopulateValues()
    {
        // Arrange
        AdvancedOutputParameters output = CreateAdvancedOutputParameters();

        // Act
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("adv.usp_Adv_OutputParameters")
            .With(new
            {
                InputVal = 10,
                output.OutputVal,
                output.InOutVal,
                output.ReturnValue
            })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        AssertAdvancedOutputValuesNotPopulated(output);

        await foreach (Dictionary<string, object?> _ in result.Value!)
        {
        }

        AssertAdvancedOutputValues(output);
    }

    #endregion

    #region OP09: Procedure + QueryAsync early dispose — OUTPUT 역매핑

    /// <summary>
    /// 스트리밍 QueryAsync를 정상적으로 조기 중단해도 enumerator dispose가 reader를 닫고
    /// OUTPUT/RETURN_VALUE를 역매핑한다.
    /// </summary>
    [Fact]
    public async Task OP09_QueryAsync_EarlyDispose_WithExplicitSqlParameterOutput_ShouldPopulateValues()
    {
        // Arrange
        var status = new SqlParameter("@Status", SqlDbType.NVarChar, 20)
        {
            Direction = ParameterDirection.Output
        };

        // Act
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("test.usp_Status_Branch_Logic")
            .With(new
            {
                UserId = 1,
                Status = status
            })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        status.Value.Should().BeNull();

        int rowsRead = 0;
        await foreach (Dictionary<string, object?> row in result.Value!)
        {
            rowsRead++;
            row["Status"].Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
            break;
        }

        rowsRead.Should().Be(1);
        status.Value.Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region OP10: Procedure + QueryMultipleAsync dispose — OUTPUT 역매핑

    /// <summary>
    /// QueryMultipleAsync는 reader 반환 시점에는 OUTPUT을 갱신하지 않고,
    /// IMultipleResultReader.DisposeAsync 성공 후 OUTPUT/RETURN_VALUE를 역매핑한다.
    /// </summary>
    [Fact]
    public async Task OP10_QueryMultiple_Dispose_WithExplicitSqlParameterOutput_ShouldPopulateValues()
    {
        // Arrange
        AdvancedOutputParameters output = CreateAdvancedOutputParameters();

        // Act
        DbResult<IMultipleResultReader> result = await _db
            .Procedure("adv.usp_Adv_OutputParameters")
            .With(new
            {
                InputVal = 10,
                output.OutputVal,
                output.InOutVal,
                output.ReturnValue
            })
            .QueryMultipleAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        AssertAdvancedOutputValuesNotPopulated(output);

        await result.Value!.DisposeAsync();

        AssertAdvancedOutputValues(output);
    }

    #endregion

    private static AdvancedOutputParameters CreateAdvancedOutputParameters()
        => new(
            new SqlParameter("@OutputVal", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            },
            new SqlParameter("@InOutVal", SqlDbType.Int)
            {
                Direction = ParameterDirection.InputOutput,
                Value = 5
            },
            new SqlParameter("@ReturnValue", SqlDbType.Int)
            {
                Direction = ParameterDirection.ReturnValue
            });

    private static void AssertAdvancedOutputValuesNotPopulated(AdvancedOutputParameters output)
    {
        output.OutputVal.Value.Should().BeNull();
        Convert.ToInt32(output.InOutVal.Value).Should().Be(5);
        output.ReturnValue.Value.Should().BeNull();
    }

    private static void AssertAdvancedOutputValues(AdvancedOutputParameters output)
    {
        Convert.ToInt32(output.OutputVal.Value).Should().Be(20, "OutputVal = InputVal(10) * 2 = 20");
        Convert.ToInt32(output.InOutVal.Value).Should().Be(15, "InOutVal = InOutVal(5) + InputVal(10) = 15");
        Convert.ToInt32(output.ReturnValue.Value).Should().Be(10, "ReturnValue = InputVal(10)");
    }

    private sealed record AdvancedOutputParameters(
        SqlParameter OutputVal,
        SqlParameter InOutVal,
        SqlParameter ReturnValue);
}
