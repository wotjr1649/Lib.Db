// ============================================================================
// 파일: Unit/PackageGraphTests.cs
// 설명: v2.3 단일 Lib.Db 패키지 그래프 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.IntegrationTests.Unit;

public sealed class PackageGraphTests
{
    [Fact]
    public void LibDbPackageGraph_ShouldNotReferenceTvpGenAnalyzer()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string project = File.ReadAllText(Path.Combine(repoRoot.FullName, "Lib.Db", "Lib.Db.csproj"));
        string testsProject = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "projects",
            "Lib.Db.IntegrationTests",
            "Lib.Db.IntegrationTests.csproj"));
        string solution = File.ReadAllText(Path.Combine(repoRoot.FullName, "Lib.Db.slnx"));

        project.Should().NotContain("Lib.Db.TvpGen");
        project.Should().NotContain("analyzers/dotnet/cs");
        project.Should().NotContain("LIBDB_NATIVE_AOT");
        project.Should().NotContain("PublishAot");
        testsProject.Should().NotContain("Lib.Db.TvpGen");
        solution.Should().NotContain("Lib.Db.TvpGen");
    }

    [Fact]
    public void LibDbPackageGraph_ShouldNotUsePublishAotConditionalSourceSplits()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        IEnumerable<string> sourceFiles = Directory.EnumerateFiles(
            Path.Combine(repoRoot.FullName, "Lib.Db"),
            "*.cs",
            SearchOption.AllDirectories)
            .Where(static path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (string sourceFile in sourceFiles)
        {
            string source = File.ReadAllText(sourceFile);
            source.Should().NotContain("LIBDB_NATIVE_AOT", $"package source {sourceFile} must match Native AOT consumer behavior");
        }
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
