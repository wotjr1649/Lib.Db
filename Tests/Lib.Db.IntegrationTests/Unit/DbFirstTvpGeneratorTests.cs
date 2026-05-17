// ============================================================================
// 파일: Unit/DbFirstTvpGeneratorTests.cs
// 설명: DB-first TVP source generator의 unsupported target 회귀 테스트
// ============================================================================

using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.TvpGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class DbFirstTvpGeneratorTests
{
    private const string SchemaJson = """
        {
          "Tvps": {
            "dbo.Sample": [
              { "Name": "Id", "Type": "int" }
            ]
          }
        }
        """;

    [Theory(DisplayName = "DBF01: unsupported DB-first targets emit diagnostics and skip source generation")]
    [InlineData("public partial class GenericRow<T> { }", "LIBDB007")]
    [InlineData("file partial class FileLocalRow { }", "LIBDB008")]
    [InlineData("public partial record class RecordRow { }", "LIBDB009")]
    [InlineData("public class NonPartialRow { }", "LIBDB010")]
    [InlineData("public static partial class StaticRow { }", "LIBDB011")]
    public void DBF01_UnsupportedTargets_ShouldReportDiagnostic_AndSkipGeneratedSource(
        string declaration,
        string expectedDiagnosticId)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var (diagnostics, runResult) = RunGenerator(
            new DbFirstTvpGenerator().AsSourceGenerator(),
            $$"""
            using Lib.Db.Contracts.Models;

            [GenerateTvpFromDb("dbo.Sample")]
            {{declaration}}
            """,
            parseOptions,
            new[] { new InMemoryAdditionalText("libdb.schema.json", SchemaJson) });

        diagnostics.Should().Contain(d => d.Id == expectedDiagnosticId);
        Diagnostic diagnostic = diagnostics.ToArray().First(d => d.Id == expectedDiagnosticId);
        diagnostic.Location.Should().NotBe(Location.None);
        runResult.GeneratedTrees.Should().BeEmpty();
    }

    [Fact(DisplayName = "DBF02: nested DB-first target emits diagnostic at source location and skips generation")]
    public void DBF02_NestedTarget_ShouldReportDiagnosticAtLocation_AndSkipGeneratedSource()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var (diagnostics, runResult) = RunGenerator(
            new DbFirstTvpGenerator().AsSourceGenerator(),
            """
            using Lib.Db.Contracts.Models;

            public partial class Outer
            {
                [GenerateTvpFromDb("dbo.Sample")]
                public partial class NestedRow { }
            }
            """,
            parseOptions,
            new[] { new InMemoryAdditionalText("libdb.schema.json", SchemaJson) });

        diagnostics.Should().Contain(d => d.Id == "LIBDB005");
        Diagnostic diagnostic = diagnostics.ToArray().First(d => d.Id == "LIBDB005");
        diagnostic.Location.Should().NotBe(Location.None);
        runResult.GeneratedTrees.Should().BeEmpty();
    }

    [Fact(DisplayName = "TVP01: file-local TvpRow target emits diagnostic and skips source generation")]
    public void TVP01_FileLocalTvpRow_ShouldReportDiagnostic_AndSkipGeneratedSource()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var (diagnostics, runResult) = RunGenerator(
            new TvpAccessorGenerator().AsSourceGenerator(),
            """
            using Lib.Db.Contracts.Models;

            [TvpRow(TypeName = "dbo.FileLocalTvp")]
            file partial class FileLocalTvpRow
            {
                public int Id { get; set; }
            }
            """,
            parseOptions);

        diagnostics.Should().Contain(d => d.Id == "TVP006");
        runResult.GeneratedTrees.Should().BeEmpty();
    }

    [Fact(DisplayName = "TVP02: nested TvpRow inside file-local type emits diagnostic and skips source generation")]
    public void TVP02_NestedTvpRowInsideFileLocalType_ShouldReportDiagnostic_AndSkipGeneratedSource()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var (diagnostics, runResult) = RunGenerator(
            new TvpAccessorGenerator().AsSourceGenerator(),
            """
            using Lib.Db.Contracts.Models;

            file partial class FileLocalOuter
            {
                [TvpRow(TypeName = "dbo.NestedFileLocalTvp")]
                public partial class NestedTvpRow
                {
                    public int Id { get; set; }
                }
            }
            """,
            parseOptions);

        diagnostics.Should().Contain(d => d.Id == "TVP006");
        runResult.GeneratedTrees.Should().BeEmpty();
    }

    [Fact(DisplayName = "TVP03: generated static validator preserves runtime-compatible SQL type groups")]
    public void TVP03_StaticValidator_ShouldPreserveRuntimeCompatibleSqlTypeGroups()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var (diagnostics, runResult, outputCompilation) = RunGeneratorAndUpdateCompilation(
            new TvpAccessorGenerator().AsSourceGenerator(),
            """
            using System;
            using Lib.Db.Contracts.Models;

            [TvpRow(TypeName = "dbo.CompatibilityTvp")]
            public partial class CompatibilityTvpRow
            {
                public string Name { get; set; } = "";
                public byte Flag { get; set; }
                public short SmallCode { get; set; }
                public int Quantity { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateOnly EffectiveDate { get; set; }
                public TimeOnly EffectiveTime { get; set; }
                public Half Ratio { get; set; }
                public decimal Amount { get; set; }
            }
            """,
            parseOptions);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        outputCompilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();

        string generatedSource = string.Join(
            Environment.NewLine,
            runResult.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        AssertGeneratedSqlTypePatterns(
            generatedSource,
            [
                "SqlDbType.NVarChar or SqlDbType.VarChar or SqlDbType.Char or SqlDbType.NChar or SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml",
                "SqlDbType.TinyInt",
                "SqlDbType.SmallInt",
                "SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.TinyInt",
                "SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.Date or SqlDbType.SmallDateTime",
                "SqlDbType.Date",
                "SqlDbType.Time",
                "SqlDbType.Real",
                "SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney"
            ]);

        generatedSource.Should().Contain("허용: NVarChar/VarChar/Char/NChar/Text/NText/Xml");
        generatedSource.Should().Contain("!string.Equals(col.Name, \"Name\", StringComparison.OrdinalIgnoreCase)");
        generatedSource.Should().Contain("허용: TinyInt");
        generatedSource.Should().Contain("허용: SmallInt");
        generatedSource.Should().Contain("허용: Int/SmallInt/TinyInt");
        generatedSource.Should().Contain("허용: DateTime/DateTime2/Date/SmallDateTime");
        generatedSource.Should().Contain("허용: Date");
        generatedSource.Should().Contain("허용: Time");
        generatedSource.Should().Contain("허용: Real");
        generatedSource.Should().Contain("허용: Decimal/Money/SmallMoney");
        generatedSource.Should().NotContain("col.SqlDbType != SqlDbType.NVarChar");
    }

    [Theory(DisplayName = "TVP04: DB-first SQL type mapping aligns with static validator compatible groups")]
    [InlineData("smallmoney", "decimal")]
    [InlineData("smalldatetime", "global::System.DateTime")]
    [InlineData("xml", "string")]
    [InlineData("ntext", "string")]
    [InlineData("timestamp", "byte[]")]
    [InlineData("rowversion", "byte[]")]
    public void TVP04_DbFirstReverseMapping_ShouldAlignWithCompatibleSqlTypeGroups(
        string sqlType,
        string expectedClrType)
    {
        TypeMappingRegistry.MapSqlTypeToCSharp(sqlType).Should().Be(expectedClrType);
    }

    [Fact(DisplayName = "DBF03: unknown DB-first SQL type emits diagnostic and skips generation")]
    public void DBF03_UnknownSqlType_ShouldReportDiagnostic_AndSkipGeneratedSource()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        const string schemaJson = """
            {
              "Tvps": {
                "dbo.Sample": [
                  { "Name": "Payload", "Type": "geography" }
                ]
              }
            }
            """;

        var (diagnostics, runResult) = RunGenerator(
            new DbFirstTvpGenerator().AsSourceGenerator(),
            """
            using Lib.Db.Contracts.Models;

            [GenerateTvpFromDb("dbo.Sample")]
            public partial class UnknownSqlTypeRow { }
            """,
            parseOptions,
            new[] { new InMemoryAdditionalText("libdb.schema.json", schemaJson) });

        diagnostics.Should().Contain(d => d.Id == "LIBDB012");
        runResult.GeneratedTrees.Should().BeEmpty();
    }

    [Fact(DisplayName = "RES01: file-local DbResult target emits diagnostic and skips source generation")]
    public void RES01_FileLocalDbResult_ShouldReportDiagnostic_AndSkipGeneratedSource()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var (diagnostics, runResult) = RunGenerator(
            new ResultAccessorGenerator().AsSourceGenerator(),
            """
            using Lib.Db.Contracts.Mapping;

            [DbResult]
            file partial class FileLocalResult
            {
                public int Id { get; set; }
            }
            """,
            parseOptions);

        diagnostics.Should().Contain(d => d.Id == "RES009");
        runResult.GeneratedTrees.Should().BeEmpty();
    }

    [Fact(DisplayName = "RES02: nested DbResult inside file-local type emits diagnostic and skips source generation")]
    public void RES02_NestedDbResultInsideFileLocalType_ShouldReportDiagnostic_AndSkipGeneratedSource()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var (diagnostics, runResult) = RunGenerator(
            new ResultAccessorGenerator().AsSourceGenerator(),
            """
            using Lib.Db.Contracts.Mapping;

            file partial class FileLocalOuter
            {
                [DbResult]
                public partial class NestedResult
                {
                    public int Id { get; set; }
                }
            }
            """,
            parseOptions);

        diagnostics.Should().Contain(d => d.Id == "RES009");
        runResult.GeneratedTrees.Should().BeEmpty();
    }

    [Fact(DisplayName = "RES03: DbResult keyword members are escaped in generated assignments")]
    public void RES03_DbResultKeywordMembers_ShouldGenerateCompilableAssignments()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var (diagnostics, runResult, outputCompilation) = RunGeneratorAndUpdateCompilation(
            new ResultAccessorGenerator().AsSourceGenerator(),
            """
            using Lib.Db.Contracts.Mapping;

            [DbResult]
            public partial class KeywordResult
            {
                public int @class { get; set; }
                public int? @event;
            }
            """,
            parseOptions);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        Diagnostic[] compileErrors = outputCompilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        compileErrors.Should().BeEmpty();

        string generatedSource = string.Join(
            Environment.NewLine,
            runResult.GeneratedTrees.Select(t => t.GetText(TestContext.Current.CancellationToken).ToString()));

        generatedSource.Should().Contain("result.@class");
        generatedSource.Should().Contain("result.@event");
        generatedSource.Should().NotContain("result.class");
        generatedSource.Should().NotContain("result.event");
    }

    [Fact(DisplayName = "RES04: DbResult generated mapper exposes DbDataReader overload")]
    public void RES04_DbResult_ShouldGenerateDbDataReaderMapOverload()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);

        var (diagnostics, runResult, outputCompilation) = RunGeneratorAndUpdateCompilation(
            new ResultAccessorGenerator().AsSourceGenerator(),
            """
            using Lib.Db.Contracts.Mapping;

            [DbResult]
            public partial class ReaderOverloadResult
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            """,
            parseOptions);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        Diagnostic[] compileErrors = outputCompilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        compileErrors.Should().BeEmpty();

        string generatedSource = string.Join(
            Environment.NewLine,
            runResult.GeneratedTrees.Select(t => t.GetText(TestContext.Current.CancellationToken).ToString()));

        generatedSource.Should().Contain("public static global::ReaderOverloadResult Map(DbDataReader reader)");
        generatedSource.Should().Contain("public static global::ReaderOverloadResult Map(SqlDataReader reader) => Map((DbDataReader)reader);");
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult) RunGenerator(
        ISourceGenerator generator,
        string source,
        CSharpParseOptions parseOptions,
        AdditionalText[]? additionalTexts = null)
    {
        var (diagnostics, runResult, _) = RunGeneratorAndUpdateCompilation(
            generator,
            source,
            parseOptions,
            additionalTexts);

        return (diagnostics, runResult);
    }

    private static (
        ImmutableArray<Diagnostic> Diagnostics,
        GeneratorDriverRunResult RunResult,
        Compilation OutputCompilation) RunGeneratorAndUpdateCompilation(
        ISourceGenerator generator,
        string source,
        CSharpParseOptions parseOptions,
        AdditionalText[]? additionalTexts = null)
    {
        CSharpCompilation compilation = CreateCompilation(source, parseOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            additionalTexts: additionalTexts ?? Array.Empty<AdditionalText>(),
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics,
            TestContext.Current.CancellationToken);

        return (diagnostics, driver.GetRunResult(), outputCompilation);
    }

    private static CSharpCompilation CreateCompilation(string source, CSharpParseOptions parseOptions)
    {
        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        trustedPlatformAssemblies.Should().NotBeNullOrWhiteSpace();

        IEnumerable<MetadataReference> references = trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Append(typeof(GenerateTvpFromDbAttribute).GetTypeInfo().Assembly.Location)
            .Append(typeof(SqlDataReader).GetTypeInfo().Assembly.Location)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));

        return CSharpCompilation.Create(
            "DbFirstTvpGeneratorTests",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static void AssertGeneratedSqlTypePatterns(
        string generatedSource,
        IReadOnlyList<string> expectedPatterns)
    {
        MatchCollection matches = Regex.Matches(
            generatedSource,
            @"if \(col\.SqlDbType is not \(([^)]*)\)\)");

        string[] actualPatterns = matches
            .Select(match => match.Groups[1].Value)
            .ToArray();

        actualPatterns.Should().HaveCount(expectedPatterns.Count);
        actualPatterns.OrderBy(static value => value, StringComparer.Ordinal)
            .Should()
            .Equal(expectedPatterns.OrderBy(static value => value, StringComparer.Ordinal));
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
