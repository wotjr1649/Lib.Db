#nullable enable

using System.Data.Common;
using System.Runtime.ExceptionServices;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Execution.Output;

internal enum DbCommandLeaseState
{
    Active,
    FullyConsumed,
    EarlyDisposedCleanly,
    ReadFailed,
    Canceled,
    DisposeFailed,
    OutputMapped,
    OutputMappingFailed
}

/// <summary>
/// Reader 기반 실행에서 command와 reader 수명을 함께 잡고,
/// reader가 닫힌 뒤에만 OUTPUT 값을 caller target으로 복사합니다.
/// </summary>
internal sealed class DbCommandLease : IAsyncDisposable
{
    private readonly Func<ValueTask> _completeOutputs;
    private Func<ValueTask>? _disposeCommand;
    private DbDataReader? _reader;
    private int _completionAttempted;
    private int _disposeStarted;

    public DbCommandLease(
        DbDataReader reader,
        SqlCommand? command,
        Func<ValueTask> completeOutputs)
        : this(
            reader,
            command is null ? null : () => command.DisposeAsync(),
            completeOutputs)
    {
    }

    private DbCommandLease(
        DbDataReader reader,
        Func<ValueTask>? disposeCommand,
        Func<ValueTask> completeOutputs)
    {
        _reader = reader;
        _disposeCommand = disposeCommand;
        _completeOutputs = completeOutputs;
    }

    public DbCommandLeaseState State { get; private set; } = DbCommandLeaseState.Active;

    public DbDataReader Reader => _reader
        ?? throw new ObjectDisposedException(nameof(DbCommandLease));

    public static DbCommandLease ForTest(DbDataReader reader, Action completeOutputs)
        => new(reader, command: null, () =>
        {
            completeOutputs();
            return ValueTask.CompletedTask;
        });

    public static DbCommandLease ForTest(
        DbDataReader reader,
        Func<ValueTask> disposeCommand,
        Action completeOutputs)
        => new(reader, disposeCommand, () =>
        {
            completeOutputs();
            return ValueTask.CompletedTask;
        });

    public async ValueTask<bool> ReadAsync(CancellationToken ct)
    {
        try
        {
            return await Reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            MarkCanceled();
            throw;
        }
        catch
        {
            MarkReadFailed();
            throw;
        }
    }

    public async ValueTask<bool> NextResultAsync(CancellationToken ct)
    {
        try
        {
            return await Reader.NextResultAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            MarkCanceled();
            throw;
        }
        catch
        {
            MarkReadFailed();
            throw;
        }
    }

    public TResult Map<TResult>(Func<DbDataReader, TResult> map)
    {
        try
        {
            return map(Reader);
        }
        catch
        {
            MarkReadFailed();
            throw;
        }
    }

    public void MarkFullyConsumed()
    {
        if (State == DbCommandLeaseState.Active)
            State = DbCommandLeaseState.FullyConsumed;
    }

    public void MarkReadFailed() => MarkFailure(DbCommandLeaseState.ReadFailed);

    public void MarkCanceled() => MarkFailure(DbCommandLeaseState.Canceled);

    public async ValueTask CompleteAsync()
    {
        if (Interlocked.Exchange(ref _completionAttempted, 1) != 0)
            return;

        if (!IsOutputEligible(State))
            return;

        try
        {
            await _completeOutputs().ConfigureAwait(false);
            State = DbCommandLeaseState.OutputMapped;
        }
        catch
        {
            State = DbCommandLeaseState.OutputMappingFailed;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        if (State == DbCommandLeaseState.Active)
            State = DbCommandLeaseState.EarlyDisposedCleanly;

        Exception? pending = null;

        try
        {
            if (_reader is not null)
                await _reader.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            State = DbCommandLeaseState.DisposeFailed;
            pending = ex;
        }
        finally
        {
            _reader = null;
        }

        if (pending is null)
        {
            try
            {
                await CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                pending = ex;
            }
        }

        try
        {
            if (_disposeCommand is not null)
                await _disposeCommand().ConfigureAwait(false);
        }
        catch
        {
            // Command disposal is cleanup only; reader/output completion determines
            // the public lifecycle result for this lease.
        }

        _disposeCommand = null;

        if (pending is not null)
            ExceptionDispatchInfo.Capture(pending).Throw();
    }

    private void MarkFailure(DbCommandLeaseState failureState)
    {
        if (State is DbCommandLeaseState.Active
            or DbCommandLeaseState.FullyConsumed
            or DbCommandLeaseState.EarlyDisposedCleanly)
        {
            State = failureState;
        }
    }

    private static bool IsOutputEligible(DbCommandLeaseState state)
        => state is DbCommandLeaseState.FullyConsumed or DbCommandLeaseState.EarlyDisposedCleanly;
}
