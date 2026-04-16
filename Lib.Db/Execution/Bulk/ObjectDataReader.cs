// ============================================================================
// 파일: Lib.Db/Execution/Bulk/ObjectDataReader.cs
// 설명: IEnumerable<T> → IDataReader 어댑터 (SqlBulkCopy 전용)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Lib.Db.Execution.Bulk;

#region ObjectDataReader<T> 구현

/// <summary>
/// <see cref="IEnumerable{T}"/>를 <see cref="IDataReader"/>로 변환하는 어댑터입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>SqlBulkCopy 호환</b>: SqlBulkCopy.WriteToServerAsync가 IDataReader를 요구하므로,
///   POCO 컬렉션을 IDataReader로 래핑하여 스트리밍 방식으로 전달합니다.<br/>
/// - <b>최소 구현</b>: SqlBulkCopy가 실제로 호출하는 메서드(Read, GetValue, FieldCount, GetName)만
///   구현하고 나머지는 NotSupportedException을 반환합니다.<br/>
/// - <b>Reflection 사용</b>: AOT 비호환이므로 RequiresUnreferencedCode 어트리뷰트가 적용됩니다.
/// </para>
/// </summary>
/// <typeparam name="T">레코드 타입</typeparam>
[RequiresUnreferencedCode("ObjectDataReader는 Reflection을 사용하여 T의 속성을 열거합니다.")]
internal sealed class ObjectDataReader<T>(IEnumerator<T> enumerator, PropertyInfo[] properties)
    : IDataReader
{
    #region 필드 선언 (C# 14)

    private bool _disposed;

    // [성능 최적화] 타입별 Expression Tree 컴파일 getter 캐시 (T당 1회만 컴파일)
    // Reflection.GetValue() 대비 ~10x 빠른 접근 속도를 제공합니다.
    private static readonly ConcurrentDictionary<Type, Func<object, object?>[]> s_getterCache = new();

    private readonly Func<object, object?>[] _getters = s_getterCache.GetOrAdd(
        typeof(T), static type =>
        {
            PropertyInfo[] props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            Func<object, object?>[] getters = new Func<object, object?>[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                ParameterExpression param = Expression.Parameter(typeof(object), "obj");
                UnaryExpression cast = Expression.Convert(param, type);
                MemberExpression access = Expression.Property(cast, props[i]);
                UnaryExpression box = Expression.Convert(access, typeof(object));
                getters[i] = Expression.Lambda<Func<object, object?>>(box, param).Compile();
            }
            return getters;
        });

    #endregion

    #region IDataReader 핵심 구현

    /// <summary>현재 행의 필드 수를 반환합니다.</summary>
    public int FieldCount => properties.Length;

    /// <summary>다음 행으로 이동합니다.</summary>
    public bool Read()
    {
        if (_disposed) return false;
        return enumerator.MoveNext();
    }

    /// <summary>지정된 인덱스의 필드 값을 반환합니다.</summary>
    public object GetValue(int i) => _getters[i](enumerator.Current!) ?? DBNull.Value;

    /// <summary>지정된 인덱스의 필드 이름을 반환합니다.</summary>
    public string GetName(int i) => properties[i].Name;

    /// <summary>지정된 이름의 필드 인덱스를 반환합니다.</summary>
    public int GetOrdinal(string name)
    {
        for (int i = 0; i < properties.Length; i++)
        {
            if (string.Equals(properties[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new IndexOutOfRangeException($"필드 '{name}'을 찾을 수 없습니다.");
    }

    #endregion

    #region IDataReader 보조 구현 (SqlBulkCopy 호환)

    /// <summary>다음 결과셋으로 이동합니다 (단일 결과셋이므로 항상 false).</summary>
    public bool NextResult() => false;

    /// <summary>리더가 닫혀 있는지 여부를 반환합니다.</summary>
    public bool IsClosed => _disposed;

    /// <summary>현재 행의 깊이를 반환합니다 (항상 0).</summary>
    public int Depth => 0;

    /// <summary>영향받은 행 수를 반환합니다 (SqlBulkCopy에서 미사용, -1 반환).</summary>
    public int RecordsAffected => -1;

    /// <summary>리더를 닫습니다.</summary>
    public void Close() => Dispose();

    /// <summary>지정된 인덱스의 필드 타입을 반환합니다.</summary>
    public Type GetFieldType(int i) => properties[i].PropertyType;

    /// <summary>지정된 인덱스의 데이터 타입 이름을 반환합니다.</summary>
    public string GetDataTypeName(int i) => properties[i].PropertyType.Name;

    /// <summary>모든 필드 값을 배열에 복사합니다.</summary>
    public int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, properties.Length);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    /// <summary>지정된 인덱스의 값이 DBNull인지 확인합니다.</summary>
    public bool IsDBNull(int i) => _getters[i](enumerator.Current!) is null;

    /// <summary>인덱서 (정수).</summary>
    public object this[int i] => GetValue(i);

    /// <summary>인덱서 (이름).</summary>
    public object this[string name] => GetValue(GetOrdinal(name));

    #endregion

    #region IDataReader 미사용 메서드 (NotSupportedException)

    /// <summary>스키마 테이블을 반환합니다 (미지원).</summary>
    public DataTable? GetSchemaTable() => null;

    /// <summary>boolean 값을 반환합니다.</summary>
    public bool GetBoolean(int i) => (bool)GetValue(i);

    /// <summary>byte 값을 반환합니다.</summary>
    public byte GetByte(int i) => (byte)GetValue(i);

    /// <summary>바이트 배열을 읽습니다 (미지원).</summary>
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
        => throw new NotSupportedException();

    /// <summary>char 값을 반환합니다.</summary>
    public char GetChar(int i) => (char)GetValue(i);

    /// <summary>문자 배열을 읽습니다 (미지원).</summary>
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
        => throw new NotSupportedException();

    /// <summary>IDataReader를 반환합니다 (미지원).</summary>
    public IDataReader GetData(int i) => throw new NotSupportedException();

    /// <summary>DateTime 값을 반환합니다.</summary>
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);

    /// <summary>decimal 값을 반환합니다.</summary>
    public decimal GetDecimal(int i) => (decimal)GetValue(i);

    /// <summary>double 값을 반환합니다.</summary>
    public double GetDouble(int i) => (double)GetValue(i);

    /// <summary>float 값을 반환합니다.</summary>
    public float GetFloat(int i) => (float)GetValue(i);

    /// <summary>Guid 값을 반환합니다.</summary>
    public Guid GetGuid(int i) => (Guid)GetValue(i);

    /// <summary>short 값을 반환합니다.</summary>
    public short GetInt16(int i) => (short)GetValue(i);

    /// <summary>int 값을 반환합니다.</summary>
    public int GetInt32(int i) => (int)GetValue(i);

    /// <summary>long 값을 반환합니다.</summary>
    public long GetInt64(int i) => (long)GetValue(i);

    /// <summary>string 값을 반환합니다.</summary>
    public string GetString(int i) => (string)GetValue(i);

    #endregion

    #region IDisposable 구현

    /// <summary>리소스를 해제합니다.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            enumerator.Dispose();
        }
    }

    #endregion
}

#endregion
