// ============================================================================
// 파일: V230Matrix/MultiDbFixtureBootstrapContractTests.cs
// 설명: v2.3.0 release gate DB bootstrap 순서 계약 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.V230Matrix;

public sealed class MultiDbFixtureBootstrapContractTests
{
    [Fact]
    public void MultiDbFixture_ShouldBootstrapVerificationDatabaseBeforeCreatingSession()
    {
        string sourcePath = ResolveSourcePath("MultiDbFixture.cs");
        string source = File.ReadAllText(sourcePath);

        int verificationSetup = source.IndexOf("bootstrap-libdb-verification-database.sql", StringComparison.Ordinal);
        int bootstrapCall = source.IndexOf("EnsureConfiguredDatabasesAsync", StringComparison.Ordinal);
        int sessionRegistration = source.IndexOf("services.AddLibDb(Configuration)", StringComparison.Ordinal);

        verificationSetup.Should().BeGreaterThanOrEqualTo(0);
        bootstrapCall.Should().BeGreaterThanOrEqualTo(0);
        sessionRegistration.Should().BeGreaterThanOrEqualTo(0);
        bootstrapCall.Should().BeLessThan(sessionRegistration);
    }

    [Fact]
    public void VerificationBootstrapScript_ShouldCreateOnlyTheDatabase()
    {
        string scriptPath = SqlScriptRunner.ResolveScriptPath("bootstrap-libdb-verification-database.sql");
        string script = File.ReadAllText(scriptPath);

        script.Should().Contain("CREATE DATABASE [LIBDB_VERIFICATION_TEST]");
        script.Should().NotContain("CREATE TABLE");
        script.Should().NotContain("CREATE TYPE");
        script.Should().NotContain("CREATE OR ALTER PROCEDURE");
    }

    private static string ResolveSourcePath(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(
                current.FullName,
                "Verification",
                "projects",
                "Lib.Db.IntegrationTests",
                "Infrastructure",
                fileName);

            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Source file '{fileName}' was not found.", fileName);
    }
}
