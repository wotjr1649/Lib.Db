// ============================================================================
// 파일: Infrastructure/SqlScriptRunner.cs
// 설명: 통합 테스트 SQL 스크립트 실행 헬퍼
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Infrastructure;

public static partial class SqlScriptRunner
{
    private const int CommandTimeoutSeconds = 180;

    public static async Task ExecuteScriptAsync(
        string connectionString,
        string scriptFileName,
        CancellationToken cancellationToken)
    {
        string scriptPath = ResolveScriptPath(scriptFileName);
        string script = await File.ReadAllTextAsync(scriptPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        SqlConnectionStringBuilder builder = new(connectionString)
        {
            InitialCatalog = "master"
        };

        await using SqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (string batch in SplitBatches(script))
            await ExecuteBatchAsync(connection, batch, cancellationToken).ConfigureAwait(false);
    }

    public static string ResolveScriptPath(string scriptFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFileName);

        string direct = Path.Combine(AppContext.BaseDirectory, "sql", scriptFileName);
        if (File.Exists(direct))
            return direct;

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "Tests", "Lib.Db.IntegrationTests", "sql", scriptFileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"SQL script '{scriptFileName}' was not found.", scriptFileName);
    }

    internal static IReadOnlyList<string> SplitBatches(string script)
    {
        List<string> batches = [];
        StringBuilder current = new();

        using StringReader reader = new(script);
        while (reader.ReadLine() is { } line)
        {
            if (line.TrimStart().StartsWith(':'))
                continue;

            System.Text.RegularExpressions.Match go = GoBatchSeparatorRegex().Match(line);
            if (!go.Success)
            {
                current.AppendLine(line);
                continue;
            }

            AddBatch(batches, current, go.Groups[1].Value);
        }

        AddBatch(batches, current, "1");
        return batches;
    }

    private static void AddBatch(List<string> batches, StringBuilder current, string repeatValue)
    {
        string batch = current.ToString().Trim();
        current.Clear();

        if (batch.Length == 0)
            return;

        int repeat = int.TryParse(repeatValue, out int parsed) && parsed > 0 ? parsed : 1;
        for (int i = 0; i < repeat; i++)
            batches.Add(batch);
    }

    private static async Task ExecuteBatchAsync(
        SqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"^\s*GO(?:\s+(\d+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoBatchSeparatorRegex();
}
