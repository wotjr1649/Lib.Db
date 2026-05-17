// ============================================================================
// 파일: Unit/TestConnectionStringsTests.cs
// 설명: 테스트 연결 문자열 구성 해석 유닛 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using Xunit.Sdk;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class TestConnectionStringsTests
{
    [Fact]
    public void TCS01_CreateConfiguration_ShouldNormalizeAliasEnvironmentVariables()
    {
        const string verificationAlias = "LIBDB_TEST_CONNECTION_VERIFICATION";
        const string sorterAlias = "LIBDB_TEST_CONNECTION_SORTER";
        string verification = TestConnectionStrings.Placeholder("VerificationAlias");
        string sorter = TestConnectionStrings.Placeholder("SorterAlias");

        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [verificationAlias] = verification,
            [sorterAlias] = sorter
        };

        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        configuration.GetConnectionString(TestConnectionStrings.Verification).Should().Be(verification);
        configuration.GetConnectionString(TestConnectionStrings.Sorter).Should().Be(sorter);
    }

    [Fact]
    public void TCS02_RequireSafeSchemaInitialization_ShouldAllowTestDatabaseName()
    {
        const string verificationAlias = "LIBDB_TEST_CONNECTION_VERIFICATION";
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [verificationAlias] = TestConnectionStrings.Placeholder("LIBDB_VERIFICATION_TEST")
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Verification);

        act.Should().NotThrow();
    }

    [Fact]
    public void TCS03_RequireSafeSchemaInitialization_ShouldSkipNonTestDatabaseName()
    {
        const string sorterAlias = "LIBDB_TEST_CONNECTION_SORTER";
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [sorterAlias] = TestConnectionStrings.Placeholder("ProductionDb")
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Sorter);

        act.Should().Throw<SkipException>()
            .WithMessage("*LIBDB_TEST_ALLOW_SCHEMA_INIT*");
    }

    [Fact]
    public void TCS04_RequireSafeSchemaInitialization_ShouldRejectDevSubstringWithoutTokenBoundary()
    {
        const string verificationAlias = "LIBDB_TEST_CONNECTION_VERIFICATION";
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [verificationAlias] = TestConnectionStrings.Placeholder("ProdDevelopmentArchive")
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Verification);

        act.Should().Throw<SkipException>()
            .WithMessage("*TEST/LOCAL/DEV token*");
    }

    [Fact]
    public void TCS05_RequireSafeSchemaInitialization_ShouldAllowSeparatedLocalToken()
    {
        const string verificationAlias = "LIBDB_TEST_CONNECTION_VERIFICATION";
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [verificationAlias] = TestConnectionStrings.Placeholder("LIBDB-LOCAL-VERIFICATION")
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Verification);

        act.Should().NotThrow();
    }
}
