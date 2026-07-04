// ============================================================================
// 파일: Unit/ReleaseMetadataGuardTests.cs
// 설명: v2.6.2 release metadata, docs, and curated public API drift guard
// 대상: .NET 10
// ============================================================================

using System.Xml.Linq;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class ReleaseMetadataGuardTests
{
    private const string ExpectedPackageVersion = "2.6.2";
    private const string VerificationManifestVersion = "v2.6.0";

    [Fact]
    public async Task PackageMetadata_ShouldDeclareV262ReleaseVersion()
    {
        XDocument project = XDocument.Parse(await ReadRepoFileAsync("Lib.Db", "Lib.Db.csproj"));

        string? version = project.Descendants("Version").SingleOrDefault()?.Value;
        version.Should().Be(ExpectedPackageVersion);

        string? targetFramework = project.Descendants("TargetFramework").SingleOrDefault()?.Value;
        targetFramework.Should().Be("net10.0");

        string? packageTags = project.Descendants("PackageTags").SingleOrDefault()?.Value;
        packageTags.Should().Contain("sql-server");
        packageTags.Should().Contain("aot");
    }

    [Fact]
    public async Task VerificationManifestVersion_ShouldRemainWorkflowMarker_NotPackageVersion()
    {
        string manifest = await ReadRepoFileAsync("Verification", "manifest.json");
        string verificationDocs = await ReadRepoFileAsync("docs", "verification.md");

        manifest.Should().Contain($"\"version\": \"{VerificationManifestVersion}\"");
        manifest.Should().NotContain($"\"version\": \"v{ExpectedPackageVersion}\"");
        verificationDocs.Should().Contain("manifest.json");
        verificationDocs.Should().Contain(VerificationManifestVersion);
        verificationDocs.Should().Contain("workflow manifest marker");
        verificationDocs.Should().Contain("not the NuGet package version");
    }

    [Fact]
    public async Task PublicDocs_ShouldCoverV262ReleaseSurfaceByDocument()
    {
        string guide = await ReadRepoFileAsync("docs", "01_guide.md");
        string advanced = await ReadRepoFileAsync("docs", "02_advanced.md");
        string api = await ReadRepoFileAsync("docs", "03_api_reference.md");
        string operations = await ReadRepoFileAsync("docs", "04_operations.md");
        string fluent = await ReadRepoFileAsync("docs", "05_fluent_api_reference.md");
        string cookbook = await ReadRepoFileAsync("docs", "06_cookbook.md");
        string history = await ReadRepoFileAsync("docs", "history.md");

        history.Should().Contain(ExpectedPackageVersion);
        history.Should().Contain("sp_executesql");
        history.Should().Contain("SharedMemoryCache");
        history.Should().Contain("configuration binding parity");

        guide.Should().Contain("IDbSession");
        guide.Should().Contain(".Schema");
        guide.Should().Contain(".UseSchema");
        guide.Should().Contain("bare and qualified `sp_executesql`");
        guide.Should().Contain("DenyWriteText");
        guide.Should().Contain("guardrail");
        guide.Should().Contain("DenyAllText");

        advanced.Should().Contain("SharedMemoryCache");
        advanced.Should().Contain("keyed integrity metadata");
        advanced.Should().Contain("quota enforcement");
        advanced.Should().Contain("AOT-safe `BulkShape<T>`");

        api.Should().Contain("RawSqlPolicy");
        api.Should().Contain("sp_executesql");
        api.Should().Contain("EnableSharedMemoryCache");
        api.Should().Contain("keyed integrity metadata");
        api.Should().Contain("AOT-safe bulk path");

        operations.Should().Contain("DenyWriteText");
        operations.Should().Contain("sp_executesql");
        operations.Should().Contain("guardrail");
        operations.Should().Contain("quota check");

        fluent.Should().Contain("IDbSession.Schema");
        fluent.Should().Contain("UseSchema(string)");

        cookbook.Should().Contain("AOT-safe bulk operations");
        cookbook.Should().Contain("BulkWriteOptions");
    }
    [Fact]
    public async Task CuratedLibDbSkillReferences_ShouldCoverSensitiveV262Usage()
    {
        string connectionSecurity = await ReadRepoFileAsync(".agents", "skills", "lib-db", "references", "connection-security.md");
        string caching = await ReadRepoFileAsync(".agents", "skills", "lib-db", "references", "caching.md");
        string bulk = await ReadRepoFileAsync(".agents", "skills", "lib-db", "references", "bulk-insert.md");
        string schema = await ReadRepoFileAsync(".agents", "skills", "lib-db", "references", "schema-maintenance.md");

        connectionSecurity.Should().Contain("sp_executesql");
        connectionSecurity.Should().Contain("DenyWriteText");
        connectionSecurity.Should().Contain("guardrail");
        caching.Should().Contain("quota");
        caching.Should().Contain("integrity");
        bulk.Should().Contain("AOT-safe");
        schema.Should().Contain("db.Schema");
        schema.Should().Contain("UseSchema");
    }

    private static async Task<string> ReadRepoFileAsync(params string[] pathParts)
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string path = Path.Combine(new[] { repoRoot.FullName }.Concat(pathParts).ToArray());
        return await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
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