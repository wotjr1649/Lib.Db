// ============================================================================
// File: Lib.Db/Contracts/Models/TvpModels.cs
// Role: TVP(Table-Valued Parameter) 모델, 접근자, 메타데이터 특성 정의
// Env : .NET 10 / C# 14
// Notes:
//   - TvpAccessors: 비제네릭/제네릭 접근자 컨테이너
//   - BuildSchemaTable: 프로퍼티 -> DataTable 변환 (Half 지원 포함)
//   - Attributes: 길이/정밀도/행 마커 등 메타데이터 특성
// ============================================================================

#nullable enable

using System.Collections.Frozen;
using System.Reflection;
using Lib.Db.Contracts.Schema;

namespace Lib.Db.Contracts.Models;

/// <summary>
/// 비제네릭 TVP 접근자 컨테이너입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>메타데이터 캐시</b>: DTO 프로퍼티 정보, Getter 델리게이트, 스키마 매핑 정보를 한 번 생성 후 재사용합니다.<br/>
/// - <b>다형성 지원</b>: 제네릭을 모르는 컨텍스트(예: 비제네릭 캐시)에서도 접근자 정보를 활용할 수 있도록 베이스 클래스 역할을 수행합니다.
/// </para>
/// <para>
/// DTO(행 타입)의 공개 프로퍼티 목록과 Getter 델리게이트, 컬럼 Ordinal 매핑,
/// 그리고 SqlBulkCopy/TVP 바인딩에 사용할 스키마(DataTable)를 함께 보관합니다.
/// </para>
/// </summary>
public record TvpAccessors
{
    #region 핵심 속성 (접근자/스키마)

    /// <summary>
    /// TVP DTO의 공개(읽기 가능) 프로퍼티 목록입니다.
    /// <para>
    /// 순서는 TVP 컬럼 순서(Ordinal)와 동일하게 맞추는 것을 권장합니다.
    /// </para>
    /// </summary>
    public required PropertyInfo[] Properties { get; init; }

    /// <summary>
    /// object 기반 Getter 델리게이트 배열입니다.
    /// <para>
    /// 각 인덱스는 <see cref="Properties"/>의 동일 인덱스 프로퍼티와 1:1로 대응합니다.
    /// </para>
    /// </summary>
    public required Func<object, object?>[] Accessors { get; init; }

    /// <summary>
    /// 컬럼명(대소문자 무시) → Ordinal 매핑입니다.
    /// <para>
    /// DB 스키마 컬럼명과 DTO 프로퍼티명 매칭 시 빠른 조회를 위해 사용합니다.
    /// </para>
    /// </summary>
    public required FrozenDictionary<string, int> OrdinalMap { get; init; }

    /// <summary>
    /// SqlBulkCopy / TVP 바인딩에 사용할 스키마 테이블(DataTable)입니다.
    /// <para>
    /// 내부적으로 <c>SchemaTableColumn</c> 규격에 맞는 컬럼을 구성하며,
    /// 각 행(Row)은 TVP 컬럼 정의(이름/순서/타입/Null 허용/길이/정밀도 등)를 나타냅니다.
    /// </para>
    /// </summary>
    public required DataTable SchemaTable { get; init; }

    #endregion

    #region 검증 상태

    /// <summary>
    /// TVP DTO와 DB TVP 스키마 구조가 검증되었는지 여부를 나타내는 내부 플래그입니다.
    /// <para>
    /// - 멀티 스레드 환경에서 안전한 읽기/쓰기를 위해 int + <see cref="Volatile"/>를 사용합니다.<br/>
    /// - 값은 0(미검증), 1(검증 완료)로 저장합니다.
    /// </para>
    /// </summary>
    private int _isValidated;

    /// <summary>
    /// TVP 스키마 검증 완료 여부입니다.
    /// <para>
    /// - <c>true</c>로 설정되면 이후 동일 인스턴스에 대한 검증 과정을 생략할 수 있습니다.<br/>
    /// - <see cref="Volatile"/>을 사용하므로 다중 스레드에서도 일관성을 보장합니다.
    /// </para>
    /// </summary>
    public bool IsValidated
    {
        get => Volatile.Read(ref _isValidated) != 0;
        set => Volatile.Write(ref _isValidated, value ? 1 : 0);
    }

    #endregion

    #region 선택 정보 (SQL 타입 이름)

    /// <summary>
    /// 명시적으로 지정된 SQL TVP 타입 이름입니다. (선택)
    /// <para>
    /// 이 값이 있으면 <c>DataTable.TableName</c>이나 DTO 이름보다 우선하여
    /// 실제 SQL TVP 타입 이름으로 사용될 수 있습니다.
    /// </para>
    /// <para>예: <c>"dbo.MyTvpType"</c></para>
    /// </summary>
    public string? SqlTypeName { get; init; }

    #endregion

    #region 스키마 테이블 생성기

    /// <summary>
    /// [AOT 지원] 프로퍼티 정보를 기반으로 TVP용 스키마 테이블(DataTable)을 생성합니다.
    /// <para>
    /// Source Generator가 생성한 코드나 내부 캐시가 공통으로 사용할 수 있는 표준 빌더입니다.
    /// </para>
    /// <para>
    /// 생성되는 DataTable은 <c>SchemaTableColumn</c> 규격 컬럼을 포함하며,
    /// 각 프로퍼티를 한 행(Row)으로 추가합니다.
    /// </para>
    /// </summary>
    /// <param name="props">TVP 행(DTO)의 프로퍼티 목록</param>
    /// <returns>TVP 바인딩에 사용할 스키마 테이블</returns>
    public static DataTable BuildSchemaTable(PropertyInfo[] props)
    {
        var schemaTable = new DataTable();

        // --------------------------------------------------------------------
        // 1) 기본 식별 정보: 컬럼명/순서/타입/NULL 허용 여부
        // --------------------------------------------------------------------
        schemaTable.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
        schemaTable.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
        schemaTable.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
        schemaTable.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));

        // --------------------------------------------------------------------
        // 2) 데이터 크기/정밀도 정보: 문자열/바이너리 길이, 숫자/시간 계열 Precision/Scale
        // --------------------------------------------------------------------
        schemaTable.Columns.Add(SchemaTableColumn.ColumnSize, typeof(int));
        schemaTable.Columns.Add(SchemaTableColumn.NumericPrecision, typeof(short));
        schemaTable.Columns.Add(SchemaTableColumn.NumericScale, typeof(short));

        // --------------------------------------------------------------------
        // 3) 메타데이터 플래그(확장): 필요 시 바인딩/검증 단계에서 활용 가능
        //    (기본값은 false)
        // --------------------------------------------------------------------
        schemaTable.Columns.Add(SchemaTableColumn.IsUnique, typeof(bool));
        schemaTable.Columns.Add(SchemaTableColumn.IsKey, typeof(bool));
        schemaTable.Columns.Add("IsRowVersion", typeof(bool));
        schemaTable.Columns.Add(SchemaTableColumn.IsLong, typeof(bool));
        schemaTable.Columns.Add("IsReadOnly", typeof(bool));
        schemaTable.Columns.Add("IsAutoIncrement", typeof(bool));

        // --------------------------------------------------------------------
        // 4) 프로퍼티 → 스키마 행(Row) 생성
        // --------------------------------------------------------------------
        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];

            // Nullable<T>이면 실제 타입(T)로, 아니면 원 타입 그대로 사용
            var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            // ----------------------------------------------------------------
            // [Hotfix] .NET 10 Half 타입 지원
            // ----------------------------------------------------------------
            // 원인: SQL Client는 System.Half 타입을 인식하지 못해 ArgumentException 발생
            // 해결: 값은 float으로 변환되어 전달되므로(TvpPrimitives/DataBinding), 
            //       스키마(명찰) 또한 float으로 설정하여 SqlClient를 통과시킴.
            // ----------------------------------------------------------------
            if (underlyingType == typeof(Half))
            {
                underlyingType = typeof(float);
            }

            // 참조형은 기본적으로 Nullable, 값형은 Nullable<T>인 경우만 Nullable로 판단
            var isNullable = !prop.PropertyType.IsValueType ||
                             Nullable.GetUnderlyingType(prop.PropertyType) is not null;

            // 기본값: 미지정(-1), 정밀도/스케일(0)
            short precision = 0;
            short scale = 0;
            int size = -1;

            // 선택적 메타데이터(특성) 읽기
            var precAttr = prop.GetCustomAttribute<TvpPrecisionAttribute>();
            var lengthAttr = prop.GetCustomAttribute<TvpLengthAttribute>();

            // ----------------------------------------------------------------
            // (A) Decimal / Money 계열: Precision/Scale 설정
            //     - 특성이 있으면 그 값을 사용
            //     - 없으면 보수적으로 (38, 4) 기본값을 부여
            // ----------------------------------------------------------------
            if (underlyingType == typeof(decimal))
            {
                if (precAttr is not null)
                {
                    precision = precAttr.Precision;
                    scale = precAttr.Scale;
                }
                else
                {
                    precision = 38;
                    scale = 4;
                }
            }
            // ----------------------------------------------------------------
            // (B) DateTime / DateTimeOffset / TimeSpan 계열: Scale 설정
            //     - SQL Server 기준 fractional seconds 정밀도로 사용되는 경우가 많음
            //     - 특성이 있으면 그 값을 사용
            //     - 없으면 (7) 기본값 사용
            // ----------------------------------------------------------------
            else if (underlyingType == typeof(DateTime) ||
                     underlyingType == typeof(DateTimeOffset) ||
                     underlyingType == typeof(TimeSpan))
            {
                if (precAttr is not null)
                {
                    scale = precAttr.Scale;
                }
                else
                {
                    scale = 7;
                }
            }

            // ----------------------------------------------------------------
            // (C) 문자열/바이너리 길이: Length 특성이 있을 때만 size를 양수로 설정
            //     - 기본(-1)은 "길이 미지정" 의미
            // ----------------------------------------------------------------
            if (lengthAttr is not null)
            {
                size = lengthAttr.Length;
            }

            // ----------------------------------------------------------------
            // 최종 Row 추가 (ColumnName / Ordinal / Type / Nullable / Size / Precision / Scale ...)
            // ----------------------------------------------------------------
            schemaTable.Rows.Add(
                prop.Name,          // ColumnName
                i,                  // ColumnOrdinal
                underlyingType,     // DataType
                isNullable,         // AllowDBNull
                size,               // ColumnSize (기본 -1, LengthAttribute 있을 때만 양수)
                precision,          // NumericPrecision
                scale,              // NumericScale
                false,              // IsUnique
                false,              // IsKey
                false,              // IsRowVersion
                false,              // IsLong
                false,              // IsReadOnly
                false               // IsAutoIncrement
            );
        }

        return schemaTable;
    }

    #endregion
}


#region 버퍼 주입 (AOT/SG 최적화)

/// <summary>
/// [AOT/SG 성능 최적화] TVP 컬럼 버퍼에 데이터를 Boxing 없이 직접 주입하기 위한 인터페이스입니다.
/// <para>
/// Source Generator가 컬럼별 버퍼(예: <see cref="ITvpColumn{TValue}"/>)를 생성하고,
/// 런타임에서는 이를 통해 고속으로 값을 누적(Add)할 수 있습니다.
/// </para>
/// </summary>
/// <typeparam name="TValue">컬럼 값 타입</typeparam>
public interface ITvpColumn<in TValue>
{
    /// <summary>
    /// 컬럼 버퍼에 값을 추가합니다.
    /// </summary>
    /// <param name="value">추가할 값</param>
    void Add(TValue value);
}

#endregion

#region 제네릭 접근자

/// <summary>
/// TVP 접근자 컨테이너(제네릭)입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>Type Safety</b>: 제네릭 T를 인지하여 Boxing/Unboxing 없는 최적화된 접근 경로를 제공합니다.<br/>
/// - <b>AOT 확장</b>: Source Generator가 생성하는 정적 검증기(<c>StaticValidator</c>)와 고속 버퍼 주입기(<c>BufferAdder</c>)를 담을 수 있는 슬롯을 제공합니다.
/// </para>
/// <para>
/// <see cref="TvpAccessors"/>의 비제네릭 메타데이터를 그대로 상속하면서,
/// 제네릭 T 기반의 TypedAccessors 및 AOT/Source Generator 최적화 요소를 추가 제공합니다.
/// </para>
/// </summary>
/// <typeparam name="T">TVP 행(DTO) 타입</typeparam>
public sealed record TvpAccessors<T> : TvpAccessors
{
    #region 핵심 속성 (제네릭)

    /// <summary>
    /// 제네릭 T 기반 Getter 델리게이트 배열입니다.
    /// <para>
    /// boxing이 줄어들고 호출 비용이 감소하여 대량 행 처리에서 유리합니다.
    /// </para>
    /// </summary>
    public required Func<T, object?>[] TypedAccessors { get; init; }

    /// <summary>
    /// [AOT 지원] Source Generator가 생성한 정적 검증기입니다. (선택)
    /// <para>
    /// 런타임 리플렉션 검증 대신, 컴파일 타임에 생성된 검증 로직을 사용할 수 있습니다.
    /// </para>
    /// </summary>
    public ITvpStaticValidator<T>? StaticValidator { get; init; }

    /// <summary>
    /// [AOT/SG 성능 최적화] 컬럼 버퍼에 값을 직접 주입하는 고속 델리게이트입니다. (선택)
    /// <para>
    /// 시그니처: <c>(T item, object[] buffers)</c><br/>
    /// 내부적으로 <paramref name="buffers"/>의 각 원소를 <see cref="ITvpColumn{TValue}"/>로 캐스팅하여
    /// 컬럼별 버퍼에 값을 추가(Add)하는 방식으로 동작할 수 있습니다.
    /// </para>
    /// </summary>
    public Action<T, object[]>? BufferAdder { get; init; }

    #endregion
}

#endregion

#region 특성 (TVP 메타데이터)

/// <summary>
/// TVP 길이 지정 특성입니다.
/// <para>
/// 문자열/바이너리 계열 컬럼의 길이를 명시적으로 지정할 때 사용합니다.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TvpLengthAttribute(int length) : Attribute
{
    /// <summary>
    /// 길이 값입니다. (예: NVARCHAR(50) → 50)
    /// </summary>
    public int Length { get; } = length;
}

/// <summary>
/// Source Generator가 TVP 접근자 코드를 생성할 대상 DTO에 부여하는 마커 특성입니다.
/// <para>
/// 이 특성이 붙은 타입을 기준으로 TVP 접근자/검증기/버퍼 주입기 등의 코드를 생성할 수 있습니다.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class TvpRowAttribute : Attribute
{
    /// <summary>
    /// 명시적 SQL TVP 타입 이름입니다. (스키마 포함 가능)
    /// <para>예: <c>"dbo.MyTvpType"</c></para>
    /// </summary>
    public string? TypeName { get; set; }
}

/// <summary>
/// TVP 정밀도(Precision)/스케일(Scale) 지정 특성입니다.
/// <para>
/// - Decimal 계열: Precision/Scale<br/>
/// - DateTime/TimeSpan 계열: Scale(소수 초 정밀도)로 활용 가능
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TvpPrecisionAttribute(byte precision, byte scale) : Attribute
{
    /// <summary>
    /// 정밀도(precision)입니다. (주로 decimal 계열에서 사용)
    /// </summary>
    public byte Precision { get; } = precision;

    /// <summary>
    /// 스케일(scale)입니다. (decimal 또는 시간 계열의 소수 정밀도에 사용)
    /// </summary>
    public byte Scale { get; } = scale;
}

#endregion