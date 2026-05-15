using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text;
using Lib.Db.Contracts.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Lib.Db.IntegrationTests.Architecture;

public sealed class TvpGeneratorEscapingTests
{
    [Fact]
    public void TvpAccessorGenerator_Should_Escape_SqlTypeName_String_Literals()
    {
        const string source = """
using Lib.Db.Contracts.Models;

namespace GeneratorProbe;

[TvpRow(TypeName = "dbo.Bad\"Name\r\nNext")]
public sealed partial class QuotedTvp
{
    public int Id { get; set; }
}
""";

        GeneratorResult result = RunGenerator("Lib.Db.TvpGen.TvpAccessorGenerator", source);

        string generated = result.GeneratedSources.Should().ContainSingle().Subject;
        generated.Should().Contain("SqlTypeName = \"dbo.Bad\\\"Name\\r\\nNext\"");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void DbFirstTvpGenerator_Should_Escape_TypeName_And_Xml_Documentation_Text()
    {
        const string source = """
using Lib.Db.Contracts.Models;

namespace GeneratorProbe;

[GenerateTvpFromDb("dbo.Bad\\Path")]
public sealed partial class DbFirstDto
{
}
""";

        const string schema = """
{
  "Tvps": {
    "dbo.Bad\Path": [
      { "Name": "DisplayName", "Type": "nvarchar&" }
    ]
  }
}
""";

        GeneratorResult result = RunGenerator(
            "Lib.Db.TvpGen.DbFirstTvpGenerator",
            source,
            [new InMemoryAdditionalText("libdb.schema.json", schema)]);

        string generated = result.GeneratedSources.Should().ContainSingle().Subject;
        generated.Should().Contain("[TvpRow(TypeName = \"dbo.Bad\\\\Path\")]");
        generated.Should().Contain("/// DisplayName (nvarchar&amp;)");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void DbFirstTvpGenerator_Should_Report_Diagnostic_For_Invalid_Column_Identifiers()
    {
        const string source = """
using Lib.Db.Contracts.Models;

namespace GeneratorProbe;

[GenerateTvpFromDb("dbo.InvalidColumn")]
public sealed partial class DbFirstDto
{
}
""";

        const string schema = """
{
  "Tvps": {
    "dbo.InvalidColumn": [
      { "Name": "bad-name", "Type": "int" }
    ]
  }
}
""";

        GeneratorResult result = RunGenerator(
            "Lib.Db.TvpGen.DbFirstTvpGenerator",
            source,
            [new InMemoryAdditionalText("libdb.schema.json", schema)]);

        result.DriverDiagnostics.Select(d => d.Id).Should().Contain("LIBDB003");
        result.Errors.Should().BeEmpty();
    }

    private static GeneratorResult RunGenerator(
        string generatorTypeName,
        string source,
        IReadOnlyList<AdditionalText>? additionalTexts = null)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorProbe",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: CreateMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        IIncrementalGenerator generator = CreateGenerator(generatorTypeName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: additionalTexts ?? [],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> driverDiagnostics);

        ImmutableArray<Diagnostic> errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        string[] generatedSources = driver.GetRunResult()
            .GeneratedTrees
            .Select(t => t.GetText().ToString())
            .ToArray();

        return new GeneratorResult(generatedSources, driverDiagnostics, errors);
    }

    private static IIncrementalGenerator CreateGenerator(string generatorTypeName)
    {
        string generatorPath = FindGeneratorAssemblyPath();
        Assembly assembly = Assembly.LoadFrom(generatorPath);
        Type type = assembly.GetType(generatorTypeName, throwOnError: true)!;
        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
    }

    private static string FindGeneratorAssemblyPath()
    {
        string? copiedPath = Path.Combine(AppContext.BaseDirectory, "Lib.Db.TvpGen.dll");
        if (File.Exists(copiedPath))
            return copiedPath;

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            foreach (string configuration in new[] { "Release", "Debug" })
            {
                string candidate = Path.Combine(
                    current.FullName,
                    "Lib.Db.TvpGen",
                    "bin",
                    configuration,
                    "net10.0",
                    "Lib.Db.TvpGen.dll");

                if (File.Exists(candidate))
                    return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Lib.Db.TvpGen.dll 빌드 산출물을 찾을 수 없습니다.");
    }

    private static MetadataReference[] CreateMetadataReferences()
    {
        string tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES 값을 찾을 수 없습니다.");

        IEnumerable<string> frameworkReferences = tpa
            .Split(Path.PathSeparator)
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        return frameworkReferences
            .Append(typeof(TvpRowAttribute).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private sealed record GeneratorResult(
        IReadOnlyList<string> GeneratedSources,
        ImmutableArray<Diagnostic> DriverDiagnostics,
        ImmutableArray<Diagnostic> Errors);

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(text, Encoding.UTF8);
    }
}
