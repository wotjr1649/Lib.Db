// ============================================================================
// 파일: V230Matrix/MultiDbFixtureBootstrapContractTests.cs
// 설명: v2.3.0 release gate DB bootstrap 순서 계약 검증
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.IntegrationTests.V230Matrix;

public sealed class MultiDbFixtureBootstrapContractTests
{
    [Fact]
    public void MultiDbFixture_ShouldBootstrapVerificationDatabaseBeforeCreatingSession()
    {
        string sourcePath = ResolveSourcePath("MultiDbFixture.cs");
        string source = File.ReadAllText(sourcePath);

        int verificationSetup = source.IndexOf("setup-libdb-verification-test.sql", StringComparison.Ordinal);
        int bootstrapCall = source.IndexOf("EnsureConfiguredDatabasesAsync", StringComparison.Ordinal);
        int sessionRegistration = source.IndexOf("services.AddLibDb(Configuration)", StringComparison.Ordinal);

        verificationSetup.Should().BeGreaterThanOrEqualTo(0);
        bootstrapCall.Should().BeGreaterThanOrEqualTo(0);
        sessionRegistration.Should().BeGreaterThanOrEqualTo(0);
        bootstrapCall.Should().BeLessThan(sessionRegistration);
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
