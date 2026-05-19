// ============================================================================
// 파일: Infrastructure/TvpMatrixProcedureHarness.cs
// 설명: v2.3.0 TVP matrix 저장 프로시저 자동 실행 harness
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Infrastructure;

public static class TvpMatrixProcedureHarness
{
    private const int CommandTimeoutSeconds = 20;
    private static int s_valueSeed = 230_000;

    public static async Task<TvpMatrixRunSummary> ExecuteAllAsync(
        IProcedureStage database,
        string connectionString,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TvpProcedureMetadata> procedures = await DiscoverProceduresAsync(
            connectionString,
            cancellationToken).ConfigureAwait(false);

        List<string> unexpectedFailures = [];
        int executed = 0;
        int expectedFailures = 0;

        foreach (TvpProcedureMetadata procedure in procedures)
        {
            await ResetKnownMatrixStateAsync(database, procedure, cancellationToken).ConfigureAwait(false);

            Dictionary<string, object?> parameters = await BuildParametersAsync(
                connectionString,
                procedure,
                cancellationToken).ConfigureAwait(false);

            bool expectsFailure = IsExpectedChaosFailure(procedure);
            DbResult<int> result;

            try
            {
                result = await database
                    .Procedure(procedure.QualifiedName)
                    .WithTimeout(CommandTimeoutSeconds)
                    .With(parameters)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                unexpectedFailures.Add($"{procedure.QualifiedName}: {ex.GetType().Name}: {ex.Message}");
                executed++;
                continue;
            }

            executed++;

            if (expectsFailure)
            {
                if (result.IsSuccess)
                    unexpectedFailures.Add($"{procedure.QualifiedName}: expected a controlled chaos failure but succeeded.");
                else
                    expectedFailures++;

                continue;
            }

            if (!result.IsSuccess)
                unexpectedFailures.Add($"{procedure.QualifiedName}: {result.Error?.Kind}: {result.Error?.Message}");
        }

        return new TvpMatrixRunSummary(
            procedures.Count,
            executed,
            expectedFailures,
            unexpectedFailures);
    }

    private static async Task<IReadOnlyList<TvpProcedureMetadata>> DiscoverProceduresAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        List<ProcedureParameterMetadata> rows = [];

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SCHEMA_NAME(p.[schema_id]) AS [SchemaName],
                p.[name] AS [ProcedureName],
                prm.[name] AS [ParameterName],
                prm.[parameter_id] AS [ParameterId],
                prm.[is_output] AS [IsOutput],
                TYPE_NAME(prm.[system_type_id]) AS [SystemTypeName],
                prm.[max_length] AS [MaxLength],
                prm.[precision] AS [Precision],
                prm.[scale] AS [Scale],
                SCHEMA_NAME(tt.[schema_id]) AS [TableTypeSchema],
                tt.[name] AS [TableTypeName]
            FROM sys.procedures AS p
            LEFT JOIN sys.parameters AS prm
                ON prm.[object_id] = p.[object_id]
               AND prm.[parameter_id] > 0
            LEFT JOIN sys.table_types AS tt
                ON tt.[user_type_id] = prm.[user_type_id]
            WHERE p.[is_ms_shipped] = 0
              AND
              (
                  p.[name] LIKE N'%Tvp%'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM sys.parameters AS tvp
                      INNER JOIN sys.table_types AS tvpt
                          ON tvpt.[user_type_id] = tvp.[user_type_id]
                      WHERE tvp.[object_id] = p.[object_id]
                  )
              )
            ORDER BY [SchemaName], [ProcedureName], prm.[parameter_id];
            """;

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ProcedureParameterMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                !reader.IsDBNull(4) && reader.GetBoolean(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.IsDBNull(6) ? (short)0 : reader.GetInt16(6),
                reader.IsDBNull(7) ? (byte)0 : reader.GetByte(7),
                reader.IsDBNull(8) ? (byte)0 : reader.GetByte(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return rows
            .GroupBy(row => (row.SchemaName, row.ProcedureName))
            .Select(group => new TvpProcedureMetadata(
                group.Key.SchemaName,
                group.Key.ProcedureName,
                group
                    .Where(parameter => parameter.ParameterId > 0)
                    .OrderBy(parameter => parameter.ParameterId)
                    .ToArray()))
            .OrderBy(procedure => procedure.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(procedure => procedure.ProcedureName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<Dictionary<string, object?>> BuildParametersAsync(
        string connectionString,
        TvpProcedureMetadata procedure,
        CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = new(StringComparer.OrdinalIgnoreCase);

        foreach (ProcedureParameterMetadata parameter in procedure.Parameters)
        {
            if (parameter.IsOutput)
                continue;

            string key = parameter.ParameterName.TrimStart('@');
            if (parameter.IsTableType)
            {
                parameters[key] = await BuildTvpDataTableAsync(
                    connectionString,
                    procedure,
                    parameter,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                parameters[key] = CreateScalarValue(procedure, parameter);
            }
        }

        return parameters;
    }

    private static async Task<DataTable> BuildTvpDataTableAsync(
        string connectionString,
        TvpProcedureMetadata procedure,
        ProcedureParameterMetadata parameter,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TableTypeColumnMetadata> columns = await LoadTableTypeColumnsAsync(
            connectionString,
            parameter.TableTypeSchema!,
            parameter.TableTypeName!,
            cancellationToken).ConfigureAwait(false);

        DataTable table = new($"{parameter.TableTypeSchema}.{parameter.TableTypeName}");
        foreach (TableTypeColumnMetadata column in columns)
        {
            DataColumn dataColumn = new(column.Name, GetClrType(column.SystemTypeName))
            {
                AllowDBNull = column.IsNullable
            };
            table.Columns.Add(dataColumn);
        }

        int rowCount = GetTvpRowCount(procedure);
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            DataRow row = table.NewRow();
            foreach (TableTypeColumnMetadata column in columns)
                row[column.Name] = CreateColumnValue(procedure, parameter, column, rowIndex);

            table.Rows.Add(row);
        }

        return table;
    }

    private static async Task<IReadOnlyList<TableTypeColumnMetadata>> LoadTableTypeColumnsAsync(
        string connectionString,
        string schemaName,
        string typeName,
        CancellationToken cancellationToken)
    {
        List<TableTypeColumnMetadata> columns = [];

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                c.[name] AS [ColumnName],
                TYPE_NAME(c.[system_type_id]) AS [SystemTypeName],
                c.[max_length] AS [MaxLength],
                c.[precision] AS [Precision],
                c.[scale] AS [Scale],
                c.[is_nullable] AS [IsNullable]
            FROM sys.table_types AS tt
            INNER JOIN sys.columns AS c
                ON c.[object_id] = tt.[type_table_object_id]
            WHERE SCHEMA_NAME(tt.[schema_id]) = @SchemaName
              AND tt.[name] = @TypeName
            ORDER BY c.[column_id];
            """;
        command.Parameters.AddWithValue("@SchemaName", schemaName);
        command.Parameters.AddWithValue("@TypeName", typeName);

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(new TableTypeColumnMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt16(2),
                reader.GetByte(3),
                reader.GetByte(4),
                reader.GetBoolean(5)));
        }

        return columns;
    }

    private static async Task ResetKnownMatrixStateAsync(
        IProcedureStage database,
        TvpProcedureMetadata procedure,
        CancellationToken cancellationToken)
    {
        string? resetProcedure = GetResetProcedureName(procedure);
        if (resetProcedure is null || string.Equals(procedure.QualifiedName, resetProcedure, StringComparison.OrdinalIgnoreCase))
            return;

        DbResult<int> result = await database
            .Procedure(resetProcedure)
            .WithTimeout(CommandTimeoutSeconds)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    private static string? GetResetProcedureName(TvpProcedureMetadata procedure)
    {
        if (string.Equals(procedure.SchemaName, "stress", StringComparison.OrdinalIgnoreCase))
            return "[stress].[usp_Tvp_ResetMatrixData]";

        if (string.Equals(procedure.SchemaName, "chaos", StringComparison.OrdinalIgnoreCase))
            return "[chaos].[usp_Tvp_ResetChaosMatrixData]";

        if (procedure.ProcedureName.StartsWith("libdb_bench_", StringComparison.OrdinalIgnoreCase))
            return "[dbo].[libdb_bench_ResetBenchmarkMatrix]";

        return null;
    }

    private static bool IsExpectedChaosFailure(TvpProcedureMetadata procedure)
    {
        if (!string.Equals(procedure.SchemaName, "chaos", StringComparison.OrdinalIgnoreCase))
            return false;

        string name = procedure.ProcedureName;
        return name.Contains("InsertThenRollback", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("DuplicateKeyViolation", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("NotNullViolation", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ForeignKeyViolation", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ConversionFailure", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PartialFailure", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("RetryableError", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("RaiseOnLarge", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ThrowAfterResultset", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetTvpRowCount(TvpProcedureMetadata procedure)
    {
        if (procedure.ProcedureName.Contains("ZeroRows", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (procedure.ProcedureName.Contains("DuplicateKeyViolation", StringComparison.OrdinalIgnoreCase))
            return 2;

        return 1;
    }

    private static object CreateScalarValue(TvpProcedureMetadata procedure, ProcedureParameterMetadata parameter)
    {
        string name = parameter.ParameterName.TrimStart('@');
        string lowerName = name.ToLowerInvariant();

        return lowerName switch
        {
            "tenantid" => 230,
            "scenarioname" => "v230-matrix",
            "requestedby" => "v230-matrix",
            "workercount" => 1,
            "runid" => 1L,
            "take" => 1,
            "offset" => 0,
            "fetch" => 1,
            "holdmilliseconds" => 0,
            "delaymilliseconds" => 0,
            "locktimeoutmilliseconds" => 1000,
            "maxbytes" => procedure.ProcedureName.Contains("RaiseOnLarge", StringComparison.OrdinalIgnoreCase) ? 1 : 1_000_000,
            "resource" => $"libdb-v230-matrix-{NextSeed()}",
            "methodname" => "v230-matrix",
            "shapename" => "tvp",
            "rowcount" => 1,
            "metricname" => "rows",
            "metricvalue" => 1.0m,
            "codeprefix" => "CODE",
            "orderid" => NextSeed(),
            _ => CreateValueBySqlType(procedure, parameter.SystemTypeName, name, 0, parameter.MaxLength)
        };
    }

    private static object CreateColumnValue(
        TvpProcedureMetadata procedure,
        ProcedureParameterMetadata parameter,
        TableTypeColumnMetadata column,
        int rowIndex)
    {
        string columnName = column.Name;
        string lowerName = columnName.ToLowerInvariant();

        if (procedure.ProcedureName.Contains("NotNullViolation", StringComparison.OrdinalIgnoreCase) &&
            lowerName.Contains("required", StringComparison.Ordinal))
        {
            return DBNull.Value;
        }

        if (column.IsNullable && lowerName.StartsWith("optional", StringComparison.Ordinal))
            return DBNull.Value;

        if (lowerName is "numerictext")
        {
            return procedure.ProcedureName.Contains("ConversionFailure", StringComparison.OrdinalIgnoreCase) ||
                   procedure.ProcedureName.Contains("PartialFailure", StringComparison.OrdinalIgnoreCase)
                ? "not-a-number"
                : "42";
        }

        if (lowerName is "parentid")
        {
            return procedure.ProcedureName.Contains("ForeignKeyViolation", StringComparison.OrdinalIgnoreCase)
                ? 999_999
                : 1;
        }

        if (lowerName is "id" && procedure.ProcedureName.Contains("DuplicateKeyViolation", StringComparison.OrdinalIgnoreCase))
            return 930_000;

        if (lowerName is "payload" && IsTextType(column.SystemTypeName))
        {
            return procedure.ProcedureName.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                   parameter.TableTypeName?.Contains("Json", StringComparison.OrdinalIgnoreCase) == true
                ? """{"name":"v230-matrix","ok":true}"""
                : "v230-matrix-payload";
        }

        if (lowerName is "jsonpath")
            return "$.name";

        if (lowerName is "code")
            return $"CODE-{NextSeed()}";

        if (lowerName is "sku")
            return $"SKU-{NextSeed()}";

        if (lowerName is "title" or "valuetext" or "note" or "notes")
            return $"v230-matrix-{columnName}";

        return CreateValueBySqlType(procedure, column.SystemTypeName, columnName, rowIndex, column.MaxLength);
    }

    private static object CreateValueBySqlType(
        TvpProcedureMetadata procedure,
        string systemTypeName,
        string valueName,
        int rowIndex,
        short maxLength)
    {
        int seed = NextSeed() + rowIndex;
        string lowerType = systemTypeName.ToLowerInvariant();
        string lowerName = valueName.ToLowerInvariant();

        if (lowerName is "entityid" or "headerid" or "childid" or "lineid")
            return seed;

        if (lowerName is "tenantid")
            return 230;

        if (lowerName is "revision")
            return 1;

        if (lowerName is "qty" or "quantity" or "delta" or "warehouseid" or "n01" or "n02")
            return 1;

        return lowerType switch
        {
            "bigint" => (long)seed,
            "int" => seed,
            "smallint" => (short)Math.Clamp(seed % short.MaxValue, 1, short.MaxValue),
            "tinyint" => (byte)Math.Clamp(seed % byte.MaxValue, 1, byte.MaxValue),
            "bit" => false,
            "decimal" or "numeric" or "money" or "smallmoney" => 12.34m,
            "float" => 1.25d,
            "real" => 1.25f,
            "date" => new DateTime(2026, 5, 19),
            "datetime" or "datetime2" or "smalldatetime" => new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc),
            "datetimeoffset" => new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero),
            "time" => TimeSpan.FromSeconds(1),
            "uniqueidentifier" => GuidFromSeed(seed),
            "binary" or "varbinary" or "image" => CreateBinary(maxLength),
            _ when IsTextType(lowerType) => CreateString(procedure, valueName, maxLength),
            _ => seed
        };
    }

    private static Type GetClrType(string systemTypeName)
    {
        return systemTypeName.ToLowerInvariant() switch
        {
            "bigint" => typeof(long),
            "int" => typeof(int),
            "smallint" => typeof(short),
            "tinyint" => typeof(byte),
            "bit" => typeof(bool),
            "decimal" or "numeric" or "money" or "smallmoney" => typeof(decimal),
            "float" => typeof(double),
            "real" => typeof(float),
            "date" or "datetime" or "datetime2" or "smalldatetime" => typeof(DateTime),
            "datetimeoffset" => typeof(DateTimeOffset),
            "time" => typeof(TimeSpan),
            "uniqueidentifier" => typeof(Guid),
            "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => typeof(byte[]),
            _ => typeof(string)
        };
    }

    private static bool IsTextType(string systemTypeName)
    {
        string lower = systemTypeName.ToLowerInvariant();
        return lower is "char" or "varchar" or "text" or "nchar" or "nvarchar" or "ntext" or "xml" or "sysname";
    }

    private static string CreateString(TvpProcedureMetadata procedure, string valueName, short maxLength)
    {
        if (valueName.Contains("payload", StringComparison.OrdinalIgnoreCase) &&
            procedure.ProcedureName.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return """{"name":"v230-matrix","ok":true}""";
        }

        string value = $"{valueName}-{NextSeed()}";
        int maxCharacters = maxLength <= 0 ? 64 : Math.Max(1, maxLength / 2);
        return value.Length <= maxCharacters ? value : value[..maxCharacters];
    }

    private static byte[] CreateBinary(short maxLength)
    {
        int length = maxLength is > 0 and <= 8000 ? Math.Min(maxLength, (short)32) : 32;
        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i + 1);

        return bytes;
    }

    private static Guid GuidFromSeed(int seed)
    {
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(seed).CopyTo(bytes, 0);
        BitConverter.GetBytes(230).CopyTo(bytes, 4);
        return new Guid(bytes);
    }

    private static int NextSeed()
        => Interlocked.Increment(ref s_valueSeed);

    private sealed record TvpProcedureMetadata(
        string SchemaName,
        string ProcedureName,
        IReadOnlyList<ProcedureParameterMetadata> Parameters)
    {
        public string QualifiedName => $"{QuoteName(SchemaName)}.{QuoteName(ProcedureName)}";

        private static string QuoteName(string value)
            => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private sealed record ProcedureParameterMetadata(
        string SchemaName,
        string ProcedureName,
        string ParameterName,
        int ParameterId,
        bool IsOutput,
        string SystemTypeName,
        short MaxLength,
        byte Precision,
        byte Scale,
        string? TableTypeSchema,
        string? TableTypeName)
    {
        public bool IsTableType => TableTypeSchema is not null && TableTypeName is not null;
    }

    private sealed record TableTypeColumnMetadata(
        string Name,
        string SystemTypeName,
        short MaxLength,
        byte Precision,
        byte Scale,
        bool IsNullable);
}

public sealed record TvpMatrixRunSummary(
    int DiscoveredProcedures,
    int ExecutedProcedures,
    int ExpectedFailures,
    IReadOnlyList<string> UnexpectedFailures);
