using System.Text.Json;
using Lib.Db.Tools.Contracts;
using Lib.Db.Tools.Reporting;

namespace Lib.Db.Tools;

public static class LibDbToolsApplication
{
    public static async ValueTask<int> RunAsync(
        IReadOnlyList<string> args,
        IToolConsole console,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(console);

        if (args.Count == 0 || IsHelp(args[0]))
        {
            WriteUsage(console);
            return 0;
        }

        if (!StringComparer.Ordinal.Equals(args[0], "contract"))
            return Unsupported(console);

        if (args.Count < 2)
            return Unsupported(console);

        return args[1] switch
        {
            "validate" => await RunContractValidateAsync(args, console, cancellationToken),
            "report" => await RunContractReportAsync(args, console, cancellationToken),
            _ => Unsupported(console)
        };
    }

    private static async ValueTask<int> RunContractValidateAsync(
        IReadOnlyList<string> args,
        IToolConsole console,
        CancellationToken cancellationToken)
    {
        string[] valueOptions = ["--expected", "--actual", "--format", "--out"];
        if (HasUnexpectedArguments(args, valueOptions, []))
            return Unsupported(console);

        if (!TryGetOptionValue(args, "--expected", out string? expectedPath) ||
            !TryGetOptionValue(args, "--actual", out string? actualPath) ||
            !TryGetOptionValue(args, "--format", out string? format) ||
            !TryGetOptionValue(args, "--out", out string? outputPath))
        {
            console.WriteError("Contract validation requires --expected, --actual, --format, and --out.");
            return 2;
        }

        try
        {
            LibDbContractDocument expected = await LibDbContractSerializer.ReadAsync(expectedPath!, cancellationToken);
            LibDbContractDocument actual = await LibDbContractSerializer.ReadAsync(actualPath!, cancellationToken);
            LibDbContractValidationReport report = LibDbContractValidator.Validate(expected, actual);
            string output = CreateValidationReportOutput(report, format!);
            await WriteOutputAsync(outputPath!, output, cancellationToken);
            console.WriteLine($"Contract validation report written. No SQL executed.");
            return report.Summary.Total == 0 ? 0 : 1;
        }
        catch (Exception ex) when (IsContractCommandException(ex))
        {
            console.WriteError($"Contract validation failed: {CreateSafeErrorMessage(ex)}");
            return 1;
        }
    }

    private static async ValueTask<int> RunContractReportAsync(
        IReadOnlyList<string> args,
        IToolConsole console,
        CancellationToken cancellationToken)
    {
        string[] valueOptions = ["--contracts", "--format", "--out"];
        if (HasUnexpectedArguments(args, valueOptions, []))
            return Unsupported(console);

        if (!TryGetOptionValue(args, "--contracts", out string? contractsPath) ||
            !TryGetOptionValue(args, "--format", out string? format) ||
            !TryGetOptionValue(args, "--out", out string? outputPath))
        {
            console.WriteError("Contract report requires --contracts, --format, and --out.");
            return 2;
        }

        try
        {
            LibDbContractDocument contract = await LibDbContractSerializer.ReadAsync(contractsPath!, cancellationToken);
            string output = CreateInventoryReportOutput(contract, format!);
            await WriteOutputAsync(outputPath!, output, cancellationToken);
            console.WriteLine("Contract report written. No SQL executed.");
            return 0;
        }
        catch (Exception ex) when (IsContractCommandException(ex))
        {
            console.WriteError($"Contract report failed: {CreateSafeErrorMessage(ex)}");
            return 1;
        }
    }

    private static bool TryGetOptionValue(IReadOnlyList<string> args, string option, out string? value)
    {
        value = null;
        for (int index = 2; index < args.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(args[index], option))
                continue;

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                return false;

            value = args[index + 1];
            return true;
        }

        return false;
    }

    private static bool HasUnexpectedArguments(
        IReadOnlyList<string> args,
        IReadOnlyCollection<string> valueOptions,
        IReadOnlyCollection<string> switchOptions)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int index = 2; index < args.Count;)
        {
            string arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                return true;

            if (!seen.Add(arg))
                return true;

            if (valueOptions.Contains(arg, StringComparer.Ordinal))
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    return true;

                index += 2;
                continue;
            }

            if (switchOptions.Contains(arg, StringComparer.Ordinal))
            {
                index++;
                continue;
            }

            return true;
        }

        return false;
    }

    private static string CreateValidationReportOutput(LibDbContractValidationReport report, string format) =>
        format switch
        {
            "json" => LibDbContractSerializer.SerializeReport(report),
            "markdown" => LibDbContractReportWriter.WriteValidationMarkdown(report),
            _ => throw new LibDbContractException("Report format must be json or markdown.")
        };

    private static string CreateInventoryReportOutput(LibDbContractDocument contract, string format) =>
        format switch
        {
            "json" => LibDbContractSerializer.SerializeDocument(contract),
            "markdown" => LibDbContractReportWriter.WriteInventoryMarkdown(contract),
            _ => throw new LibDbContractException("Report format must be json or markdown.")
        };

    private static async ValueTask WriteOutputAsync(string outputPath, string output, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(outputPath, output, cancellationToken);
    }

    private static bool IsContractCommandException(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException
            or LibDbContractException;

    private static string CreateSafeErrorMessage(Exception ex) =>
        ex switch
        {
            LibDbContractException contractException => contractException.Message,
            JsonException => "Contract file is invalid JSON.",
            IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException =>
                "Contract file could not be read or written.",
            _ => "Contract command failed."
        };

    private static bool IsHelp(string arg) =>
        StringComparer.Ordinal.Equals(arg, "--help") ||
        StringComparer.Ordinal.Equals(arg, "-h") ||
        StringComparer.Ordinal.Equals(arg, "help");

    private static int Unsupported(IToolConsole console)
    {
        console.WriteError("Unsupported command. Supported commands: contract validate, contract report.");
        return 2;
    }

    private static void WriteUsage(IToolConsole console)
    {
        console.WriteLine("Lib.Db.Tools");
        console.WriteLine("Commands: contract validate --expected <path> --actual <path> --format <json|markdown> --out <path>");
        console.WriteLine("          contract report --contracts <path> --format <json|markdown> --out <path>");
    }
}
