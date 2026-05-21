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
    public void ConnectionStrings_ShouldUseOrdinalIgnoreCaseLookup()
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        string connectionString = options.ConnectionStrings["Default"];
        options.ConnectionStrings = new Dictionary<string, string>
        {
            ["Primary"] = connectionString
        };
        options.ConnectionStringNames = ["primary"];

        options.ConnectionStrings.TryGetValue("PRIMARY", out string? resolved).Should().BeTrue();
        resolved.Should().Be(connectionString);

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.Should().BeFalse(string.Join("; ", result.Failures ?? []));
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

    [Theory]
    [InlineData("Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
    [InlineData("Raw:Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
    public void ConnectionStringNames_SensitiveMissingName_ShouldNotLeakRawName(string sensitiveName)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStrings.Clear();
        options.ConnectionStringNames = [sensitiveName];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        string message = string.Join(";", result.Failures);
        message.Should().Contain("[redacted]");
        message.Should().NotContain("Password=placeholder");
        message.Should().NotContain("User Id=app_user");
        message.Should().NotContain("Database=TEST");
    }

    [Fact]
    public void ConnectionStringNames_SensitiveDuplicateNames_ShouldNotLeakRawName()
    {
        const string sensitiveName =
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStringNames = [sensitiveName, sensitiveName];
        options.ConnectionStrings.Clear();

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        string message = string.Join(";", result.Failures);
        message.Should().Contain("[redacted]");
        message.Should().NotContain("Password=placeholder");
        message.Should().NotContain("User Id=app_user");
        message.Should().NotContain("Database=TEST");
    }

    [Fact]
    public void ConnectionStringNames_SensitiveProductionProfileName_ShouldStopBeforeProductionMessage()
    {
        const string sensitiveName =
            "Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=False;TrustServerCertificate=True";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
        options.ConnectionStringNames = [sensitiveName];
        options.ConnectionStrings[sensitiveName] =
            "Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=False;TrustServerCertificate=True";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        string message = string.Join(";", result.Failures);
        message.Should().Contain("[redacted]");
        message.Should().NotContain("Password=placeholder");
        message.Should().NotContain("User Id=sa");
        message.Should().NotContain("Database=TEST");
    }

    [Theory]
    [InlineData("")]
    [InlineData("=no_key_here")]
    [InlineData("Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=False;TrustServerCertificate=True")]
    public void ValidateConnectionStringSecurityProfile_SensitiveConnectionName_ShouldNotLeakRawName(
        string connectionString)
    {
        const string sensitiveName =
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
        List<string> errors = [];

        LibDbOptionsValidator.ValidateConnectionStringSecurityProfile(
            options,
            sensitiveName,
            connectionString,
            errors);

        errors.Should().NotBeEmpty();
        string message = string.Join(";", errors);
        message.Should().Contain("[redacted]");
        message.Should().NotContain("Password=placeholder");
        message.Should().NotContain("User Id=app_user");
        message.Should().NotContain("Database=TEST");
    }

    [Fact]
    public void ConnectionStringNames_MalformedSensitiveMissingNameAndRegisteredKey_ShouldNotLeakRawFragments()
    {
        const string missingName =
            "Server='unterminated;Database=TEST;User Id=app_user;Password=placeholder";
        const string registeredKey =
            "Data Source='unterminated;Initial Catalog=TEST;UID=app_user;Pwd=placeholder";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStrings.Clear();
        options.ConnectionStrings[registeredKey] =
            "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False";
        options.ConnectionStringNames = [missingName];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        AssertMalformedSecretFragmentsRedacted(string.Join(";", result.Failures));
    }

    [Fact]
    public void ConnectionStringNames_MalformedSensitiveDuplicateName_ShouldNotLeakRawFragments()
    {
        const string sensitiveName =
            "Server='unterminated;Database=TEST;User Id=app_user;Password=placeholder";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStrings.Clear();
        options.ConnectionStringNames = [sensitiveName, sensitiveName];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        AssertMalformedSecretFragmentsRedacted(string.Join(";", result.Failures));
    }

    [Fact]
    public void ConnectionStringNames_MalformedSensitiveEmptyConnectionString_ShouldNotLeakRawFragments()
    {
        const string sensitiveName =
            "Server='unterminated;Database=TEST;User Id=app_user;Password=placeholder";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStrings.Clear();
        options.ConnectionStrings[sensitiveName] = string.Empty;
        options.ConnectionStringNames = [sensitiveName];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        AssertMalformedSecretFragmentsRedacted(string.Join(";", result.Failures));
    }

    [Fact]
    public void ConnectionStringNames_MalformedSensitiveNameRegisteredWithValidValue_ShouldFailValidationWithoutLeakingSecret()
    {
        const string sensitiveName =
            "Server='unterminated;Database=TEST;User Id=app_user;Password=placeholder";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStrings.Clear();
        options.ConnectionStrings[sensitiveName] =
            "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False";
        options.ConnectionStringNames = [sensitiveName];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);
        string failures = string.Join("; ", result.Failures ?? []);

        result.Failed.Should().BeTrue(
            "malformed connection-string-like logical names must be rejected even when the configured value is valid");
        failures.Should().Contain("ConnectionString:[redacted]");
        failures.Should().NotContain(sensitiveName);
        AssertMalformedSecretFragmentsRedacted(failures);
    }

    [Theory]
    [InlineData("Address='unterminated,Initial Catalog=TEST,UID=app_user,Pwd=placeholder")]
    [InlineData("Addr='unterminated,Initial Catalog=TEST,UID=app_user,Pwd=placeholder")]
    [InlineData("Network Address='unterminated,Initial Catalog=TEST,UID=app_user,Pwd=placeholder")]
    public void ConnectionStringNames_MalformedAliasOnlySensitiveName_ShouldFailValidationWithoutLeakingSecret(
        string sensitiveName)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStrings.Clear();
        options.ConnectionStrings[sensitiveName] =
            "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False";
        options.ConnectionStringNames = [sensitiveName];

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);
        string failures = string.Join("; ", result.Failures ?? []);

        result.Failed.Should().BeTrue(
            "malformed alias-only connection-string-like logical names must be rejected");
        failures.Should().Contain("ConnectionString:[redacted]");
        failures.Should().NotContain(sensitiveName);
        AssertMalformedSecretFragmentsRedacted(failures);
    }

    [Theory]
    [InlineData("Address='unterminated,Initial Catalog=TEST,UID=app_user,Pwd=placeholder")]
    [InlineData("Addr='unterminated,Initial Catalog=TEST,UID=app_user,Pwd=placeholder")]
    [InlineData("Network Address='unterminated,Initial Catalog=TEST,UID=app_user,Pwd=placeholder")]
    public void ConnectionStrings_MalformedAliasOnlySensitiveKey_ShouldFailValidationWithoutLeakingSecret(
        string sensitiveKey)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStringNames = ["Missing"];
        options.ConnectionStrings.Clear();
        options.ConnectionStrings[sensitiveKey] =
            "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);
        string failures = string.Join("; ", result.Failures ?? []);

        result.Failed.Should().BeTrue(
            "malformed alias-only connection-string-like dictionary keys must be rejected");
        failures.Should().Contain("ConnectionString:[redacted]");
        failures.Should().NotContain(sensitiveKey);
        AssertMalformedSecretFragmentsRedacted(failures);
    }

    [Fact]
    public void ConnectionStrings_MalformedSensitiveMarsParseFailureKey_ShouldNotLeakRawFragments()
    {
        const string sensitiveKey =
            "Server='unterminated;Database=TEST;User Id=app_user;Password=placeholder";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.Mars = MarsPolicy.ForceEnable;
        options.ConnectionStrings[sensitiveKey] = "Server='unterminated";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        AssertMalformedSecretFragmentsRedacted(string.Join(";", result.Failures));
    }

    [Fact]
    public void ValidateConnectionStringSecurityProfile_MalformedSensitiveConnectionName_ShouldNotLeakRawFragments()
    {
        const string sensitiveName =
            "Server='unterminated;Database=TEST;User Id=app_user;Password=placeholder";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
        List<string> errors = [];

        LibDbOptionsValidator.ValidateConnectionStringSecurityProfile(
            options,
            sensitiveName,
            "Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=False;TrustServerCertificate=True",
            errors);

        errors.Should().NotBeEmpty();
        AssertMalformedSecretFragmentsRedacted(string.Join(";", errors));
    }

    [Fact]
    public void ConnectionStringNames_ConnectionStringShape_ShouldFailValidationWithoutLeakingSecret()
    {
        const string rawName =
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStringNames = [rawName];
        options.ConnectionStrings[rawName] = "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);
        string failures = string.Join("; ", result.Failures ?? []);

        result.Failed.Should().BeTrue("connection string material must never be used as a logical instance key");
        failures.Should().Contain("ConnectionString:[redacted]");
        failures.Should().NotContain("placeholder");
        failures.Should().NotContain(rawName);
    }

    [Fact]
    public void ConnectionStrings_ConnectionStringShapeKey_ShouldFailValidationWithoutLeakingSecret()
    {
        const string rawKey =
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.ConnectionStringNames = ["Missing"];
        options.ConnectionStrings.Clear();
        options.ConnectionStrings[rawKey] =
            "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False";

        LibDbOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, options);
        string failures = string.Join("; ", result.Failures ?? []);

        result.Failed.Should().BeTrue("connection string material must never be used as an option dictionary key");
        failures.Should().Contain("ConnectionString:[redacted]");
        failures.Should().NotContain("placeholder");
        failures.Should().NotContain(rawKey);
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

    private static void AssertMalformedSecretFragmentsRedacted(string message)
    {
        message.Should().Contain("[redacted]");
        message.Should().NotContain("Address=");
        message.Should().NotContain("Addr=");
        message.Should().NotContain("Network Address=");
        message.Should().NotContain("Password=");
        message.Should().NotContain("Pwd=");
        message.Should().NotContain("User Id=");
        message.Should().NotContain("UID=");
        message.Should().NotContain("Database=TEST");
        message.Should().NotContain("Initial Catalog=TEST");
    }

    #endregion
}
