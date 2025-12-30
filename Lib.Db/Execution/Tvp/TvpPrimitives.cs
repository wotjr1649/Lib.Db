// File: Lib.Db.Execution/Tvp/TvpPrimitives.cs
// Role: TVP(Table-Valued Parameter)를 위한 컬럼 기반 인메모리 저장소 및 최적화된 리더
// Target: .NET 10 / C# 14
// Note: Nullable<T> 언박싱 문제 해결 및 Half 타입 지원 적용됨

#nullable enable

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Lib.Db.Execution.Tvp;

using Lib.Db.Contracts.Models; // ITvpColumn<T>

#region 컬럼 버퍼

/// <summary>
/// TVP(Table-Valued Parameter)의 단일 컬럼 데이터를 저장하는 추상 기저 클래스입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// - 제네릭이 아닌 컨텍스트(예: <see cref="ColumnarTvpReader"/>)에서 컬럼에 접근하기 위한 공통 인터페이스를 제공합니다.<br/>
/// - <see cref="IDisposable"/>을 구현하여 네이티브 메모리나 풀링된 리소스를 해제합니다.
/// </para>
/// </summary>
internal abstract class ColumnBuffer : IDisposable
{
    /// <summary>
    /// 지정된 인덱스의 값을 객체(object) 형태로 반환합니다. (박싱 발생 가능)
    /// </summary>
    public abstract object GetValue(int index);

    /// <summary>
    /// 지정된 인덱스의 값이 DBNull(또는 null)인지 확인합니다.
    /// </summary>
    public abstract bool IsNull(int index);

    /// <summary>
    /// 리소스를 해제하고 사용 중인 버퍼를 풀에 반환합니다.
    /// </summary>
    public abstract void Dispose();
}

/// <summary>
/// 특정 타입(T)의 컬럼 데이터를 <see cref="ArrayPool{T}"/>을 사용하여 효율적으로 저장하는 버퍼입니다.
/// <para>
/// <b>[핵심 기능 및 최적화]</b><br/>
/// - <b>Zero-Boxing:</b> 값 타입(Value Type)을 박싱 없이 그대로 배열에 저장하여 힙 할당을 최소화합니다.<br/>
/// - <b>ArrayPool 활용:</b> <see cref="ArrayPool{T}.Shared"/>를 사용하여 대용량 배열 할당/해제 오버헤드를 줄입니다.<br/>
/// - <b>동적 확장:</b> 데이터 추가 시 용량이 부족하면 자동으로 2배씩 확장됩니다.
/// </para>
/// </summary>
/// <typeparam name="T">컬럼 데이터 타입</typeparam>
internal sealed class TypedColumnBuffer<T> : ColumnBuffer, ITvpColumn<T>
{
    private T[]? _buffer;
    private int _count;

    /// <summary>
    /// 지정된 초기 용량으로 버퍼를 생성합니다.
    /// </summary>
    public TypedColumnBuffer(int initialCapacity)
    {
        _buffer = ArrayPool<T>.Shared.Rent(initialCapacity);
        _count = 0;
    }

    /// <summary>
    /// 값을 버퍼에 추가합니다. 용량이 부족하면 자동으로 확장합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value)
    {
        var buffer = _buffer;
        // [Safety] 이미 Dispose된 버퍼 접근 방지
        if (buffer == null) ThrowObjectDisposed();

        // [Performance] Unsigned cast를 통한 범위 검사 최적화 (JIT 힌트)
        if ((uint)_count >= (uint)buffer!.Length)
        {
            Resize();
            buffer = _buffer; // Resize 후 참조 갱신
        }
        buffer![_count++] = value;
    }

    /// <summary>
    /// 지정된 인덱스의 값을 타입 T 그대로 반환합니다. (박싱 없음)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetTypedValue(int index)
    {
        Debug.Assert(_buffer != null);
        Debug.Assert((uint)index < (uint)_count);
        return _buffer![index];
    }

    /// <inheritdoc/>
    public override object GetValue(int index)
    {
        var value = _buffer![index]!;
        
        // .NET 10 타입을 SQL Client가 이해할 수 있는 타입으로 변환
        // SqlClient의 ValueUtilsSmi는 GetValue()를 호출하여 객체를 받고,
        // TVP 전송 시 다음과 같은 변환을 기대합니다:
        // - DATE (SqlDbType.Date) → DateTime
        // - TIME (SqlDbType.Time) → TimeSpan
        // - REAL (SqlDbType.Real) → float
        return value switch
        {
            DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),  // DATE → DateTime (00:00:00)
            TimeOnly timeOnly => timeOnly.ToTimeSpan(),                    // TIME → TimeSpan
            Half half => (float)half,                                      // REAL → float
            _ => value  // 다른 타입은 그대로 반환 (int, string, DateTime, etc.)
        };
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool IsNull(int index) => _buffer![index] is null;

    /// <summary>
    /// 버퍼 용량을 2배로 확장합니다. 기존 데이터는 복사되고, 이전 버퍼는 풀에 반환됩니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Resize()
    {
        if (_buffer == null) ThrowObjectDisposed();

        var oldBuffer = _buffer;
        var newSize = oldBuffer!.Length * 2;
        var newBuffer = ArrayPool<T>.Shared.Rent(newSize);

        oldBuffer.AsSpan(0, _count).CopyTo(newBuffer);

        ArrayPool<T>.Shared.Return(oldBuffer);
        _buffer = newBuffer;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        var buffer = _buffer;
        if (buffer != null)
        {
            // [Safety] UAF(Use-After-Free) 방지를 위해 참조를 먼저 끊습니다.
            _buffer = null;
            _count = 0;
            ArrayPool<T>.Shared.Return(buffer);
        }
    }

    private void ThrowObjectDisposed() => throw new ObjectDisposedException(nameof(TypedColumnBuffer<T>));
}

#endregion

#region 리더 (Reader Optimized)

/// <summary>
/// 컬럼 기반으로 저장된 TVP 데이터를 행(Row) 단위로 읽을 수 있도록 변환하는 <see cref="DbDataReader"/> 구현체입니다.
/// <para>
/// <b>[사용 시나리오]</b><br/>
/// - <see cref="System.Data.SqlClient.SqlParameter"/>의 Value로 TVP를 전달할 때 사용됩니다.<br/>
/// - <see cref="System.Data.SqlClient.SqlBulkCopy.WriteToServerAsync"/>의 소스 데이터로 사용됩니다.<br/>
/// </para>
/// <para>
/// <b>[성능 특징]</b><br/>
/// - <b>고성능 접근:</b> <see cref="TypedColumnBuffer{T}"/>의 메서드를 직접 호출하여 박싱을 방지하고 호출 비용을 최소화합니다.<br/>
/// - <b>Ordinal 매핑 최적화:</b> <see cref="IReadOnlyDictionary{TKey, TValue}"/> (FrozenDictionary 권장)를 사용하여 컬럼 이름 조회를 O(1)에 가깝게 처리합니다.<br/>
/// - <b>비동기 제거:</b> 메모리상의 데이터를 읽으므로 I/O 대기가 없어 대부분의 메서드가 동기적으로 동작합니다.
/// </para>
/// </summary>
public sealed class ColumnarTvpReader : DbDataReader, IAsyncDisposable
{
    private readonly ColumnBuffer[] _columns;
    private readonly int _rowCount;
    private readonly DataTable? _schemaTable;
    // [Performance] FrozenDictionary 인터페이스 호환으로 조회 성능 극대화
    private readonly IReadOnlyDictionary<string, int> _ordinalMap;

    private int _currentIndex = -1;
    private bool _isClosed;

    /// <summary>
    /// 내부 생성자입니다. 외부에서는 빌더를 통해 생성해야 합니다.
    /// </summary>
    internal ColumnarTvpReader(
        ColumnBuffer[] columns,
        int rowCount,
        IReadOnlyDictionary<string, int> ordinalMap,
        DataTable? schemaTable = null)
    {
        _columns = columns;
        _rowCount = rowCount;
        _ordinalMap = ordinalMap;
        _schemaTable = schemaTable;
    }

    /// <summary>
    /// 다음 행으로 이동합니다.
    /// </summary>
    /// <returns>더 읽을 행이 있으면 true, 없으면 false</returns>
    public override bool Read() => !_isClosed && ++_currentIndex < _rowCount;

    /// <summary>
    /// 컬럼 이름으로 인덱스(Ordinal)를 조회합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetOrdinal(string name)
    {
        // [Performance] Dictionary 조회를 통해 루프 없이 즉시 반환
        if (_ordinalMap.TryGetValue(name, out int ordinal)) return ordinal;
        throw new IndexOutOfRangeException($"Column '{name}' not found in TVP data.");
    }

    // 인덱서 구현
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    // =========================================================================================
    // Typed Getters (형식 안전 접근자)
    // - [수정] Nullable<T> 버퍼 지원을 위해 패턴 매칭(is) 도입 (InvalidCastException 방지)
    // - [수정] Half 타입 지원을 위해 GetFloat 확장
    // =========================================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetInt32(int i)
    {
        if (_columns[i] is TypedColumnBuffer<int> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<int?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Int32.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string GetString(int i)
    {
        // 참조 타입(string)은 TypedColumnBuffer<string> 하나로 처리됨 (런타임 타입 동일)
        return ((TypedColumnBuffer<string>)_columns[i]).GetTypedValue(_currentIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override long GetInt64(int i)
    {
        if (_columns[i] is TypedColumnBuffer<long> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<long?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Int64.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool GetBoolean(int i)
    {
        if (_columns[i] is TypedColumnBuffer<bool> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<bool?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Boolean.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override decimal GetDecimal(int i)
    {
        if (_columns[i] is TypedColumnBuffer<decimal> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<decimal?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Decimal.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double GetDouble(int i)
    {
        if (_columns[i] is TypedColumnBuffer<double> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<double?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Double.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override DateTime GetDateTime(int i)
    {
        // 1. DateTime 버퍼 (기존 동작)
        if (_columns[i] is TypedColumnBuffer<DateTime> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<DateTime?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        
        // 2. .NET 10 DateOnly 버퍼 지원 (DATE → DateTime 변환)
        if (_columns[i] is TypedColumnBuffer<DateOnly> d1) 
            return d1.GetTypedValue(_currentIndex).ToDateTime(TimeOnly.MinValue);
        if (_columns[i] is TypedColumnBuffer<DateOnly?> d2)
        {
            var dateOnly = d2.GetTypedValue(_currentIndex);
            return dateOnly!.Value.ToDateTime(TimeOnly.MinValue);
        }
        
        // 3. .NET 10 TimeOnly 버퍼 지원 (TIME → DateTime 변환: 오늘 날짜 + 시간)
        if (_columns[i] is TypedColumnBuffer<TimeOnly> t1)
            return DateTime.Today.Add(t1.GetTypedValue(_currentIndex).ToTimeSpan());
        if (_columns[i] is TypedColumnBuffer<TimeOnly?> t2)
        {
            var timeOnly = t2.GetTypedValue(_currentIndex);
            return DateTime.Today.Add(timeOnly!.Value.ToTimeSpan());
        }
        
        throw new InvalidCastException($"Column {i} is not DateTime, DateOnly, or TimeOnly.");
    }

    /// <summary>
    /// 해당 컬럼의 값을 객체(object)로 반환합니다.
    /// </summary>
    public override object GetValue(int i) => _columns[i].GetValue(_currentIndex);

    /// <summary>
    /// 해당 컬럼의 값이 DBNull인지 확인합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool IsDBNull(int i) => _columns[i].IsNull(_currentIndex);

    // =========================================================================================
    // 메타데이터 및 상태 (Metadata & State)
    // =========================================================================================
    public override int FieldCount => _columns.Length;
    public override bool HasRows => _rowCount > 0;
    public override bool IsClosed => _isClosed;
    public override int Depth => 0;
    public override int RecordsAffected => -1;
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <summary>
    /// SqlBulkCopy가 컬럼 매핑을 위해 참조하는 스키마 테이블을 반환합니다.
    /// </summary>
    public override DataTable? GetSchemaTable() => _schemaTable;

    public override bool NextResult() => false;

    // [Strict Compliance] 기존 동작 100% 유지 (예외 발생)
    public override string GetName(int ordinal)
    {
        if (_schemaTable is null) throw new NotSupportedException("SchemaTable is not available.");
        return (string)_schemaTable.Rows[ordinal][SchemaTableColumn.ColumnName];
    }
    public override Type GetFieldType(int ordinal)
    {
        if (_schemaTable is null) throw new NotSupportedException("SchemaTable is not available.");
        return (Type)_schemaTable.Rows[ordinal][SchemaTableColumn.DataType];
    }
    public override string GetDataTypeName(int ordinal)
    {
        if (_schemaTable is null) throw new NotSupportedException("SchemaTable is not available.");
        return GetFieldType(ordinal).Name;
    }

    // =========================================================================================
    // 추가 타입 지원 (Extended Types)
    // - [수정] 모든 값 타입에 Nullable Safe 패턴 적용
    // - [수정] Half 타입 지원 추가
    // =========================================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte GetByte(int i)
    {
        if (_columns[i] is TypedColumnBuffer<byte> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<byte?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Byte.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override char GetChar(int i)
    {
        if (_columns[i] is TypedColumnBuffer<char> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<char?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Char.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override float GetFloat(int i)
    {
        // 1. float (System.Single) 버퍼
        if (_columns[i] is TypedColumnBuffer<float> f1) return f1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<float?> f2) return f2.GetTypedValue(_currentIndex)!.Value;

        // 2. .NET 10 Half 타입 버퍼 지원 (float으로 변환하여 반환)
        if (_columns[i] is TypedColumnBuffer<Half> h1) return (float)h1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<Half?> h2) return (float)h2.GetTypedValue(_currentIndex)!.Value;

        throw new InvalidCastException($"Column {i} is not Float/Half.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Guid GetGuid(int i)
    {
        if (_columns[i] is TypedColumnBuffer<Guid> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<Guid?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Guid.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override short GetInt16(int i)
    {
        if (_columns[i] is TypedColumnBuffer<short> b1) return b1.GetTypedValue(_currentIndex);
        if (_columns[i] is TypedColumnBuffer<short?> b2) return b2.GetTypedValue(_currentIndex)!.Value;
        throw new InvalidCastException($"Column {i} is not Int16.");
    }

    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, _columns.Length);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    /// <summary>
    /// 바이너리 데이터(byte[])를 스트림처럼 읽습니다.
    /// </summary>
    public override long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
    {
        var value = ((TypedColumnBuffer<byte[]>)_columns[i]).GetTypedValue(_currentIndex);
        if (value is null) return 0;

        if (buffer is null) return value.Length;
        if (fieldOffset < 0 || fieldOffset >= value.Length) return 0;
        if (length < 0) throw new IndexOutOfRangeException("length cannot be negative.");

        long available = value.Length - fieldOffset;
        int count = Math.Min(length, (int)available);

        Buffer.BlockCopy(value, (int)fieldOffset, buffer, bufferoffset, count);
        return count;
    }

    /// <summary>
    /// 문자열 데이터(char[])를 스트림처럼 읽습니다.
    /// </summary>
    public override long GetChars(int i, long fieldOffset, char[]? buffer, int bufferoffset, int length)
    {
        var value = GetString(i); // String buffer uses GetString
        if (value is null) return 0;

        if (buffer is null) return value.Length;
        if (fieldOffset < 0 || fieldOffset >= value.Length) return 0;
        if (length < 0) throw new IndexOutOfRangeException("length cannot be negative.");

        long available = value.Length - fieldOffset;
        int count = Math.Min(length, (int)available);

        value.CopyTo((int)fieldOffset, buffer, bufferoffset, count);
        return count;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (_isClosed) return;
        _isClosed = true;
        if (disposing)
        {
            // 리더가 닫힐 때 내부의 모든 컬럼 버퍼도 함께 정리합니다.
            foreach (var col in _columns) col.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        Dispose(true);
        return ValueTask.CompletedTask;
    }
}
#endregion