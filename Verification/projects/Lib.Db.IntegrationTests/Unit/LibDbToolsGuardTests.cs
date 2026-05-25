// ============================================================================
// 파일: Unit/LibDbToolsGuardTests.cs
// 설명: Lib.Db.Tools no-DB 명령 guard 회귀 테스트
// 대상: .NET 10
// ============================================================================

using Lib.Db.Tools;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class LibDbToolsGuardTests
{
    [Theory]
    [InlineData("migrate apply")]
    [InlineData("contract apply")]
    [InlineData("sql exec")]
    [InlineData("contract inspect --execute")]
    [InlineData("contract inspect --query SELECT name FROM sys.objects --dry-run")]
    [InlineData("contract scaffold --contracts contract.json")]
    public async Task Tool_ShouldRejectUnsupportedCommandsWithoutSqlExecution(string commandLine)
    {
        RecordingToolConsole console = new();

        int exitCode = await LibDbToolsApplication.RunAsync(
            SplitCommandLine(commandLine),
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(2, console.Output);
        console.Output.Should().Contain("Unsupported command");
    }

    [Fact]
    public async Task Tool_ShouldRejectConnectionBearingValidateWithoutEchoingValue()
    {
        RecordingToolConsole console = new();
        const string connectionValue = "Server=prod-sql.internal;Database=Ledger;Password=fixture-secret";

        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "validate", "--connection", connectionValue],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(2, console.Output);
        console.Output.Should().Contain("Unsupported command");
        console.Output.Should().NotContain(connectionValue);
        console.Output.Should().NotContain("fixture-secret");
    }

    [Fact]
    public async Task Tool_ShouldRejectUnexpectedArguments()
    {
        RecordingToolConsole console = new();

        int exitCode = await LibDbToolsApplication.RunAsync(
            ["contract", "report", "--contracts", "contract.json", "--format", "markdown", "--out", "report.md", "unexpected"],
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(2, console.Output);
        console.Output.Should().Contain("Unsupported command");
    }

    [Theory]
    [InlineData("contract validate --expected expected.json --actual actual.json --expected other.json --format json --out report.json")]
    [InlineData("contract validate --expected expected.json --actual actual.json --format json --format markdown --out report.json")]
    [InlineData("contract report --contracts contract.json --contracts other.json --format markdown --out report.md")]
    [InlineData("contract report --contracts contract.json --format markdown --out report.md --out other.md")]
    public async Task Tool_ShouldRejectDuplicateOptions(string commandLine)
    {
        RecordingToolConsole console = new();

        int exitCode = await LibDbToolsApplication.RunAsync(
            SplitCommandLine(commandLine),
            console,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(2, console.Output);
        console.Output.Should().Contain("Unsupported command");
    }

    private static string[] SplitCommandLine(string commandLine) =>
        commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed class RecordingToolConsole : IToolConsole
    {
        private readonly List<string> _lines = [];

        public string Output => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message) => _lines.Add(message);

        public void WriteError(string message) => _lines.Add(message);
    }

}
