using FluentAssertions;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Execution;
using Lib.Db.Extensions;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class MultipleResultExtensionsTests
{
    [Fact]
    public async Task ReadMultipleAsync_Arity2_ShouldReadAndDisposeReader()
    {
        var reader = new FakeMultipleResultReader(
            [
                new List<UserRow> { new(1) },
                new List<OrderRow> { new(7) }
            ]);

        DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
            .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
            .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.First.Should().ContainSingle(row => row.Id == 1);
        result.Value.Second.Should().ContainSingle(row => row.Id == 7);
        reader.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadMultipleAsync_Arity3_ShouldReadInOrderAndDisposeReader()
    {
        var reader = new FakeMultipleResultReader(
            [
                new List<UserRow> { new(1) },
                new List<OrderRow> { new(7) },
                new List<SummaryRow> { new(2) }
            ]);

        DbResult<DbMultiple<UserRow, OrderRow, SummaryRow>> result = await Task
            .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
            .ReadMultipleAsync<UserRow, OrderRow, SummaryRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.First.Should().ContainSingle(row => row.Id == 1);
        result.Value.Second.Should().ContainSingle(row => row.Id == 7);
        result.Value.Third.Should().ContainSingle(row => row.Count == 2);
        reader.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadMultipleAsync_Arity4_ShouldReadInOrderAndDisposeReader()
    {
        var reader = new FakeMultipleResultReader(
            [
                new List<UserRow> { new(1) },
                new List<OrderRow> { new(7) },
                new List<SummaryRow> { new(2) },
                new List<AuditRow> { new(9) }
            ]);

        DbResult<DbMultiple<UserRow, OrderRow, SummaryRow, AuditRow>> result = await Task
            .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
            .ReadMultipleAsync<UserRow, OrderRow, SummaryRow, AuditRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.First.Should().ContainSingle(row => row.Id == 1);
        result.Value.Second.Should().ContainSingle(row => row.Id == 7);
        result.Value.Third.Should().ContainSingle(row => row.Count == 2);
        result.Value.Fourth.Should().ContainSingle(row => row.Id == 9);
        reader.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadMultipleAsync_ShouldPropagateFailedDbResult()
    {
        DbError error = new()
        {
            Kind = DbErrorKind.Unknown,
            SqlErrorCode = 0,
            Severity = 0,
            IsTransient = false,
            Message = "QueryMultiple failed"
        };

        DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
            .FromResult(DbResult<IMultipleResultReader>.Fail(error))
            .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task ReadMultipleAsync_ShouldReturnFailureWhenExpectedResultSetIsMissing()
    {
        var reader = new FakeMultipleResultReader(
            [
                new List<UserRow> { new(1) }
            ]);

        DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
            .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
            .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
        reader.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadMultipleAsync_ShouldDisposeReaderWhenReadFails()
    {
        var reader = FakeMultipleResultReader.ThrowOnSecondRead(
            new InvalidOperationException("mapper failed for dbo.SecretTenant"));

        DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
            .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
            .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
        result.Error.Value.Message.Should().NotContain("mapper failed");
        result.Error.Value.Message.Should().NotContain("dbo.SecretTenant");
        result.Error.Value.InnerException.Should().BeNull();
        reader.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadMultipleAsync_Arity3_ShouldReturnFailureWhenExpectedResultSetIsMissing()
    {
        var reader = new FakeMultipleResultReader(
            [
                new List<UserRow> { new(1) },
                new List<OrderRow> { new(7) }
            ]);

        DbResult<DbMultiple<UserRow, OrderRow, SummaryRow>> result = await Task
            .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
            .ReadMultipleAsync<UserRow, OrderRow, SummaryRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
        result.Error.Value.Message.Should().NotContain("Missing");
        result.Error.Value.Message.Should().NotContain("missing");
        result.Error.Value.InnerException.Should().BeNull();
        reader.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadMultipleAsync_Arity4_ShouldDisposeReaderWhenReadFailsAndRedactError()
    {
        var reader = FakeMultipleResultReader.ThrowOnFourthRead(
            new InvalidOperationException("mapper failed for user tenant-42 in dbo.SecretAudit"));

        DbResult<DbMultiple<UserRow, OrderRow, SummaryRow, AuditRow>> result = await Task
            .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
            .ReadMultipleAsync<UserRow, OrderRow, SummaryRow, AuditRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
        result.Error.Value.Message.Should().NotContain("mapper failed");
        result.Error.Value.Message.Should().NotContain("tenant-42");
        result.Error.Value.Message.Should().NotContain("dbo.SecretAudit");
        result.Error.Value.InnerException.Should().BeNull();
        reader.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadMultipleAsync_ShouldPropagateCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var reader = FakeMultipleResultReader.ThrowWhenCanceled();

        Func<Task> act = async () => await Task
            .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
            .ReadMultipleAsync<UserRow, OrderRow>(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        reader.DisposeCount.Should().Be(1);
    }

    private sealed record UserRow(int Id);

    private sealed record OrderRow(int Id);

    private sealed record SummaryRow(int Count);

    private sealed record AuditRow(int Id);

    private sealed class FakeMultipleResultReader : IMultipleResultReader
    {
        private readonly IReadOnlyList<object> resultSets;
        private readonly Dictionary<int, Exception> failuresByReadNumber;
        private readonly bool throwWhenCanceled;
        private int nextResultSetIndex;
        private int readCount;

        public FakeMultipleResultReader(IReadOnlyList<object> resultSets)
            : this(resultSets, new Dictionary<int, Exception>(), false)
        {
        }

        private FakeMultipleResultReader(
            IReadOnlyList<object> resultSets,
            Dictionary<int, Exception> failuresByReadNumber,
            bool throwWhenCanceled)
        {
            this.resultSets = resultSets;
            this.failuresByReadNumber = failuresByReadNumber;
            this.throwWhenCanceled = throwWhenCanceled;
        }

        public int DisposeCount { get; private set; }

        public static FakeMultipleResultReader ThrowOnSecondRead(Exception exception)
            => new(
                [
                    new List<UserRow> { new(1) },
                    new List<OrderRow> { new(7) }
                ],
                new Dictionary<int, Exception> { [2] = exception },
                false);

        public static FakeMultipleResultReader ThrowOnFourthRead(Exception exception)
            => new(
                [
                    new List<UserRow> { new(1) },
                    new List<OrderRow> { new(7) },
                    new List<SummaryRow> { new(2) },
                    new List<AuditRow> { new(9) }
                ],
                new Dictionary<int, Exception> { [4] = exception },
                false);

        public static FakeMultipleResultReader ThrowWhenCanceled()
            => new(
                [
                    new List<UserRow> { new(1) },
                    new List<OrderRow> { new(7) }
                ],
                new Dictionary<int, Exception>(),
                true);

        public Task<List<T>> ReadAsync<T>(CancellationToken ct = default)
        {
            readCount++;

            if (throwWhenCanceled)
                ct.ThrowIfCancellationRequested();

            if (failuresByReadNumber.TryGetValue(readCount, out Exception? exception))
                throw exception;

            if (nextResultSetIndex >= resultSets.Count)
                throw new InvalidOperationException("Missing result set for dbo.SecretProcedure.");

            object resultSet = resultSets[nextResultSetIndex++];
            if (resultSet is not List<T> typedResultSet)
                throw new InvalidOperationException($"Unexpected result type {typeof(T).FullName}.");

            return Task.FromResult(typedResultSet);
        }

        public Task<T?> ReadSingleAsync<T>(CancellationToken ct = default)
            => throw new NotSupportedException("This fake only supports ReadAsync.");

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
