// ============================================================================
// 파일: VerificationDb/InterceptorTests.cs
// 설명: 사용자 수준 쿼리 인터셉터 API 검증 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

#region 테스트용 인터셉터 구현

/// <summary>
/// 테스트용 인터셉터 — 호출된 명령을 기록합니다.
/// </summary>
internal sealed class TestInterceptor : IDbInterceptor
{
    public List<string> ExecutingCommands { get; } = [];
    public List<string> ExecutedCommands { get; } = [];
    public List<string> ErrorCommands { get; } = [];

    public ValueTask<DbInterceptionResult> OnExecutingAsync(DbInterceptionContext context, CancellationToken ct)
    {
        ExecutingCommands.Add(context.CommandText);
        return ValueTask.FromResult(DbInterceptionResult.Continue);
    }

    public ValueTask OnExecutedAsync(DbInterceptionContext context, CancellationToken ct)
    {
        ExecutedCommands.Add(context.CommandText);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(DbInterceptionContext context, CancellationToken ct)
    {
        ErrorCommands.Add(context.CommandText);
        return ValueTask.CompletedTask;
    }
}

#endregion

/// <summary>
/// IDbInterceptor API의 동작을 검증하는 테스트입니다.
/// <para><b>[설계 의도]</b> 인터셉터 미등록 시 기존 동작 유지 + 컨텍스트 필드 검증 + 인터셉터 호출 검증</para>
/// </summary>
[Collection("MultiDb")]
public sealed class InterceptorTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region IC01: 인터셉터 미등록 — 기존 실행에 영향 없음

    /// <summary>
    /// 인터셉터가 등록되지 않은 상태에서 기존 쿼리가 정상 동작하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task IC01_Interceptor_NotRegistered_ShouldNotAffectExecution()
    {
        // Act — 인터셉터 없이 기존 fixture 사용
        DbResult<int> result = await _db
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    #endregion

    #region IC02: DbInterceptionContext 필드 검증 (유닛)

    /// <summary>
    /// DbInterceptionContext를 직접 생성하여 필드가 올바르게 설정되는지 검증합니다.
    /// </summary>
    [Fact]
    public void IC02_InterceptionContext_ShouldPopulateFields()
    {
        // Arrange & Act
        DbInterceptionContext context = new()
        {
            CommandText = "dbo.usp_Test",
            CommandType = CommandType.StoredProcedure,
            InstanceName = "Verification"
        };

        // Assert
        context.CommandText.Should().Be("dbo.usp_Test");
        context.CommandType.Should().Be(CommandType.StoredProcedure);
        context.InstanceName.Should().Be("Verification");
        context.StartTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        context.ElapsedMs.Should().BeNull();
        context.Result.Should().BeNull();
        context.Exception.Should().BeNull();
        context.State.Should().NotBeNull().And.BeEmpty();
    }

    #endregion

    #region IC03: TestInterceptor — 명령 기록 검증 (유닛)

    /// <summary>
    /// TestInterceptor가 OnExecuting/OnExecuted 호출 시 명령을 올바르게 기록하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task IC03_TestInterceptor_ShouldRecordCommands()
    {
        // Arrange
        TestInterceptor interceptor = new();
        DbInterceptionContext context = new()
        {
            CommandText = "SELECT 1",
            CommandType = CommandType.Text,
            InstanceName = "Test"
        };

        // Act
        DbInterceptionResult result = await interceptor.OnExecutingAsync(context, CancellationToken.None);
        await interceptor.OnExecutedAsync(context, CancellationToken.None);

        // Assert
        result.Should().Be(DbInterceptionResult.Continue);
        interceptor.ExecutingCommands.Should().ContainSingle().Which.Should().Be("SELECT 1");
        interceptor.ExecutedCommands.Should().ContainSingle().Which.Should().Be("SELECT 1");
        interceptor.ErrorCommands.Should().BeEmpty();
    }

    #endregion

    #region IC04: DbInterceptionResult 열거형 검증

    /// <summary>
    /// DbInterceptionResult 열거형이 올바른 값을 가지는지 검증합니다.
    /// </summary>
    [Fact]
    public void IC04_DbInterceptionResult_ShouldHaveExpectedValues()
    {
        DbInterceptionResult.Continue.Should().Be(DbInterceptionResult.Continue);
        DbInterceptionResult.Suppress.Should().Be(DbInterceptionResult.Suppress);

        // Suppress != Continue
        DbInterceptionResult.Suppress.Should().NotBe(DbInterceptionResult.Continue);
    }

    #endregion

    #region IC05: DbInterceptionContext State — 인터셉터 간 데이터 전달

    /// <summary>
    /// DbInterceptionContext.State 딕셔너리로 인터셉터 간 데이터 전달이 가능한지 검증합니다.
    /// </summary>
    [Fact]
    public void IC05_InterceptionContext_State_ShouldSupportDataSharing()
    {
        // Arrange
        DbInterceptionContext context = new()
        {
            CommandText = "SELECT 1",
            CommandType = CommandType.Text,
            InstanceName = "Test"
        };

        // Act
        context.State["key1"] = "value1";
        context.State["key2"] = 42;

        // Assert
        context.State.Should().HaveCount(2);
        context.State["key1"].Should().Be("value1");
        context.State["key2"].Should().Be(42);
    }

    #endregion
}
