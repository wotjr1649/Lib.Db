using System.Text;

namespace Lib.Db.Execution.Bulk;

internal static class BulkSqlBuilder
{
    public static string CreateStageTable<T>(string stageTableName, IReadOnlyList<BulkColumn<T>> columns)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(columns);
        ValidateStageTableName(stageTableName);

        if (columns.Count == 0)
            throw new InvalidOperationException("Bulk stage table requires at least one column.");

        StringBuilder sql = new();
        sql.Append("CREATE TABLE ").Append(stageTableName).AppendLine(" (");

        for (int i = 0; i < columns.Count; i++)
        {
            BulkColumn<T> column = columns[i];
            sql.Append("    ")
                .Append(BulkIdentifier.Quote(column.DestinationName))
                .Append(' ')
                .Append(BulkSqlTypeRenderer.Render(column))
                .Append(column.Nullable ? " NULL" : " NOT NULL");

            if (i + 1 < columns.Count)
                sql.Append(',');

            sql.AppendLine();
        }

        sql.Append(");");
        return sql.ToString();
    }

    public static string CreateUniqueStageKeyIndex<T>(string stageTableName, BulkShape<T> shape)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(shape);
        ValidateStageTableName(stageTableName);

        if (shape.KeyColumns.Count == 0)
            throw new InvalidOperationException("Bulk stage key index requires at least one key column.");

        string keyColumns = string.Join(", ", shape.KeyColumns.Select(static column => BulkIdentifier.Quote(column.DestinationName)));
        return $"CREATE UNIQUE INDEX [IX_LibDbBulk_Key] ON {stageTableName} ({keyColumns});";
    }

    public static string UpdateFromStage<T>(BulkIdentifier destination, string stageTableName, BulkShape<T> shape)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(shape);
        ValidateStageTableName(stageTableName);

        if (shape.KeyColumns.Count == 0)
            throw new InvalidOperationException("Bulk update requires at least one key column.");

        if (shape.WritableColumns.Count == 0)
            throw new InvalidOperationException("Bulk update requires at least one non-key column.");

        string setClause = string.Join(", ", shape.WritableColumns.Select(static column =>
            $"target.{BulkIdentifier.Quote(column.DestinationName)} = source.{BulkIdentifier.Quote(column.DestinationName)}"));
        string joinClause = JoinOnKeys(shape);

        return $"UPDATE target SET {setClause} FROM {destination.ToSql()} AS target INNER JOIN {stageTableName} AS source ON {joinClause};";
    }

    public static string DeleteFromStage<T>(BulkIdentifier destination, string stageTableName, BulkShape<T> shape)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(shape);
        ValidateStageTableName(stageTableName);

        if (shape.KeyColumns.Count == 0)
            throw new InvalidOperationException("Bulk delete requires at least one key column.");

        string joinClause = JoinOnKeys(shape);
        return $"DELETE target FROM {destination.ToSql()} AS target INNER JOIN {stageTableName} AS source ON {joinClause};";
    }

    private static string JoinOnKeys<T>(BulkShape<T> shape)
        where T : notnull
        => string.Join(" AND ", shape.KeyColumns.Select(static column =>
            $"target.{BulkIdentifier.Quote(column.DestinationName)} = source.{BulkIdentifier.Quote(column.DestinationName)}"));

    private static void ValidateStageTableName(string stageTableName)
    {
        if (string.IsNullOrWhiteSpace(stageTableName))
            throw new ArgumentException("Stage table name cannot be empty.", nameof(stageTableName));

        if (!string.Equals(stageTableName, stageTableName.Trim(), StringComparison.Ordinal)
            || stageTableName.Any(char.IsWhiteSpace)
            || stageTableName.Contains(';', StringComparison.Ordinal)
            || stageTableName.Contains("--", StringComparison.Ordinal)
            || stageTableName.Contains("/*", StringComparison.Ordinal)
            || stageTableName.Contains("*/", StringComparison.Ordinal)
            || stageTableName.Contains('[', StringComparison.Ordinal)
            || stageTableName.Contains(']', StringComparison.Ordinal)
            || stageTableName.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException("Stage table name contains unsupported SQL identifier syntax.", nameof(stageTableName));
        }

        if (stageTableName.Length > 128)
            throw new ArgumentException("Stage table name cannot exceed 128 characters.", nameof(stageTableName));

        if (!stageTableName.All(static value => char.IsAsciiLetterOrDigit(value) || value is '_' or '#'))
            throw new ArgumentException("Stage table name contains malformed identifier characters.", nameof(stageTableName));
    }
}
