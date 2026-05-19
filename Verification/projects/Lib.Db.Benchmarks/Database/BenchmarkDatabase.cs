// ============================================================================
// 파일: Benchmarks/Lib.Db.Benchmarks/Database/BenchmarkDatabase.cs
// 설명: 실제 SQL Server 벤치마크용 DDL 초기화
// 대상: .NET 10 / C# 14
// ============================================================================

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.RegularExpressions;

namespace Lib.Db.Benchmarks.Database;

public enum BenchmarkDatabaseSetupMode
{
    NarrowWideOnly,
    FullMatrix
}

public static partial class BenchmarkDatabase
{
    private const string ConnectionStringName = "Benchmark";
    private const string EnvironmentVariableName = "LIBDB_BENCHMARK_CONNECTION";
    private const string AllowResetEnvironmentVariableName = "LIBDB_BENCHMARK_ALLOW_RESET";
    private const string FullMatrixScriptFileName = "setup-libdb-bench-test.sql";
    private const string ResetNarrowWideBenchmarkObjectsSql = """
        IF OBJECT_ID('[dbo].[libdb_bench_InsertWideOrderItems]', 'P') IS NOT NULL
            DROP PROCEDURE [dbo].[libdb_bench_InsertWideOrderItems];

        IF OBJECT_ID('[dbo].[libdb_bench_InsertMultiOrderGraph]', 'P') IS NOT NULL
            DROP PROCEDURE [dbo].[libdb_bench_InsertMultiOrderGraph];

        IF OBJECT_ID('[dbo].[libdb_bench_InsertOrderItems]', 'P') IS NOT NULL
            DROP PROCEDURE [dbo].[libdb_bench_InsertOrderItems];

        IF OBJECT_ID('[dbo].[libdb_bench_WideOrderItems]', 'U') IS NOT NULL
            DROP TABLE [dbo].[libdb_bench_WideOrderItems];

        IF OBJECT_ID('[dbo].[libdb_bench_OrderItems]', 'U') IS NOT NULL
            DROP TABLE [dbo].[libdb_bench_OrderItems];

        IF TYPE_ID(N'[dbo].[libdb_bench_WideOrderItem]') IS NOT NULL
            DROP TYPE [dbo].[libdb_bench_WideOrderItem];

        IF TYPE_ID(N'[dbo].[libdb_bench_OrderItem]') IS NOT NULL
            DROP TYPE [dbo].[libdb_bench_OrderItem];

        CREATE TYPE [dbo].[libdb_bench_OrderItem] AS TABLE
        (
            [Id] INT NOT NULL,
            [Sku] NVARCHAR(64) NOT NULL,
            [Qty] INT NOT NULL,
            [Price] DECIMAL(18, 2) NOT NULL
        );

        CREATE TABLE [dbo].[libdb_bench_OrderItems]
        (
            [OrderId] INT NOT NULL,
            [RequestedBy] NVARCHAR(64) NOT NULL,
            [Id] INT NOT NULL,
            [Sku] NVARCHAR(64) NOT NULL,
            [Qty] INT NOT NULL,
            [Price] DECIMAL(18, 2) NOT NULL,
            [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_OrderItems_CreatedAt] DEFAULT SYSUTCDATETIME()
        );

        CREATE TYPE [dbo].[libdb_bench_WideOrderItem] AS TABLE
        (
            [Id] INT NOT NULL,
            [Sku] NVARCHAR(64) NOT NULL,
            [Qty] INT NOT NULL,
            [Price] DECIMAL(18, 2) NOT NULL,
            [Discount] DECIMAL(18, 2) NOT NULL,
            [Tax] DECIMAL(18, 2) NOT NULL,
            [LineTotal] DECIMAL(18, 2) NOT NULL,
            [IsGift] BIT NOT NULL,
            [WarehouseId] INT NOT NULL,
            [Region] NVARCHAR(16) NOT NULL,
            [BatchId] UNIQUEIDENTIFIER NOT NULL,
            [RequestedAt] DATETIME2(7) NOT NULL,
            [SequenceNumber] BIGINT NOT NULL,
            [Priority] SMALLINT NOT NULL,
            [Status] TINYINT NOT NULL,
            [Note] NVARCHAR(128) NOT NULL
        );

        CREATE TABLE [dbo].[libdb_bench_WideOrderItems]
        (
            [OrderId] INT NOT NULL,
            [RequestedBy] NVARCHAR(64) NOT NULL,
            [Id] INT NOT NULL,
            [Sku] NVARCHAR(64) NOT NULL,
            [Qty] INT NOT NULL,
            [Price] DECIMAL(18, 2) NOT NULL,
            [Discount] DECIMAL(18, 2) NOT NULL,
            [Tax] DECIMAL(18, 2) NOT NULL,
            [LineTotal] DECIMAL(18, 2) NOT NULL,
            [IsGift] BIT NOT NULL,
            [WarehouseId] INT NOT NULL,
            [Region] NVARCHAR(16) NOT NULL,
            [BatchId] UNIQUEIDENTIFIER NOT NULL,
            [RequestedAt] DATETIME2(7) NOT NULL,
            [SequenceNumber] BIGINT NOT NULL,
            [Priority] SMALLINT NOT NULL,
            [Status] TINYINT NOT NULL,
            [Note] NVARCHAR(128) NOT NULL,
            [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_WideOrderItems_CreatedAt] DEFAULT SYSUTCDATETIME()
        );
        """;

    private const string CreateOrderItemsProcedureSql = """
        CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertOrderItems]
            @OrderId INT,
            @RequestedBy NVARCHAR(64),
            @Rows [dbo].[libdb_bench_OrderItem] READONLY
        AS
        BEGIN
            SET NOCOUNT ON;

            INSERT INTO [dbo].[libdb_bench_OrderItems] ([OrderId], [RequestedBy], [Id], [Sku], [Qty], [Price])
            SELECT @OrderId, @RequestedBy, [Id], [Sku], [Qty], [Price]
            FROM @Rows;

            SELECT COUNT_BIG(*) AS [InsertedCount]
            FROM [dbo].[libdb_bench_OrderItems]
            WHERE [OrderId] = @OrderId;
        END
        """;

    private const string CreateWideOrderItemsProcedureSql = """
        CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertWideOrderItems]
            @OrderId INT,
            @RequestedBy NVARCHAR(64),
            @Rows [dbo].[libdb_bench_WideOrderItem] READONLY
        AS
        BEGIN
            SET NOCOUNT ON;

            INSERT INTO [dbo].[libdb_bench_WideOrderItems]
            (
                [OrderId], [RequestedBy], [Id], [Sku], [Qty], [Price], [Discount], [Tax],
                [LineTotal], [IsGift], [WarehouseId], [Region], [BatchId], [RequestedAt],
                [SequenceNumber], [Priority], [Status], [Note]
            )
            SELECT
                @OrderId, @RequestedBy, [Id], [Sku], [Qty], [Price], [Discount], [Tax],
                [LineTotal], [IsGift], [WarehouseId], [Region], [BatchId], [RequestedAt],
                [SequenceNumber], [Priority], [Status], [Note]
            FROM @Rows;

            SELECT COUNT_BIG(*) AS [InsertedCount]
            FROM [dbo].[libdb_bench_WideOrderItems]
            WHERE [OrderId] = @OrderId;
        END
        """;

    public static string GetConnectionString()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        string? configured = configuration.GetConnectionString(ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        string? environment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environment))
            return environment;

        throw new InvalidOperationException(
            $"Benchmark connection string is not configured. Set ConnectionStrings:{ConnectionStringName} or {EnvironmentVariableName}.");
    }

    public static async Task ResetAsync(
        string connectionString,
        CancellationToken cancellationToken,
        BenchmarkDatabaseSetupMode setupMode = BenchmarkDatabaseSetupMode.NarrowWideOnly)
    {
        ValidateResetTarget(connectionString);

        if (setupMode == BenchmarkDatabaseSetupMode.FullMatrix)
        {
            await ExecuteFullMatrixSetupAsync(connectionString, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, ResetNarrowWideBenchmarkObjectsSql, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, CreateOrderItemsProcedureSql, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, CreateWideOrderItemsProcedureSql, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<DatabaseObjectInfo>> ListUserObjectsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        List<DatabaseObjectInfo> objects = [];

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT 'TABLE' AS [ObjectType], s.[name] AS [SchemaName], t.[name] AS [ObjectName]
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id]
            WHERE t.[is_ms_shipped] = 0

            UNION ALL

            SELECT 'PROCEDURE' AS [ObjectType], s.[name] AS [SchemaName], p.[name] AS [ObjectName]
            FROM sys.procedures AS p
            INNER JOIN sys.schemas AS s ON s.[schema_id] = p.[schema_id]
            WHERE p.[is_ms_shipped] = 0

            UNION ALL

            SELECT 'TABLE_TYPE' AS [ObjectType], s.[name] AS [SchemaName], tt.[name] AS [ObjectName]
            FROM sys.table_types AS tt
            INNER JOIN sys.schemas AS s ON s.[schema_id] = tt.[schema_id]
            WHERE tt.[is_user_defined] = 1

            ORDER BY [ObjectType], [SchemaName], [ObjectName];
            """;

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            objects.Add(new DatabaseObjectInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return objects;
    }

    private static void ValidateResetTarget(string connectionString)
    {
        string? allowReset = Environment.GetEnvironmentVariable(AllowResetEnvironmentVariableName);
        if (!string.Equals(allowReset, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Benchmark reset requires {AllowResetEnvironmentVariableName}=true because it drops and recreates benchmark objects.");
        }

        string catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!catalog.Contains("BENCH", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Benchmark reset requires a database name containing 'BENCH'. Refusing to run destructive benchmark DDL on the configured catalog.");
        }
    }

    public sealed record DatabaseObjectInfo(string ObjectType, string SchemaName, string ObjectName);

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteFullMatrixSetupAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        string scriptPath = ResolveFullMatrixScriptPath();
        string script = await File.ReadAllTextAsync(scriptPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        SqlConnectionStringBuilder builder = new(connectionString)
        {
            InitialCatalog = "master"
        };

        await using SqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (string batch in SplitBatches(script))
            await ExecuteAsync(connection, batch, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveFullMatrixScriptPath()
    {
        string direct = Path.Combine(AppContext.BaseDirectory, "sql", FullMatrixScriptFileName);
        if (File.Exists(direct))
            return direct;

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "Tests", "Lib.Db.IntegrationTests", "sql", FullMatrixScriptFileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Benchmark full-matrix setup script '{FullMatrixScriptFileName}' was not found.", FullMatrixScriptFileName);
    }

    private static IReadOnlyList<string> SplitBatches(string script)
    {
        List<string> batches = [];
        StringBuilder current = new();

        using StringReader reader = new(script);
        while (reader.ReadLine() is { } line)
        {
            Match go = GoBatchSeparatorRegex().Match(line);
            if (!go.Success)
            {
                if (!line.TrimStart().StartsWith(':'))
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

    [GeneratedRegex(@"^\s*GO(?:\s+(\d+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoBatchSeparatorRegex();
}
