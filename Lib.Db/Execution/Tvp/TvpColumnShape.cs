// ============================================================================
// 파일: Execution/Tvp/TvpColumnShape.cs
// 설명: 런타임 TVP reader 컬럼 정의
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// 런타임 TVP row reader가 노출할 컬럼의 형태입니다.
/// </summary>
public sealed record TvpColumnShape
{
    private string _name = string.Empty;
    private Type _fieldType = typeof(object);

    /// <summary>
    /// TVP 컬럼 정의를 생성하고 컬럼 식별자와 CLR 타입을 검증합니다.
    /// </summary>
    public TvpColumnShape(
        string name,
        Type fieldType,
        bool allowNull,
        int size = 0,
        byte precision = 0,
        byte scale = 0,
        SqlDbType? dbType = null)
    {
        Name = name;
        FieldType = fieldType;
        AllowNull = allowNull;
        Size = size;
        Precision = precision;
        Scale = scale;
        DbType = dbType;
    }

    /// <summary>
    /// SQL Server TVP 컬럼 이름입니다. 안전한 식별자 형태만 허용합니다.
    /// </summary>
    public string Name
    {
        get => _name;
        init => _name = ValidateName(value);
    }

    /// <summary>
    /// 컬럼 값의 CLR 타입입니다.
    /// </summary>
    public Type FieldType
    {
        get => _fieldType;
        init => _fieldType = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// 컬럼이 null 값을 허용하는지 여부입니다.
    /// </summary>
    public bool AllowNull { get; init; }

    /// <summary>
    /// 문자열/바이너리 컬럼 크기입니다.
    /// </summary>
    public int Size { get; init; }

    /// <summary>
    /// decimal 컬럼 precision입니다.
    /// </summary>
    public byte Precision { get; init; }

    /// <summary>
    /// decimal 또는 time 계열 컬럼 scale입니다.
    /// </summary>
    public byte Scale { get; init; }

    /// <summary>
    /// 명시적으로 확인된 SQL Server 컬럼 타입입니다.
    /// </summary>
    public SqlDbType? DbType { get; init; }

    /// <summary>
    /// null을 허용하지 않는 필수 컬럼 정의를 생성합니다.
    /// </summary>
    /// <param name="name">컬럼 이름입니다.</param>
    /// <param name="fieldType">CLR 필드 타입입니다.</param>
    /// <param name="size">문자열/바이너리 컬럼 크기입니다.</param>
    /// <param name="precision">decimal 컬럼 precision입니다.</param>
    /// <param name="scale">decimal 또는 time 계열 컬럼 scale입니다.</param>
    /// <returns>필수 컬럼 정의입니다.</returns>
    public static TvpColumnShape Required(
        string name,
        Type fieldType,
        int size = 0,
        byte precision = 0,
        byte scale = 0)
        => new(ValidateName(name), fieldType, allowNull: false, size, precision, scale);

    /// <summary>
    /// null을 허용하는 선택 컬럼 정의를 생성합니다.
    /// </summary>
    /// <param name="name">컬럼 이름입니다.</param>
    /// <param name="fieldType">CLR 필드 타입입니다.</param>
    /// <param name="size">문자열/바이너리 컬럼 크기입니다.</param>
    /// <param name="precision">decimal 컬럼 precision입니다.</param>
    /// <param name="scale">decimal 또는 time 계열 컬럼 scale입니다.</param>
    /// <returns>선택 컬럼 정의입니다.</returns>
    public static TvpColumnShape Optional(
        string name,
        Type fieldType,
        int size = 0,
        byte precision = 0,
        byte scale = 0)
        => new(ValidateName(name), fieldType, allowNull: true, size, precision, scale);

    internal static TvpColumnShape FromSql<TValue>(
        string name,
        SqlDbType dbType,
        bool allowNull,
        int size,
        byte precision,
        byte scale)
        => new(
            ValidateName(name),
            ResolveFieldType(dbType, typeof(TValue)),
            allowNull || Nullable.GetUnderlyingType(typeof(TValue)) is not null,
            size,
            precision,
            scale,
            dbType);

    private static Type ResolveFieldType(SqlDbType dbType, Type accessorType)
    {
        Type normalized = Nullable.GetUnderlyingType(accessorType) ?? accessorType;

        return dbType switch
        {
            SqlDbType.BigInt => typeof(long),
            SqlDbType.Binary or SqlDbType.Image or SqlDbType.Timestamp or SqlDbType.VarBinary => typeof(byte[]),
            SqlDbType.Bit => typeof(bool),
            SqlDbType.Char or SqlDbType.NChar or SqlDbType.NText or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.VarChar or SqlDbType.Xml => typeof(string),
            SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime => typeof(DateTime),
            SqlDbType.DateTimeOffset => typeof(DateTimeOffset),
            SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => typeof(decimal),
            SqlDbType.Float => typeof(double),
            SqlDbType.Int => typeof(int),
            SqlDbType.Real => typeof(float),
            SqlDbType.SmallInt => typeof(short),
            SqlDbType.Structured => normalized,
            SqlDbType.Time => typeof(TimeSpan),
            SqlDbType.TinyInt => typeof(byte),
            SqlDbType.UniqueIdentifier => typeof(Guid),
            _ => normalized
        };
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("TVP column name is required.", nameof(name));

        string normalized = name.Trim();
        if (!TvpTypeName.IsSafeIdentifier(normalized))
            throw new ArgumentException("Invalid TVP column name.", nameof(name));

        return normalized;
    }
}
