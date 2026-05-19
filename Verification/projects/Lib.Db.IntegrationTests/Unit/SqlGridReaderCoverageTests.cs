// ============================================================================
// 파일: Unit/SqlGridReaderCoverageTests.cs
// 설명: 다중 ResultSet reader 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Data;
using System.Data.Common;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Executors;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class SqlGridReaderCoverageTests
{
    [Fact]
    public async Task ReadAsyncAndReadSingleAsync_ShouldAdvanceAcrossResultSetsAndDisposeReader()
    {
        var reader = new SequenceDbDataReader(
            [[1], [2]],
            [["second"]],
            []);
        var grid = new SqlGridReader(reader, new ValueMapperFactory());

        List<int> first = await grid.ReadAsync<int>();
        string? second = await grid.ReadSingleAsync<string>();
        int emptySingle = await grid.ReadSingleAsync<int>();
        List<int> afterLast = await grid.ReadAsync<int>();
        string? afterLastSingle = await grid.ReadSingleAsync<string>();

        await grid.DisposeAsync();

        first.Should().Equal(1, 2);
        second.Should().Be("second");
        emptySingle.Should().Be(0);
        afterLast.Should().BeEmpty();
        afterLastSingle.Should().BeNull();
        reader.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyGridReader_ShouldReturnEmptyDefaultsAndCompleteDispose()
    {
        var reader = new EmptyGridReader();

        List<int> rows = await reader.ReadAsync<int>();
        string? single = await reader.ReadSingleAsync<string>();
        await reader.DisposeAsync();

        rows.Should().BeEmpty();
        single.Should().BeNull();
    }

    private sealed class ValueMapperFactory : IMapperFactory
    {
        public ISqlMapper<T> GetMapper<T>() => new ValueMapper<T>();
    }

    private sealed class ValueMapper<T> : ISqlMapper<T>
    {
        public void MapParameters(SqlCommand cmd, T parameters, SpSchema? schema)
        {
        }

        public void MapOutputParameters(SqlCommand cmd, T parameters)
        {
        }

        public T MapResult(DbDataReader reader)
        {
            object value = reader.GetValue(0);
            return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
        }
    }

    private sealed class SequenceDbDataReader(params object?[][][] resultSets) : DbDataReader
    {
        private int _resultIndex;
        private int _rowIndex = -1;

        public bool Disposed { get; private set; }

        public override int FieldCount => 1;

        public override bool HasRows => CurrentSet.Length > 0;

        public override bool IsClosed => Disposed;

        public override int RecordsAffected => -1;

        public override int Depth => 0;

        public override object this[int ordinal] => GetValue(ordinal);

        public override object this[string name] => GetValue(GetOrdinal(name));

        private object?[][] CurrentSet => _resultIndex < resultSets.Length ? resultSets[_resultIndex] : [];

        public override bool Read()
        {
            if (Disposed)
                return false;

            _rowIndex++;
            return _rowIndex < CurrentSet.Length;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            => Task.FromResult(Read());

        public override bool NextResult()
        {
            if (_resultIndex + 1 >= resultSets.Length)
                return false;

            _resultIndex++;
            _rowIndex = -1;
            return true;
        }

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
            => Task.FromResult(NextResult());

        public override object GetValue(int ordinal)
            => CurrentSet[_rowIndex][ordinal] ?? DBNull.Value;

        public override int GetValues(object[] values)
        {
            values[0] = GetValue(0);
            return 1;
        }

        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

        public override string GetName(int ordinal) => "Value";

        public override int GetOrdinal(string name) => string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase) ? 0 : -1;

        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

        public override Type GetFieldType(int ordinal) => GetValue(ordinal).GetType();

        public override IEnumerator GetEnumerator() => CurrentSet.GetEnumerator();

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
