// ============================================================================
// 파일: Unit/OptionsValidationTests.cs
// 설명: LibDbOptions/ChaosOptions/ResilienceOptions 검증 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Core;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Lib.Db.IntegrationTests.Unit;

[Trait("Category", "Unit")]
public sealed class OptionsValidationTests
{
    #region LibDbOptions Validation Tests

    [Theory]
    [InlineData(1)]
    [InlineData(600)]
    [InlineData(30)]
    public void DefaultCommandTimeoutSeconds_ValidRange_ShouldSet(int value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.DefaultCommandTimeoutSeconds = value;
        Assert.Equal(value, options.DefaultCommandTimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    [InlineData(-1)]
    public void DefaultCommandTimeoutSeconds_InvalidRange_ShouldThrow(int value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DefaultCommandTimeoutSeconds = value);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(100_000)]
    [InlineData(5000)]
    public void BulkBatchSize_ValidRange_ShouldSet(int value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.BulkBatchSize = value;
        Assert.Equal(value, options.BulkBatchSize);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100_001)]
    public void BulkBatchSize_InvalidRange_ShouldThrow(int value)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.BulkBatchSize = value);
    }

    [Fact]
    public void ConnectionStrings_SetNull_ShouldThrow()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentNullException>(() => options.ConnectionStrings = null!);
    }

    [Fact]
    public void PrewarmSchemas_SetNull_ShouldThrow()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentNullException>(() => options.PrewarmSchemas = null!);
    }

    #endregion

    #region ChaosOptions Validation Tests

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public void Chaos_ExceptionRate_ValidRange_ShouldSet(double value)
    {
        ChaosOptions options = new();
        options.ExceptionRate = value;
        Assert.Equal(value, options.ExceptionRate);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Chaos_ExceptionRate_InvalidRange_ShouldThrow(double value)
    {
        ChaosOptions options = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ExceptionRate = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60000)]
    public void Chaos_MinLatencyMs_ValidRange_ShouldSet(int value)
    {
        ChaosOptions options = new();
        options.MinLatencyMs = value;
        Assert.Equal(value, options.MinLatencyMs);
    }

    [Theory]
    [InlineData(-1)]
    public void Chaos_MinLatencyMs_InvalidRange_ShouldThrow(int value)
    {
        ChaosOptions options = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MinLatencyMs = value);
    }

    #endregion

    #region ResilienceOptions Validation Tests

    [Theory]
    [InlineData(0.0)]
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

    #endregion

    #region ConnectionStringNames 방어 로직 테스트

    [Fact]
    public void ConnectionStringNames_Empty_ShouldFailValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        Assert.Throws<ArgumentException>(() => options.ConnectionStringNames = Array.Empty<string>());
    }

    [Fact]
    public void ConnectionStringNames_DuplicateKey_ShouldFailValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStringNames = ["Default", "Default"];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed, "중복 키가 있으면 검증이 실패해야 합니다.");
        Assert.Contains("중복 키", string.Join("; ", result.Failures!));
    }

    [Fact]
    public void ConnectionStringNames_WhitespaceName_ShouldFailValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStringNames = ["Default", "  "];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed, "공백 이름이 있으면 검증이 실패해야 합니다.");
        Assert.Contains("비어있거나 공백", string.Join("; ", result.Failures!));
    }

    [Fact]
    public void ConnectionStringNames_MissingFromConnectionStrings_ShouldFailValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStringNames = ["Default", "NonExistent"];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed, "ConnectionStrings에 없는 키가 있으면 검증이 실패해야 합니다.");
        Assert.Contains("NonExistent", string.Join("; ", result.Failures!));
    }

    [Fact]
    public void ConnectionStrings_EmptyValue_ShouldFailValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStrings["Default"] = "";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed, "빈 연결 문자열이면 검증이 실패해야 합니다.");
        Assert.Contains("비어있습니다", string.Join("; ", result.Failures!));
    }

    [Theory]
    [InlineData("Server='unclosed")]
    [InlineData("=no_key_here")]
    public void ConnectionStrings_InvalidFormat_ShouldFailValidation(string malformed)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStrings["Default"] = malformed;

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed, "잘못된 형식의 연결 문자열이면 검증이 실패해야 합니다.");
        Assert.Contains("형식이 잘못되었습니다", string.Join("; ", result.Failures!));
    }

    [Fact]
    public void ProductionSecurityProfile_TrustServerCertificate_ShouldFailValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
        options.ConnectionStrings["Default"] =
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed, "운영 프로필에서는 TrustServerCertificate=True를 기본 차단해야 합니다.");
        Assert.Contains("TrustServerCertificate", string.Join("; ", result.Failures!));
    }

    [Fact]
    public void ProductionSecurityProfile_WeakEncryption_ShouldFailValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
        options.ConnectionStrings["Default"] =
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=False;TrustServerCertificate=False";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed, "운영 프로필에서는 암호화 비활성 연결을 차단해야 합니다.");
        Assert.Contains("Encrypt", string.Join("; ", result.Failures!));
    }

    [Fact]
    public void ProductionSecurityProfile_SaLogin_ShouldFailValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
        options.ConnectionStrings["Default"] =
            "Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=True;TrustServerCertificate=False";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.True(result.Failed, "운영 프로필에서는 sa 로그인을 기본 차단해야 합니다.");
        Assert.Contains("privileged SQL login", string.Join("; ", result.Failures!));
    }

    [Fact]
    public void ProductionSecurityProfile_ExplicitWaivers_ShouldPassValidation()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
        options.AllowProductionTrustServerCertificateWaiver = true;
        options.AllowProductionSaLoginWaiver = true;
        options.ConnectionStrings["Default"] =
            "Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=True;TrustServerCertificate=True";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        Assert.False(result.Failed, string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void ProductionSecurityDefaults_ShouldEnableProductionProfileAndSafeRawSqlPolicy()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.IncludeParametersInTrace = true;
        options.AllowProductionTrustServerCertificateWaiver = true;
        options.AllowProductionSaLoginWaiver = true;

        LibDbOptions returned = options.UseProductionSecurityDefaults();

        returned.Should().BeSameAs(options);
        options.ConnectionSecurityProfile.Should().Be(ConnectionSecurityProfile.Production);
        options.RawSqlPolicy.Should().Be(RawSqlPolicy.DenyWriteText);
        options.IncludeParametersInTrace.Should().BeFalse();
        options.AllowProductionTrustServerCertificateWaiver.Should().BeFalse();
        options.AllowProductionSaLoginWaiver.Should().BeFalse();
    }

    [Fact]
    public void ProductionSecurityDefaults_ShouldNotWeakenDenyAllText()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.RawSqlPolicy = RawSqlPolicy.DenyAllText;

        options.UseProductionSecurityDefaults();

        options.RawSqlPolicy.Should().Be(RawSqlPolicy.DenyAllText);
    }

    [Fact]
    public void ProductionSecurityDefaults_OptionsBuilder_ShouldApplyAtResolution()
    {
        ServiceCollection services = new();
        services
            .AddLibDbOptions(options =>
            {
                options.ConnectionStringNames = ["Default"];
                options.ConnectionStrings["Default"] =
                    "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=False";
            })
            .UseProductionSecurityDefaults();

        using ServiceProvider provider = services.BuildServiceProvider();

        LibDbOptions options = provider.GetRequiredService<IOptions<LibDbOptions>>().Value;
        options.ConnectionSecurityProfile.Should().Be(ConnectionSecurityProfile.Production);
        options.RawSqlPolicy.Should().Be(RawSqlPolicy.DenyWriteText);
    }

    [Fact]
    public void ConnectionStringNames_FirstItem_ShouldBeDefaultInstance()
    {
        LibDbOptions options = TestOptionsFactory.CreateValidOptions();
        options.ConnectionStringNames = ["Admin", "Default"];

        Assert.Equal("Admin", options.ConnectionStringNames[0]);
    }

    #endregion
}
