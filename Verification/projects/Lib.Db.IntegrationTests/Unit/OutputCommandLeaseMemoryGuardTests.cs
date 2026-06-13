// ============================================================================
// 파일: Unit/OutputCommandLeaseMemoryGuardTests.cs
// 설명: Reader command lease의 스트리밍/OUTPUT completion 수명 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Data.Common;
using Lib.Db.Execution.Output;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class OutputCommandLeaseMemoryGuardTests
{
    [Fact]
    public async Task ReadLoop_ShouldNotMaterializeAllRowsForEarlyDispose()
    {
        var reader = new CountingDbDataReader(totalRows: 1_000_000);
        int outputCompletions = 0;
        await using var lease = DbCommandLease.ForTest(reader, () => outputCompletions++);

        bool hasRow = await lease.ReadAsync(TestContext.Current.CancellationToken);
        int value = lease.Map(reader => reader.GetInt32(0));
        await lease.DisposeAsync();

        hasRow.Should().BeTrue();
        value.Should().Be(1);
        reader.ReadCount.Should().Be(1);
        reader.Disposed.Should().BeTrue();
        outputCompletions.Should().Be(1);
        lease.State.Should().Be(DbCommandLeaseState.OutputMapped);
    }

    [Fact]
    public async Task ReadLoop_ShouldNotCompleteOutputsAfterReadFailure()
    {
        var reader = new CountingDbDataReader(totalRows: 1_000_000, failOnReadCall: 2);
        int outputCompletions = 0;
        await using var lease = DbCommandLease.ForTest(reader, () => outputCompletions++);

        Func<Task> act = async () =>
        {
            try
            {
                while (await lease.ReadAsync(TestContext.Current.CancellationToken))
                {
                    _ = lease.Map(reader => reader.GetInt32(0));
                }
            }
            finally
            {
                await lease.DisposeAsync();
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*read failed*");
        reader.ReadCount.Should().Be(2);
        reader.Disposed.Should().BeTrue();
        outputCompletions.Should().Be(0);
        lease.State.Should().Be(DbCommandLeaseState.ReadFailed);
    }

    [Fact]
    public async Task DisposeAsync_ShouldCompleteOutputsExactlyOnce()
    {
        var reader = new CountingDbDataReader(totalRows: 0);
        int outputCompletions = 0;
        await using var lease = DbCommandLease.ForTest(reader, () => outputCompletions++);

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        outputCompletions.Should().Be(1);
        lease.State.Should().Be(DbCommandLeaseState.OutputMapped);
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotCompleteOutputsWhenReaderDisposeFails()
    {
        var reader = new CountingDbDataReader(totalRows: 0, throwOnDispose: true);
        int outputCompletions = 0;
        await using var lease = DbCommandLease.ForTest(reader, () => outputCompletions++);

        Func<Task> act = async () => await lease.DisposeAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dispose failed*");
        outputCompletions.Should().Be(0);
        lease.State.Should().Be(DbCommandLeaseState.DisposeFailed);
    }

    [Fact]
    public async Task ReadLoop_ShouldNotCompleteOutputsAfterCancellation()
    {
        var reader = new CountingDbDataReader(totalRows: 1_000_000, cancelOnReadCall: 2);
        int outputCompletions = 0;
        await using var lease = DbCommandLease.ForTest(reader, () => outputCompletions++);

        Func<Task> act = async () =>
        {
            try
            {
                while (await lease.ReadAsync(TestContext.Current.CancellationToken))
                {
                    _ = lease.Map(reader => reader.GetInt32(0));
                }
            }
            finally
            {
                await lease.DisposeAsync();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        reader.ReadCount.Should().Be(2);
        reader.Disposed.Should().BeTrue();
        outputCompletions.Should().Be(0);
        lease.State.Should().Be(DbCommandLeaseState.Canceled);
    }

    [Fact]
    public async Task DisposeAsync_ShouldTreatCommandDisposeFailureAsCleanupOnly()
    {
        var reader = new CountingDbDataReader(totalRows: 0);
        bool commandDisposeAttempted = false;
        int outputCompletions = 0;
        await using var lease = DbCommandLease.ForTest(
            reader,
            () =>
            {
                commandDisposeAttempted = true;
                throw new InvalidOperationException("command dispose failed");
            },
            () => outputCompletions++);

        Func<Task> act = async () => await lease.DisposeAsync();

        await act.Should().NotThrowAsync();
        outputCompletions.Should().Be(1);
        reader.Disposed.Should().BeTrue();
        commandDisposeAttempted.Should().BeTrue();
        lease.State.Should().Be(DbCommandLeaseState.OutputMapped);
    }

    private sealed class CountingDbDataReader(
        int totalRows,
        int failOnReadCall = 0,
        int cancelOnReadCall = 0,
        bool throwOnDispose = false) : DbDataReader
    {
        private int _currentRow;

        public int ReadCount { get; private set; }

        public bool Disposed { get; private set; }

        public override int FieldCount => 1;

        public override bool HasRows => totalRows > 0;

        public override bool IsClosed => Disposed;

        public override int RecordsAffected => -1;

        public override int Depth => 0;

        public override object this[int ordinal] => GetValue(ordinal);

        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool Read()
        {
            if (Disposed)
                return false;

            ReadCount++;
            if (failOnReadCall > 0 && ReadCount == failOnReadCall)
                throw new InvalidOperationException("read failed");
            if (cancelOnReadCall > 0 && ReadCount == cancelOnReadCall)
                throw new OperationCanceledException();

            if (ReadCount > totalRows)
                return false;

            _currentRow = ReadCount;
            return true;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            => Task.FromResult(Read());

        public override bool NextResult() => false;

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public override object GetValue(int ordinal) => _currentRow;

        public override int GetValues(object[] values)
        {
            values[0] = GetValue(0);
            return 1;
        }

        public override bool IsDBNull(int ordinal) => false;

        public override string GetName(int ordinal) => "Value";

        public override int GetOrdinal(string name)
            => string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase) ? 0 : -1;

        public override string GetDataTypeName(int ordinal) => nameof(Int32);

        public override Type GetFieldType(int ordinal) => typeof(int);

        public override IEnumerator GetEnumerator() => Enumerable.Range(1, totalRows).GetEnumerator();

        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            if (throwOnDispose)
                throw new InvalidOperationException("dispose failed");

            return ValueTask.CompletedTask;
        }
    }

}
