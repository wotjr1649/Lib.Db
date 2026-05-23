using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Execution.Bulk;

internal sealed class BulkWriteExecutor(IDbConnectionFactory connectionFactory)
{
    private const string BulkInsertFailureMessage = "Bulk insert failed.";
    private const string BulkUpdateFailureMessage = "Bulk update failed.";
    private const string BulkDeleteFailureMessage = "Bulk delete failed.";

    internal static Func<BulkWriteHookContext, CancellationToken, ValueTask>? BeforeCommitAsync { get; set; }

    internal static Action<BulkWriteHookContext>? RollbackAttempted { get; set; }

    internal static Func<SqlTransaction, BulkWriteHookContext, CancellationToken, ValueTask>? RollbackAsyncForTesting { get; set; }

    internal static void ResetTestHooks()
    {
        BeforeCommitAsync = null;
        RollbackAttempted = null;
        RollbackAsyncForTesting = null;
    }

    public async Task<DbResult<long>> BulkInsertAsync<T>(
        string instanceName,
        string destinationTable,
        IEnumerable<T> records,
        BulkShape<T> shape,
        BulkWriteOptions? options,
        CancellationToken ct)
        where T : notnull
    {
        string? objectName = null;

        try
        {
            BulkWriteOptions effectiveOptions = options ?? new BulkWriteOptions();
            effectiveOptions.Validate();

            BulkIdentifier destination = BulkIdentifier.ParseTableName(destinationTable);
            objectName = destination.ToSql();

            return await BulkInsertCoreAsync(
                    instanceName,
                    objectName,
                    records,
                    shape,
                    effectiveOptions,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            return DbResult<long>.Fail(CreateSqlFailure(ex, objectName, BulkInsertFailureMessage));
        }
        catch (Exception ex)
        {
            _ = ex;
            return DbResult<long>.Fail(CreateGeneralFailure(objectName, BulkInsertFailureMessage));
        }
    }

    public Task<DbResult<long>> BulkUpdateAsync<T>(
        string instanceName,
        string destinationTable,
        IEnumerable<T> records,
        BulkShape<T> shape,
        BulkWriteOptions? options,
        CancellationToken ct)
        where T : notnull
        => ExecuteStagedSingleActionResultAsync(
            instanceName,
            destinationTable,
            records,
            shape,
            options,
            BulkSqlBuilder.UpdateFromStage,
            stageKeysOnly: false,
            BulkUpdateFailureMessage,
            ct);

    public Task<DbResult<long>> BulkDeleteAsync<T>(
        string instanceName,
        string destinationTable,
        IEnumerable<T> records,
        BulkShape<T> shape,
        BulkWriteOptions? options,
        CancellationToken ct)
        where T : notnull
        => ExecuteStagedSingleActionResultAsync(
            instanceName,
            destinationTable,
            records,
            shape,
            options,
            BulkSqlBuilder.DeleteFromStage,
            stageKeysOnly: true,
            BulkDeleteFailureMessage,
            ct);

    private async Task<DbResult<long>> BulkInsertCoreAsync<T>(
        string instanceName,
        string destinationTable,
        IEnumerable<T> records,
        BulkShape<T> shape,
        BulkWriteOptions options,
        CancellationToken ct)
        where T : notnull
    {
        using BulkShapeDataReader<T> reader = new(records, shape);
        if (!reader.HasRows)
            return DbResult<long>.Ok(0);

        await using SqlConnection connection = await connectionFactory
            .CreateConnectionAsync(instanceName, ct)
            .ConfigureAwait(false);

        SqlTransaction? transaction = options.UseTransaction
            ? (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

        bool commitStarted = false;

        try
        {
            using SqlBulkCopy bulkCopy = new(connection, CreateSqlBulkCopyOptions(options), transaction)
            {
                DestinationTableName = destinationTable,
                BatchSize = options.BatchSize,
                BulkCopyTimeout = options.TimeoutSeconds,
                EnableStreaming = options.EnableStreaming
            };

            foreach (BulkColumn<T> column in shape.Columns)
                bulkCopy.ColumnMappings.Add(column.DestinationName, column.DestinationName);

            await bulkCopy.WriteToServerAsync(reader, ct).ConfigureAwait(false);

            if (transaction is not null)
            {
                ct.ThrowIfCancellationRequested();

                BulkWriteHookContext context = new(destinationTable);
                Func<BulkWriteHookContext, CancellationToken, ValueTask>? beforeCommit = BeforeCommitAsync;
                if (beforeCommit is not null)
                    await beforeCommit(context, ct).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();
                commitStarted = true;
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return DbResult<long>.Ok(reader.RowsRead);
        }
        catch (OperationCanceledException)
        {
            if (transaction is not null && !commitStarted)
                await TryRollbackAsync(transaction, destinationTable).ConfigureAwait(false);
            throw;
        }
        catch (SqlException)
        {
            if (transaction is not null && !commitStarted)
                await TryRollbackAsync(transaction, destinationTable).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            if (transaction is not null && !commitStarted)
                await TryRollbackAsync(transaction, destinationTable).ConfigureAwait(false);
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static SqlBulkCopyOptions CreateSqlBulkCopyOptions(BulkWriteOptions options)
    {
        SqlBulkCopyOptions copyOptions = SqlBulkCopyOptions.Default;
        if (options.FireTriggers)
            copyOptions |= SqlBulkCopyOptions.FireTriggers;
        if (options.KeepIdentity)
            copyOptions |= SqlBulkCopyOptions.KeepIdentity;
        if (options.CheckConstraints)
            copyOptions |= SqlBulkCopyOptions.CheckConstraints;

        return copyOptions;
    }

    private async Task<DbResult<long>> ExecuteStagedSingleActionResultAsync<T>(
        string instanceName,
        string destinationTable,
        IEnumerable<T> records,
        BulkShape<T> shape,
        BulkWriteOptions? options,
        Func<BulkIdentifier, string, BulkShape<T>, string> buildActionSql,
        bool stageKeysOnly,
        string failureMessage,
        CancellationToken ct)
        where T : notnull
    {
        string? objectName = null;

        try
        {
            BulkIdentifier destination = BulkIdentifier.ParseTableName(destinationTable);
            objectName = destination.ToSql();
            string stageTableName = CreateStageTableName();

            BulkWriteOptions effectiveOptions = options ?? new BulkWriteOptions();
            effectiveOptions.Validate();
            ValidateStagedOptions(effectiveOptions);

            long affected = await ExecuteStagedSingleActionAsync(
                    instanceName,
                    destination,
                    objectName,
                    stageTableName,
                    records,
                    shape,
                    effectiveOptions,
                    buildActionSql,
                    stageKeysOnly,
                    ct)
                .ConfigureAwait(false);

            return DbResult<long>.Ok(affected);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            return DbResult<long>.Fail(CreateSqlFailure(ex, objectName, failureMessage));
        }
        catch (Exception ex)
        {
            _ = ex;
            return DbResult<long>.Fail(CreateGeneralFailure(objectName, failureMessage));
        }
    }

    private async Task<long> ExecuteStagedSingleActionAsync<T>(
        string instanceName,
        BulkIdentifier destination,
        string destinationTable,
        string stageTableName,
        IEnumerable<T> records,
        BulkShape<T> shape,
        BulkWriteOptions options,
        Func<BulkIdentifier, string, BulkShape<T>, string> buildActionSql,
        bool stageKeysOnly,
        CancellationToken ct)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(buildActionSql);

        shape.ValidateForMutation();
        if (!stageKeysOnly && shape.WritableColumns.Count == 0)
            throw new InvalidOperationException("Bulk update requires at least one non-key column.");

        IReadOnlyList<BulkColumn<T>> stageColumns = GetStageColumns(shape, stageKeysOnly);
        using BulkShapeDataReader<T> reader = new(records, shape, stageColumns);
        if (!reader.HasRows)
            return 0;

        await using SqlConnection connection = await connectionFactory
            .CreateConnectionAsync(instanceName, ct)
            .ConfigureAwait(false);

        SqlTransaction? transaction = null;
        bool commitStarted = false;

        try
        {
            transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            string createStageSql = BulkSqlBuilder.CreateStageTable(stageTableName, stageColumns);
            await ExecuteNonQueryAsync(connection, transaction, createStageSql, options.TimeoutSeconds, ct)
                .ConfigureAwait(false);

            await BulkCopyToStageAsync(connection, transaction, stageTableName, reader, stageColumns, options, ct)
                .ConfigureAwait(false);

            string createKeyIndexSql = BulkSqlBuilder.CreateUniqueStageKeyIndex(stageTableName, shape);
            await ExecuteNonQueryAsync(connection, transaction, createKeyIndexSql, options.TimeoutSeconds, ct)
                .ConfigureAwait(false);

            string actionSql = buildActionSql(destination, stageTableName, shape);
            long affectedRows = await ExecuteNonQueryAsync(connection, transaction, actionSql, options.TimeoutSeconds, ct)
                .ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            await TryDropStageTableAsync(connection, transaction, stageTableName, options.TimeoutSeconds)
                .ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            BulkWriteHookContext context = new(destinationTable);
            Func<BulkWriteHookContext, CancellationToken, ValueTask>? beforeCommit = BeforeCommitAsync;
            if (beforeCommit is not null)
                await beforeCommit(context, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            commitStarted = true;
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

            return affectedRows;
        }
        catch (OperationCanceledException)
        {
            if (transaction is not null && !commitStarted)
                await TryRollbackAsync(transaction, destinationTable).ConfigureAwait(false);
            throw;
        }
        catch (SqlException)
        {
            if (transaction is not null && !commitStarted)
                await TryRollbackAsync(transaction, destinationTable).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            if (transaction is not null && !commitStarted)
                await TryRollbackAsync(transaction, destinationTable).ConfigureAwait(false);
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static IReadOnlyList<BulkColumn<T>> GetStageColumns<T>(BulkShape<T> shape, bool stageKeysOnly)
        where T : notnull
        => stageKeysOnly ? shape.KeyColumns : shape.Columns;

    private static string CreateStageTableName()
        => "#LibDbBulk_" + Guid.NewGuid().ToString("N");

    private static void ValidateStagedOptions(BulkWriteOptions options)
    {
        if (!options.UseTransaction)
            throw new InvalidOperationException("Staged bulk mutations require a local transaction.");

        if (options.FireTriggers)
            throw new InvalidOperationException("FireTriggers is not supported for staged bulk mutations.");

        if (options.KeepIdentity)
            throw new InvalidOperationException("KeepIdentity is not supported for staged bulk mutations.");

        if (!options.CheckConstraints)
            throw new InvalidOperationException("CheckConstraints cannot be disabled for staged bulk mutations.");
    }

    private static async Task BulkCopyToStageAsync<T>(
        SqlConnection connection,
        SqlTransaction transaction,
        string stageTableName,
        BulkShapeDataReader<T> reader,
        IReadOnlyList<BulkColumn<T>> stageColumns,
        BulkWriteOptions options,
        CancellationToken ct)
        where T : notnull
    {
        using SqlBulkCopy bulkCopy = new(connection, CreateSqlBulkCopyOptions(options), transaction)
        {
            DestinationTableName = stageTableName,
            BatchSize = options.BatchSize,
            BulkCopyTimeout = options.TimeoutSeconds,
            EnableStreaming = options.EnableStreaming
        };

        foreach (BulkColumn<T> column in stageColumns)
            bulkCopy.ColumnMappings.Add(column.DestinationName, column.DestinationName);

        await bulkCopy.WriteToServerAsync(reader, ct).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteNonQueryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        int timeoutSeconds,
        CancellationToken ct)
    {
        await using SqlCommand command = new(sql, connection, transaction)
        {
            CommandTimeout = timeoutSeconds
        };

        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task TryDropStageTableAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string stageTableName,
        int timeoutSeconds)
    {
        try
        {
            await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    $"DROP TABLE {stageTableName};",
                    timeoutSeconds,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    private static async Task TryRollbackAsync(SqlTransaction transaction, string destinationTable)
    {
        BulkWriteHookContext context = new(destinationTable);

        try
        {
            RollbackAttempted?.Invoke(context);
        }
        catch (Exception ex)
        {
            _ = ex;
        }

        try
        {
            Func<SqlTransaction, BulkWriteHookContext, CancellationToken, ValueTask>? rollback =
                RollbackAsyncForTesting;
            if (rollback is not null)
            {
                await rollback(transaction, context, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    private static DbError CreateSqlFailure(SqlException exception, string? objectName, string failureMessage)
    {
        DbError mapped = DbErrorMapper.FromSqlException(exception, objectName);
        return new DbError
        {
            Kind = mapped.Kind,
            SqlErrorCode = mapped.SqlErrorCode,
            Severity = mapped.Severity,
            IsTransient = mapped.IsTransient,
            Message = failureMessage,
            Hint = mapped.Hint,
            ObjectName = objectName,
            InnerException = null
        };
    }

    private static DbError CreateGeneralFailure(string? objectName, string failureMessage)
        => new()
        {
            Kind = DbErrorKind.Unknown,
            Message = failureMessage,
            ObjectName = objectName,
            InnerException = null
        };
}

internal readonly record struct BulkWriteHookContext(string DestinationTable);
