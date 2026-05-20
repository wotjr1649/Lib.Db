// ============================================================================
// 파일: Unit/ConsumerSkillTests.cs
// 설명: repo-local Lib.Db consumer skill 회귀 테스트
// 대상: .NET 10
// ============================================================================

namespace Lib.Db.IntegrationTests.Unit;

public sealed class ConsumerSkillTests
{
    [Theory]
    [InlineData(".agent")]
    [InlineData(".claude")]
    public void ConsumerSkill_ShouldDocumentRuntimeTvpMigrationWithoutInternalVerificationWorkflows(string skillRootName)
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string skillRoot = Path.Combine(repoRoot.FullName, skillRootName, "skills", "lib-db");
        Directory.Exists(skillRoot).Should().BeTrue($"{skillRootName} should contain the repo-local Lib.Db skill");

        string combined = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(skillRoot, "*.md", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));

        combined.Should().Contain("Runtime TVP");
        combined.Should().Contain("Migrating From `Lib.Db.TvpGen`");
        combined.Should().Contain("Remove the `Lib.Db.TvpGen` package/analyzer reference");
        combined.Should().Contain("LibDb.Tvp(\"schema.TypeName\", rows)");
        combined.Should().Contain("options.Tvp.Map<T>()");

        combined.Should().NotContain("Invoke-Benchmarks");
        combined.Should().NotContain("Invoke-Coverage");
        combined.Should().NotContain("Invoke-Verification");
        combined.Should().NotContain("BenchmarkDotNet");
        combined.Should().NotContain("Verification/scripts");
        combined.Should().NotContain("ChaosHarness");
        combined.Should().NotContain("LIBDB_CHAOS_TEST");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lib.Db.slnx")))
                return current;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Lib.Db repository root could not be found.");
    }
}
