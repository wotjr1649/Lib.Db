// ============================================================================
// 파일: Unit/LibDbToolsContractTests.cs
// 설명: Lib.Db.Tools no-DB contract validate/report MVP 회귀 테스트
// 대상: .NET 10
// ============================================================================

using System.Text.Json;
using Lib.Db.Tools;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class LibDbToolsContractTests
{
    [Fact]
    public async Task ContractValidate_ShouldWriteDeterministicJsonDiffReport()
    {
        using TemporaryToolRoot root = new();
        string expected = Path.Combine(root.Path, "expected.libdb.contracts.json");
        string actual = Path.Combine(root.Path, "actual.libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.json");

        await File.WriteAllTextAsync(expected, MinimalContract("dbo", "Customer_Get", parameterType: "int"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(actual, MinimalContract("dbo", "Customer_Get", parameterType: "bigint"), TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "validate", "--expected", expected, "--actual", actual, "--format", "json", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        string first = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        string second = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        first.Should().Be(second);
        first.Should().Contain("\"status\": \"Failed\"");
        first.Should().Contain("\"severity\": \"Breaking\"");
        first.Should().Contain("Procedure[dbo.Customer_Get].Parameter[@CustomerId].Type");
        first.Should().Contain("expected int but found bigint");
        console.Output.Should().Contain("No SQL executed");
    }

    [Fact]
    public async Task ContractValidate_ShouldReturnSuccessForEquivalentContracts()
    {
        using TemporaryToolRoot root = new();
        string expected = Path.Combine(root.Path, "expected.libdb.contracts.json");
        string actual = Path.Combine(root.Path, "actual.libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.json");

        string contract = MinimalContract("dbo", "Customer_Get", parameterType: "int");
        await File.WriteAllTextAsync(expected, contract, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(actual, contract, TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "validate", "--expected", expected, "--actual", actual, "--format", "json", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, console.Output);
        string output = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        output.Should().Contain("\"status\": \"Passed\"");
        output.Should().Contain("\"total\": 0");
        console.Output.Should().Contain("No SQL executed");
    }

    [Fact]
    public async Task ContractValidate_ShouldReportActualOnlyProcedureParameter()
    {
        using TemporaryToolRoot root = new();
        string expected = Path.Combine(root.Path, "expected.libdb.contracts.json");
        string actual = Path.Combine(root.Path, "actual.libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.json");

        await File.WriteAllTextAsync(expected, MinimalContract("dbo", "Customer_Get", parameterType: "int"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(actual, MinimalContractWithAdditionalParameter(), TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "validate", "--expected", expected, "--actual", actual, "--format", "json", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        string output = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        output.Should().Contain("\"status\": \"Failed\"");
        output.Should().Contain("\"severity\": \"Breaking\"");
        output.Should().Contain("Additional parameter found in actual contract");
        output.Should().Contain("@TenantId");
    }

    [Fact]
    public async Task ContractValidate_ShouldReportBulkKeyColumnMismatch()
    {
        using TemporaryToolRoot root = new();
        string expected = Path.Combine(root.Path, "expected.libdb.contracts.json");
        string actual = Path.Combine(root.Path, "actual.libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.json");

        await File.WriteAllTextAsync(expected, MinimalContract("dbo", "Customer_Get", parameterType: "int"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            actual,
            MinimalContract("dbo", "Customer_Get", parameterType: "int").Replace(
                "\"keyColumns\": [ \"CustomerId\" ]",
                "\"keyColumns\": [ \"TenantId\" ]",
                StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "validate", "--expected", expected, "--actual", actual, "--format", "json", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        string output = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        output.Should().Contain("\"status\": \"Failed\"");
        output.Should().Contain("BulkTarget[dbo.Customer].KeyColumns[0]");
        output.Should().Contain("expected CustomerId but found TenantId");
    }

    [Fact]
    public async Task ContractValidate_ShouldReportTvpColumnOrdinalMismatch()
    {
        using TemporaryToolRoot root = new();
        string expected = Path.Combine(root.Path, "expected.libdb.contracts.json");
        string actual = Path.Combine(root.Path, "actual.libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.json");

        await File.WriteAllTextAsync(expected, MinimalContract("dbo", "Customer_Get", parameterType: "int"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            actual,
            MinimalContract("dbo", "Customer_Get", parameterType: "int").Replace(
                "\"ordinal\": 1",
                "\"ordinal\": 0",
                StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "validate", "--expected", expected, "--actual", actual, "--format", "json", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        string output = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        output.Should().Contain("\"status\": \"Failed\"");
        output.Should().Contain("TableType[dbo.CustomerTvp].Column[CustomerId].Ordinal");
        output.Should().Contain("expected 1 but found 0");
    }

    [Theory]
    [InlineData("clientSecretValue")]
    [InlineData("accessTokenValue")]
    [InlineData("secretKeyName")]
    public async Task ContractReports_ShouldRedactConcatenatedSecretLikeNames(string secretLikeName)
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");

        await File.WriteAllTextAsync(
            contracts,
            MinimalContract("dbo", secretLikeName, parameterType: "int", resultShape: "Known"),
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, console.Output);
        string markdown = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        markdown.Should().Contain("<redacted>");
        markdown.Should().NotContain(secretLikeName);
    }

    [Fact]
    public async Task ContractReport_ShouldRedactConnectionStringShapedAllowedValues()
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");
        string connectionStringShapedName = $"Server=prod-sql.internal;Database=Ledger;Encrypt=True;Application Name=fixture-{Guid.NewGuid():N}";

        await File.WriteAllTextAsync(
            contracts,
            MinimalContract("dbo", connectionStringShapedName, parameterType: "int", resultShape: "Known"),
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, console.Output);
        string markdown = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        markdown.Should().Contain("<redacted>");
        markdown.Should().NotContain(connectionStringShapedName);
        markdown.Should().NotContain("prod-sql.internal");
        markdown.Should().NotContain("Ledger");
        markdown.Should().NotContain("fixture-");
    }

    [Fact]
    public async Task ContractValidate_ShouldRedactConnectionStringShapedDiffValues()
    {
        using TemporaryToolRoot root = new();
        string expected = Path.Combine(root.Path, "expected.libdb.contracts.json");
        string actual = Path.Combine(root.Path, "actual.libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");
        string connectionStringShapedType = $"Data Source=prod-sql.internal;Initial Catalog=Ledger;Encrypt=True;Application Name=fixture-{Guid.NewGuid():N}";

        await File.WriteAllTextAsync(expected, MinimalContract("dbo", "Customer_Get", parameterType: "int"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(actual, MinimalContract("dbo", "Customer_Get", parameterType: connectionStringShapedType), TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "validate", "--expected", expected, "--actual", actual, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        string markdown = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        markdown.Should().Contain("<redacted>");
        markdown.Should().NotContain(connectionStringShapedType);
        markdown.Should().NotContain("prod-sql.internal");
        markdown.Should().NotContain("Ledger");
        markdown.Should().NotContain("fixture-");
    }

    [Fact]
    public async Task ContractReport_ShouldHtmlEscapeMarkdownCells()
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");

        await File.WriteAllTextAsync(
            contracts,
            MinimalContract("dbo", "<script>alert(1)</script>", parameterType: "int", resultShape: "Known"),
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, console.Output);
        string markdown = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        markdown.Should().Contain("&lt;script&gt;");
        markdown.Should().NotContain("<script>");
    }

    [Fact]
    public async Task ContractReport_ShouldWriteMarkdownInventoryWithoutEchoingSecretLikeNames()
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");
        string secretLikeName = $"Password=fixture-{Guid.NewGuid():N}";

        await File.WriteAllTextAsync(
            contracts,
            MinimalContract("dbo", secretLikeName, parameterType: "int", resultShape: "Unknown"),
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, console.Output);
        string markdown = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        markdown.Should().StartWith("# Lib.Db Contract Report");
        markdown.Should().Contain("Procedures: 1");
        markdown.Should().Contain("Unknown result shapes: 1");
        markdown.Should().Contain("<redacted>");
        markdown.Should().NotContain(secretLikeName);
        console.Output.Should().Contain("No SQL executed");
    }

    [Fact]
    public async Task ContractReport_ShouldWriteJsonInventoryReport()
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.json");
        string secretLikeName = $"Password=fixture-{Guid.NewGuid():N}";

        await File.WriteAllTextAsync(
            contracts,
            MinimalContract("dbo", secretLikeName, parameterType: "int", resultShape: "Known"),
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "json", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0, console.Output);
        string json = await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken);
        json.Should().Contain("\"schemaVersion\": \"1\"");
        json.Should().Contain("\"procedures\"");
        json.Should().Contain("\"tableTypes\"");
        json.Should().Contain("\"bulkTargets\"");
        using JsonDocument document = JsonDocument.Parse(json);
        string? procedureName = document.RootElement
            .GetProperty("procedures")[0]
            .GetProperty("name")
            .GetString();
        procedureName.Should().Be("<redacted>");
        json.Should().NotContain(secretLikeName);
        json.Should().NotContain("fixture-");
        json.Should().NotContain("\"status\": \"Passed\"");
        console.Output.Should().Contain("No SQL executed");
    }

    [Fact]
    public async Task ContractCommands_ShouldRejectSecretBearingSchemaFieldsWithoutEchoingValues()
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");
        string secretValue = $"Server=prod-sql.internal;Database=Ledger;Password=fixture-{Guid.NewGuid():N}";

        await File.WriteAllTextAsync(
            contracts,
            $$"""
            {
              "schemaVersion": "1",
              "connectionString": "{{secretValue}}",
              "procedures": []
            }
            """,
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        File.Exists(report).Should().BeFalse();
        console.Output.Should().Contain("unsupported secret-bearing field");
        console.Output.Should().NotContain(secretValue);
        console.Output.Should().NotContain("fixture-");
    }

    [Theory]
    [InlineData("clientSecret")]
    [InlineData("accessToken")]
    [InlineData("secret_key")]
    [InlineData("api_key")]
    [InlineData("sasToken")]
    [InlineData("authorization")]
    [InlineData("credential")]
    public async Task ContractCommands_ShouldRejectSecretLikeSchemaFields(string fieldName)
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");
        string secretValue = $"fixture-{Guid.NewGuid():N}";

        await File.WriteAllTextAsync(
            contracts,
            $$"""
            {
              "schemaVersion": "1",
              "{{fieldName}}": "{{secretValue}}",
              "procedures": []
            }
            """,
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        File.Exists(report).Should().BeFalse();
        console.Output.Should().Contain("unsupported secret-bearing field");
        console.Output.Should().NotContain(secretValue);
        console.Output.Should().NotContain("fixture-");
    }

    [Fact]
    public async Task ContractCommands_ShouldRedactSecretLikeSchemaFieldNameValues()
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");
        string secretInFieldName = $"Password=fixture-{Guid.NewGuid():N}";

        await File.WriteAllTextAsync(
            contracts,
            $$"""
            {
              "schemaVersion": "1",
              "{{secretInFieldName}}": true,
              "procedures": [],
              "tableTypes": [],
              "bulkTargets": []
            }
            """,
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        File.Exists(report).Should().BeFalse();
        console.Output.Should().Contain("unsupported secret-bearing field");
        console.Output.Should().NotContain(secretInFieldName);
        console.Output.Should().NotContain("fixture-");
    }

    [Fact]
    public async Task ContractCommands_ShouldRedactSecretLikePathsFromFailureMessages()
    {
        using TemporaryToolRoot root = new();
        string secretPathSegment = $"Password=fixture-{Guid.NewGuid():N}";
        string expected = Path.Combine(root.Path, secretPathSegment, "expected.libdb.contracts.json");
        string actual = Path.Combine(root.Path, "actual.libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.json");

        await File.WriteAllTextAsync(
            actual,
            MinimalContract("dbo", "Customer_Get", parameterType: "int"),
            TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "validate", "--expected", expected, "--actual", actual, "--format", "json", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        console.Output.Should().Contain("Contract validation failed");
        console.Output.Should().NotContain(secretPathSegment);
        console.Output.Should().NotContain("fixture-");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":\"1\",\"procedures\":[],\"tableTypes\":[],\"bulkTargets\":[],\"parameterValues\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"procedures\":null}")]
    [InlineData("{\"schemaVersion\":\"1\",\"procedures\":[{\"schema\":\"dbo\",\"name\":\"Customer_Get\"},{\"schema\":\"dbo\",\"name\":\"Customer_Get\"}]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"procedures\":[{\"schema\":\"dbo\",\"name\":\"Customer_Get\",\"parameters\":null}]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"procedures\":[{\"schema\":\"dbo\",\"name\":\"Customer_Get\",\"parameters\":[],\"resultShape\":\"Maybe\"}],\"tableTypes\":[],\"bulkTargets\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"tableTypes\":null}")]
    [InlineData("{\"schemaVersion\":\"1\",\"tableTypes\":[{\"schema\":\"dbo\",\"name\":\"CustomerTvp\",\"columns\":null}]}")]
    public async Task ContractCommands_ShouldRejectMalformedContractShapeWithoutCrashing(string contractJson)
    {
        using TemporaryToolRoot root = new();
        string contracts = Path.Combine(root.Path, "libdb.contracts.json");
        string report = Path.Combine(root.Path, "contract-report.md");

        await File.WriteAllTextAsync(contracts, contractJson, TestContext.Current.CancellationToken);

        RecordingToolConsole console = new();
        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", contracts, "--format", "markdown", "--out", report],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1, console.Output);
        File.Exists(report).Should().BeFalse();
        console.Output.Should().Contain("Contract report failed");
    }

    private static string MinimalContract(
        string schema,
        string procedureName,
        string parameterType,
        string resultShape = "Known") =>
        $$"""
        {
          "schemaVersion": "1",
          "procedures": [
            {
              "schema": "{{schema}}",
              "name": "{{JsonEncodedText.Encode(procedureName)}}",
              "parameters": [
                {
                  "name": "@CustomerId",
                  "direction": "Input",
                  "type": "{{parameterType}}",
                  "nullable": false
                }
              ],
              "resultShape": "{{resultShape}}"
            }
          ],
          "tableTypes": [
            {
              "schema": "dbo",
              "name": "CustomerTvp",
              "columns": [
                {
                  "ordinal": 1,
                  "name": "CustomerId",
                  "type": "int",
                  "nullable": false
                },
                {
                  "ordinal": 2,
                  "name": "DisplayName",
                  "type": "nvarchar",
                  "nullable": true,
                  "maxLength": 100
                }
              ]
            }
          ],
          "bulkTargets": [
            {
              "schema": "dbo",
              "table": "Customer",
              "keyColumns": [ "CustomerId" ]
            }
          ]
        }
        """;

    private static string MinimalContractWithAdditionalParameter() =>
        $$"""
        {
          "schemaVersion": "1",
          "procedures": [
            {
              "schema": "dbo",
              "name": "Customer_Get",
              "parameters": [
                {
                  "name": "@CustomerId",
                  "direction": "Input",
                  "type": "int",
                  "nullable": false
                },
                {
                  "name": "@TenantId",
                  "direction": "Input",
                  "type": "int",
                  "nullable": false
                }
              ],
              "resultShape": "Known"
            }
          ],
          "tableTypes": [
            {
              "schema": "dbo",
              "name": "CustomerTvp",
              "columns": [
                {
                  "ordinal": 1,
                  "name": "CustomerId",
                  "type": "int",
                  "nullable": false
                },
                {
                  "ordinal": 2,
                  "name": "DisplayName",
                  "type": "nvarchar",
                  "nullable": true,
                  "maxLength": 100
                }
              ]
            }
          ],
          "bulkTargets": [
            {
              "schema": "dbo",
              "table": "Customer",
              "keyColumns": [ "CustomerId" ]
            }
          ]
        }
        """;

    private sealed class RecordingToolConsole : IToolConsole
    {
        private readonly List<string> _lines = [];

        public string Output => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message) => _lines.Add(message);

        public void WriteError(string message) => _lines.Add(message);
    }

    private sealed class TemporaryToolRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "libdb-tools-" + Guid.NewGuid().ToString("N"));

        public TemporaryToolRoot() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
