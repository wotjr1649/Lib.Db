using System.Collections;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Lib.Db.Execution.Bulk;

internal sealed class BulkShapeDataReader<T> : DbDataReader
    where T : notnull
{
    private readonly IEnumerator<T> _enumerator;
    private readonly IReadOnlyList<BulkColumn<T>> _columns;
    private T _current = default!;
    private T _bufferedRow = default!;
    private bool _hasCurrent;
    private bool _hasBufferedRow;
    private bool _hasRowsKnown;
    private bool _hasRows;
    private bool _closed;

    public BulkShapeDataReader(
        IEnumerable<T> rows,
        BulkShape<T> shape,
        IReadOnlyList<BulkColumn<T>>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(shape);

        _enumerator = rows.GetEnumerator();
        _columns = columns ?? shape.Columns;
    }

    public long RowsRead { get; private set; }

    public override int FieldCount => _columns.Count;

    public override bool HasRows
    {
        get
        {
            if (_closed)
                return false;

            if (!_hasRowsKnown)
                BufferFirstRow();

            return _hasRows;
        }
    }

    public override bool IsClosed => _closed;
    public override int RecordsAffected => -1;
    public override int Depth => 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_closed)
            return false;

        if (_hasBufferedRow)
        {
            _current = _bufferedRow;
            _bufferedRow = default!;
            _hasBufferedRow = false;
            _hasCurrent = true;
            RowsRead++;
            return true;
        }

        if (!_enumerator.MoveNext())
        {
            ClearCurrent();
            _hasRowsKnown = true;
            if (RowsRead == 0)
                _hasRows = false;
            return false;
        }

        _current = _enumerator.Current;
        _hasCurrent = true;
        _hasRowsKnown = true;
        _hasRows = true;
        RowsRead++;
        return true;
    }

    public override string GetName(int ordinal) => _columns[ordinal].DestinationName;

    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            if (string.Equals(_columns[i].DestinationName, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new IndexOutOfRangeException($"Column '{name}' was not found in the bulk shape.");
    }

    public override object GetValue(int ordinal)
    {
        if (!_hasCurrent)
            throw new InvalidOperationException("Read must be called before reading current bulk row values.");

        BulkColumn<T> column = _columns[ordinal];
        object? value = column.GetValue(_current);
        if (value is not null)
            return value;

        if (!column.Nullable)
            throw new InvalidOperationException($"Bulk column '{column.DestinationName}' produced null for a non-nullable column.");

        return DBNull.Value;
    }

    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
            values[i] = GetValue(i);

        return count;
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;
    public override string GetDataTypeName(int ordinal) => _columns[ordinal].SqlDbType.ToString();
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2093",
        Justification = "The reader returns provider-facing primitive Type values only; Lib.Db does not reflect over returned members here.")]
    public override Type GetFieldType(int ordinal) => BulkSqlTypeRenderer.GetFieldType(_columns[ordinal]);
    public override bool NextResult() => false;
    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

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

    public override void Close()
    {
        if (_closed)
            return;

        _closed = true;
        ClearCurrent();
        _bufferedRow = default!;
        _hasBufferedRow = false;
        _enumerator.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();

        base.Dispose(disposing);
    }

    private void BufferFirstRow()
    {
        if (_enumerator.MoveNext())
        {
            _bufferedRow = _enumerator.Current;
            _hasBufferedRow = true;
            _hasRows = true;
        }
        else
        {
            _hasRows = false;
        }

        _hasRowsKnown = true;
    }

    private void ClearCurrent()
    {
        _current = default!;
        _hasCurrent = false;
    }
}
