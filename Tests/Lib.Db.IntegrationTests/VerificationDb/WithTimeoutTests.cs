// ============================================================================
// 파일: VerificationDb/WithTimeoutTests.cs
// 설명: WithTimeout() Fluent API 체이닝 — 타임아웃 발생/미발생 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// IParameterStage.WithTimeout(int) 체이닝으로 명령 실행 타임아웃을 제어하는 테스트.
/// <para><b>[설계 의도]</b> resilience.usp_Resilience_Simulate_Delay SP로
/// WAITFOR DELAY를 시뮬레이션하여, 짧은 타임아웃은 DbErrorKind.Timeout을 반환하고
/// 충분한 타임아웃은 정상 완료하는지 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class WithTimeoutTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region WT01: 짧은 타임아웃 → Timeout 에러

    /// <summary>
    /// 10초 지연 SP에 2초 타임아웃을 설정하면 DbErrorKind.Timeout이 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task WT01_WithTimeout_ShortTimeout_ShouldTimeout()
    {
        // Act — 10초 지연, 2초 타임아웃
        DbResult<int> result = await _db
            .Procedure("resilience.usp_Resilience_Simulate_Delay")
            .WithTimeout(2)
            .With(new { DelaySeconds = 10 })
            .ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.Timeout,
            "2초 타임아웃에 10초 지연이면 타임아웃이 발생해야 합니다.");
    }

    #endregion

    #region WT02: 충분한 타임아웃 → 성공

    /// <summary>
    /// 1초 지연 SP에 10초 타임아웃을 설정하면 정상 완료되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task WT02_WithTimeout_LongTimeout_ShouldSucceed()
    {
        // Act — 1초 지연, 10초 타임아웃
        DbResult<int> result = await _db
            .Procedure("resilience.usp_Resilience_Simulate_Delay")
            .WithTimeout(10)
            .With(new { DelaySeconds = 1 })
            .ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue("1초 지연에 10초 타임아웃이면 충분히 완료되어야 합니다.");
    }

    #endregion
}
