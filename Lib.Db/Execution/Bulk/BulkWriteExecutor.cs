using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Execution.Bulk;

internal sealed class BulkWriteExecutor(IDbConnectionFactory connectionFactory)
{
    private const string BulkInsertFailureMessage = "Bulk insert failed.";

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
            return DbResult<long>.Fail(CreateSqlFailure(ex, objectName));
        }
        catch (Exception ex)
        {
            _ = ex;
            return DbResult<long>.Fail(CreateGeneralFailure(objectName));
        }
    }

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

    private static DbError CreateSqlFailure(SqlException exception, string? objectName)
    {
        DbError mapped = DbErrorMapper.FromSqlException(exception, objectName);
        return new DbError
        {
            Kind = mapped.Kind,
            SqlErrorCode = mapped.SqlErrorCode,
            Severity = mapped.Severity,
            IsTransient = mapped.IsTransient,
            Message = BulkInsertFailureMessage,
            Hint = mapped.Hint,
            ObjectName = objectName,
            InnerException = null
        };
    }

    private static DbError CreateGeneralFailure(string? objectName)
        => new()
        {
            Kind = DbErrorKind.Unknown,
            Message = BulkInsertFailureMessage,
            ObjectName = objectName,
            InnerException = null
        };
}

internal readonly record struct BulkWriteHookContext(string DestinationTable);
