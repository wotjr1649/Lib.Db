// ============================================================================
// File: Lib.Db/Execution/Binding/DbBinding.cs
// Role: 데이터 바인딩, TVP 변환, 유효성 검증을 총괄하는 단일 엔진
// Merged: DataBindingEngine + DataBindingHelpers + TvpFactory
// Env : .NET 10 / C# 14
// Notes:
//   - SP/Raw SQL/TVP/BulkCopy 단일 진입점(DbBinder)
//   - Decimal/정수/Enum 오버플로우 사전 검증 + 한글 컨텍스트 예외
//   - 문자열 전처리/JSON 직렬화/Stream & LOB(MAX) 지원
//   - Smart TVP 감지 (AOT 호환) 및 Columnar TVP + Bounded Cache
// ============================================================================

#nullable enable

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis; // [AOT] NotNullWhen 등
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Tvp;
using System.Runtime.InteropServices; // [Zero-Copy] CollectionsMarshal

namespace Lib.Db.Execution.Binding;

/// <summary>
/// DB 파라미터 바인딩 및 TVP 처리를 담당하는 고성능 정적 엔진입니다.
/// </summary>
/// <remarks>
/// <para><strong>📊 설계 의도 (Intent)</strong></para>
/// <list type="bullet">
/// <item><strong>단일 진입점 패턴</strong>: SP/Raw SQL/TVP/BulkCopy 모든 파라미터 바인딩 로직을 DbBinder로 통합</item>
/// <item><strong>AOT 호환성</strong>: Source Generator 기반 TvpAccessor로 Reflection 제거</item>
/// <item><strong>사전 검증</strong>: Decimal/정수/DateTime 범위를 DB 전송 전에 검증하여 런타임 오류 방지</item>
/// <item><strong>스마트 TVP 감지</strong>: IEnumerable&lt;T&gt;를 자동 인식하여 JSON 직렬화 오류 방지</item>
/// </list>
/// 
/// <para><strong>💡 핵심 기능 (Core Features)</strong></para>
/// <list type="bullet">
/// <item><strong>SP 메타데이터 기반 바인딩</strong>: NOT NULL/DEFAULT 검증, Precision/Scale 오버플로우 검사</item>
/// <item><strong>Raw SQL 바인딩</strong>: 스키마 없이도 Smart TVP Detection으로 자동 변환</item>
/// <item><strong>TVP(Table-Valued Parameter)</strong>: Columnar 버퍼로 Zero-Allocation POCO → TVP 변환</item>
/// <item><strong>BulkCopy 헬퍼</strong>: SqlBulkCopy에 사용할 IDataReader 제공</item>
/// <item><strong>JSON 자동 직렬화</strong>: 복합 객체 감지 시 NVarChar로 자동 변환</item>
/// <item><strong>문자열 전처리</strong>: 공백/제어문자 제거, Size 기반 Truncate</item>
/// </list>
/// 
/// <para><strong>⚡ 성능 특성 (Performance)</strong></para>
/// <list type="bullet">
/// <item><strong>메모리 할당</strong>: Columnar TVP Reader로 Zero-Allocation 패턴 구현</item>
/// <item><strong>시간 복잡도</strong>: O(1) 바인딩, O(N) TVP 변환</item>
/// <item><strong>Bounded Cache</strong>: TVP Reader Factory 및 Validation State를 최대 10,000개까지 캐싱</item>
/// <item><strong>Columnar 레이아웃</strong>: 행 기반이 아닌 열 기반 메모리 레이아웃으로 캐시 친화적</item>
/// <item><strong>AggressiveOptimization</strong>: 핵심 메서드에 적용하여 JIT 최적화</item>
/// </list>
/// 
/// <para><strong>🔒 데이터 무결성 (Data Integrity)</strong></para>
/// <list type="bullet">
/// <item><strong>NOT NULL 검증</strong>: Strict 모드에서 NOT NULL 위반 시 예외 발생</item>
/// <item><strong>오버플로우 사전 검증</strong>: Decimal(precision, scale), TinyInt/SmallInt/Int 범위, DateTime 범위</item>
/// <item><strong>SQL Injection 방어</strong>: 문자열 전처리로 제어문자 제거</item>
/// <item><strong>TVP 스키마 검증</strong>: ValidatorCallback으로 DTO 타입과 TVP 타입명 일치 확인</item>
/// </list>
/// 
/// <para><strong>⚠️ 예외 처리 (Exceptions)</strong></para>
/// <list type="bullet">
/// <item><strong>ArgumentException</strong>: NOT NULL 위반, 필수 파라미터 누락</item>
/// <item><strong>ArgumentOutOfRangeException</strong>: Overflow(오버플로우), DateTime 범위 초과</item>
/// <item><strong>InvalidOperationException</strong>: TVP 리더 생성 실패, 지원되지 않는 컬렉션 타입</item>
/// <item><strong>상세 컨텍스트</strong>: 파라미터 이름, 현재 값, 허용 범위, SQL 타입, Precision/Scale, SP 이름 포함</item>
/// </list>
/// 
/// <para><strong>🛡️ 스레드 안전성 (Thread Safety)</strong></para>
/// <list type="bullet">
/// <item><strong>Thread-Safe</strong>: 모든 public 메서드는 동시 호출 가능 (static class)</item>
/// <item><strong>ConcurrentDictionary</strong>: TVP Reader Cache 및 Validation Cache는 동시성 안전</item>
/// <item><strong>Stateless</strong>: 모든 상태는 캐시에만 저장, 메서드 호출은 순수 함수형</item>
/// </list>
/// 
/// <para><strong>🛠️ 유지보수 및 확장성 (Maintenance)</strong></para>
/// <list type="bullet">
/// <item><strong>ValidatorCallback</strong>: 외부에서 TVP 스키마 검증 로직 주입 가능</item>
/// <item><strong>ConfigureTvp</strong>: 캐시 크기 등 정책 동적 구성</item>
/// <item><strong>ClearTvpCaches</strong>: 메모리 압박 시 캐시 초기화 가능</item>
/// <item><strong>DbParameterAttribute</strong>: 명시적 메타데이터 오버라이드 지원</item>
/// <item><strong>Breaking Change 위험</strong>: TVP 포맷 변경 시 영향도 높음</item>
/// </list>
/// 
/// <para><strong>📈 TVP(Table-Valued Parameter) 처리</strong></para>
/// <list type="number">
/// <item><strong>TvpAccessorCache</strong>: Source Generator가 생성한 Accessor 캐싱 (Reflection 제거)</item>
/// <item><strong>Columnar Buffer</strong>: 각 프로퍼티별로 값을 별도 배열에 저장</item>
/// <item><strong>JSON 자동 직렬화</strong>: 복합 객체 감지 시 NVarChar로 변환</item>
/// <item><strong>스키마 검증</strong>: ValidatorCallback으로 DTO 타입과 TVP 타입명 일치 확인</item>
/// </list>
/// </remarks>
[SkipLocalsInit]
public static partial class DbBinder
{
    // =========================================================================
    // 0. 정적 설정 및 브리지
    // =========================================================================

    #region [0. 정적 설정 및 브리지]

    /// <summary>
    /// Decimal 정밀도/스케일 오버플로우 검사용 10의 거듭제곱 캐시입니다.
    /// <para>precision - scale 자리수까지의 최대 정수부를 빠르게 판별하기 위해 사용합니다.</para>
    /// </summary>
    private static readonly decimal[] s_powersOf10 =
    [
        1m, 10m, 100m, 1_000m, 10_000m, 100_000m, 1_000_000m, 10_000_000m, 100_000_000m,
        1_000_000_000m, 10_000_000_000m, 100_000_000_000m, 1_000_000_000_000m,
        10_000_000_000_000m, 100_000_000_000_000m, 1_000_000_000_000_000m, 10_000_000_000_000_000m,
        100_000_000_000_000_000m, 1_000_000_000_000_000_000m, 10_000_000_000_000_000_000m,
        100_000_000_000_000_000_000m, 1_000_000_000_000_000_000_000m, 10_000_000_000_000_000_000_000m,
        100_000_000_000_000_000_000_000m, 1_000_000_000_000_000_000_000_000m,
        10_000_000_000_000_000_000_000_000m, 100_000_000_000_000_000_000_000_000m,
        1_000_000_000_000_000_000_000_000_000m, 10_000_000_000_000_000_000_000_000_000m
    ];

    /// <summary>
    /// TVP 스키마 검증에 사용할 외부 콜백을 설정/조회합니다.
    /// <para>ValidatorCallback은 DTO 타입과 TVP 타입명 일치 여부를 검사하는 데 사용됩니다.</para>
    /// </summary>
    public static Func<Type, string, bool>? ValidatorCallback
    {
        get => Tvp.ValidatorCallback;
        set => Tvp.ValidatorCallback = value;
    }

    /// <summary>
    /// TVP 관련 캐시 정책(최대 캐시 크기 등)을 구성합니다.
    /// <para>
    /// LibDbOptions에서 이미 검증된 값을 사용하므로 별도의 유효성 검사를 수행하지 않습니다.
    /// </para>
    /// </summary>
    public static void ConfigureTvp(LibDbOptions options) => Tvp.Configure(options);

    /// <summary>
    /// TVP 관련 캐시 및 버퍼 팩터리를 모두 초기화합니다.
    /// </summary>
    public static void ClearTvpCaches() => Tvp.ClearCaches();

    #endregion

    // =========================================================================
    // 1. 공개 API - SP 메타데이터 기반 바인딩
    // =========================================================================

    #region [1. SP 메타데이터 기반 바인딩]

    /// <summary>
    /// 스키마 메타데이터(<see cref="SpParameterMetadata"/>)를 기반으로 단일 파라미터를 안전하게 바인딩합니다.
    /// <para>
    /// - NOT NULL/DEFAULT 제약 검증<br/>
    /// - Decimal/정수/Enum 범위 사전 검증<br/>
    /// - 문자열 전처리 및 Size 기반 잘라내기<br/>
    /// - TVP/DataTable/Stream 처리<br/>
    /// - 한글 컨텍스트를 포함한 상세 예외 메시지 제공
    /// </para>
    /// </summary>
    /// <param name="cmd">파라미터를 추가할 <see cref="SqlCommand"/> 인스턴스</param>
    /// <param name="meta">SP 파라미터 메타데이터</param>
    /// <param name="rawValue">원본 값(호출자 전달 값)</param>
    /// <param name="strictCheck">true인 경우 NOT NULL 위반 시 예외를 발생시킵니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void BindParameter(SqlCommand cmd, SpParameterMetadata meta, object? rawValue, bool strictCheck)
    {
        // 1. Null 체크 및 기본값 처리
        bool isNullOrDbNull = rawValue is null || rawValue == DBNull.Value;

        // DB 기본값 사용 (입력 파라미터 + DEFAULT 존재)
        if (isNullOrDbNull && meta.HasDefaultValue && meta.Direction == ParameterDirection.Input)
            return;

        // NOT NULL + Strict 모드 위반
        if (strictCheck && !meta.IsNullable && isNullOrDbNull && meta.Direction == ParameterDirection.Input)
        {
            var baseMsg = $"파라미터 '{meta.Name}'는 필수값입니다. (NOT NULL 제약 조건 위반) " +
                          $"Command: {DbExecutionContextScope.Current?.CommandText ?? "N/A"}, " +
                          $"SQL 타입: {meta.SqlDbType}, Direction: {meta.Direction}";
            var ctx = Context.FromMeta(meta, typeof(object));
            throw new ArgumentException(ctx.CreateErrorMessage(baseMsg), meta.Name);
        }

        object finalValue = rawValue ?? DBNull.Value;

        // 2. 값 변환 및 유효성 검증 (실제 값이 있을 때만)
        if (finalValue != DBNull.Value)
        {
            // 정밀도/범위 오버플로우 사전 검증 (Decimal/정수/Enum/DateTime)
            CheckValueOverflow(meta.Name, finalValue, meta.SqlDbType, meta.Precision, meta.Scale);

            if (finalValue is DateTime valDt)
            {
                CheckDateTimeRange(meta.Name, valDt, meta.SqlDbType);
            }

            if (finalValue is string strVal)
            {
                // 문자열 전처리 (공백/제어문자 제거 등)
                var processedSpan = StringPreprocessor.Sanitize(strVal);

                // Size 기반 Truncate
                if (meta.Size > 0 && processedSpan.Length > meta.Size)
                    finalValue = processedSpan[..(int)meta.Size].ToString();
                else if (processedSpan.Length != strVal.Length)
                    finalValue = processedSpan.ToString();
            }
            // ★ JSON 직렬화: "문자열 컬럼" 이면서 "복합 객체"인 경우에만 수행 (구조적 데이터 보존)
            else if (IsStringColumn(meta.SqlDbType) && IsComplexObject(finalValue))
            {
                finalValue = JsonSerializer.Serialize(finalValue);
            }
            // TVP(Table-Valued Parameter)
            else if (meta.SqlDbType == SqlDbType.Structured)
            {
                finalValue = finalValue switch
                {
                    DataTable dt => ConfigureDataTableTvp(dt, meta.UdtTypeName),
                    IEnumerable list => Tvp.CreateReader(list, meta),
                    _ => finalValue
                };
            }
            // Stream 파라미터는 그대로 통과 (SqlClient가 VarBinary/Binary로 처리)

            // 숫자형 보정 (Enum, 정수 → DB 타입에 맞는 크기로 변환)
            finalValue = NormalizeNumericForDbType(finalValue, meta.SqlDbType);
        }

        // 3. SqlParameter 생성 및 설정
        var sqlParam = cmd.Parameters.Add(
            meta.Name,
            meta.SqlDbType == SqlDbType.Structured ? SqlDbType.Structured : meta.SqlDbType);

        sqlParam.Direction = meta.Direction;

        if (meta.SqlDbType == SqlDbType.Structured)
        {
            sqlParam.TypeName = meta.UdtTypeName;
        }
        else
        {
            // Size, Precision, Scale 설정
            if (meta.Size > 0) sqlParam.Size = (int)meta.Size;
            else if (meta.Size == -1) sqlParam.Size = -1; // MAX

            if (meta.Precision > 0) sqlParam.Precision = meta.Precision;
            if (meta.Scale > 0) sqlParam.Scale = meta.Scale;
        }

        sqlParam.Value = finalValue;
    }

    #endregion

    // =========================================================================
    // 2. 공개 API - 스키마 없는 Raw SQL 바인딩
    // =========================================================================

    #region [2. 스키마 없는 Raw SQL 바인딩 (Smart TVP 감지)]

    /// <summary>
    /// 스키마 정보 없이, 또는 명시적 메타데이터(<see cref="DbParameterAttribute"/>)를 기반으로 파라미터를 바인딩합니다.
    /// <para>
    /// <b>[스마트 감지]</b>
    /// <list type="bullet">
    /// <item><c>IEnumerable&lt;[TvpRow]&gt;</c>가 감지되면 자동으로 TVP로 변환합니다.</item>
    /// <item>복합 객체는 JSON으로 직렬화됩니다.</item>
    /// <item>이름/타입/Size/Precision/Scale 자동 추론</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="cmd">파라미터를 추가할 <see cref="SqlCommand"/> 인스턴스</param>
    /// <param name="name">파라미터 이름(@ 유무는 자동 보정)</param>
    /// <param name="value">파라미터 값</param>
    /// <param name="metaOverride">옵션 메타데이터(Attribute). 설정되어 있으면 추론보다 우선합니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void BindRawParameter(
        SqlCommand cmd,
        string name,
        object? value,
        DbParameterAttribute? metaOverride = null)
    {
        string paramName = name.StartsWith('@') ? name : "@" + name;
        string? tvpTypeName = null; // [Fix] CS0103: 스코프 확장을 위해 상단 선언

        // 1. 이미 SqlParameter인 경우 그대로 추가
        if (value is SqlParameter rawParam)
        {
            rawParam.ParameterName = paramName;
            cmd.Parameters.Add(rawParam);
            return;
        }

        object finalValue = value ?? DBNull.Value;

        // 2. 문자열 전처리
        if (finalValue is string strVal)
        {
            var processedSpan = StringPreprocessor.Sanitize(strVal);
            if (processedSpan.Length != strVal.Length)
                finalValue = processedSpan.ToString();
        }
        // 3. [최적화] TVP 컬렉션 자동 감지 (JSON 직렬화보다 우선!)
        //    List<Dto>를 넘겼을 때 JSON으로 오판하여 AOT 에러가 나는 것을 방지
        else if (IsTvpCollection(finalValue, out var tvpReader, out tvpTypeName))
        {
            finalValue = tvpReader;
        }
        // 4. JSON 직렬화 (복합 객체이면서 TVP/Stream/바이너리가 아닌 경우)
        else if (IsComplexObject(finalValue))
        {
            finalValue = JsonSerializer.Serialize(finalValue);
        }

        // 5. 타입 및 메타데이터 결정
        SqlDbType dbType;
        int size = 0;
        byte precision = 0;
        byte scale = 0;

        if (metaOverride is not null && metaOverride.IsConfigured)
        {
            // Attribute 우선
            dbType = metaOverride.DbType != SqlDbType.Variant
                ? metaOverride.DbType
                : (finalValue is DBNull ? SqlDbType.Variant : InferSqlDbType(finalValue));

            size = metaOverride.Size;
            precision = metaOverride.Precision;
            scale = metaOverride.Scale;
        }
        else
        {
            // 추론
            dbType = finalValue is DBNull ? SqlDbType.Variant : InferSqlDbType(finalValue);

            // 자동 추론 보정 (LOB)
            if (finalValue is byte[] or Stream)
            {
                // Stream은 무조건 -1(MAX), byte[]는 VarBinary인 경우에만 MAX로 처리
                if (finalValue is Stream) size = -1;
                else if (dbType == SqlDbType.VarBinary) size = -1;
            }
        }

        // 6. 파라미터 생성 (생성자 오버로드 활용)
        SqlParameter p;
        if (dbType != SqlDbType.Variant)
        {
            if (size != 0)
                p = new SqlParameter(paramName, dbType, size);
            else
                p = new SqlParameter(paramName, dbType);
        }
        else
        {
            p = new SqlParameter { ParameterName = paramName };
        }

        p.Direction = ParameterDirection.Input;

        // 7. 값 설정 (LOB/Decimal/일반 케이스)
        if (dbType == SqlDbType.Decimal && finalValue is ulong ul)
        {
            // Decimal(38,0)으로 안전하게 승격
            p.Precision = 38;
            p.Scale = 0;
            p.Value = (decimal)ul;
        }
        else if (finalValue is byte[] bytes)
        {
            p.Value = bytes;
            // 생성자에서 Size를 설정하지 않은 경우, VarBinary는 -1(MAX)로 보정
            if (size <= 0 && p.Size == 0) p.Size = -1;
        }
        else if (finalValue is Stream stream)
        {
            p.Value = stream;
            if (size <= 0) p.Size = -1;
        }
        else
        {
            p.Value = finalValue is DBNull ? finalValue : NormalizeNumericForDbType(finalValue, dbType);
        }

        // [보정] TVP TypeName 명시적 설정 (Ad-hoc 쿼리에서 필수)
        if (dbType == SqlDbType.Structured && !string.IsNullOrEmpty(tvpTypeName))
        {
            p.TypeName = tvpTypeName;
        }

        if (precision > 0) p.Precision = precision;
        if (scale > 0) p.Scale = scale;

        cmd.Parameters.Add(p);
    }

    #endregion

    // =========================================================================
    // 3. 공개 API - TVP 및 BulkCopy 헬퍼
    // =========================================================================

    #region [3. TVP 및 BulkCopy 헬퍼]

    /// <summary>
    /// <see cref="IEnumerable{T}"/>를 TVP/BulkCopy에 사용할 수 있는 <see cref="IDataReader"/>로 변환합니다.
    /// <para>
    /// - DataTable/IDataReader는 그대로 통과<br/>
    /// - POCO 컬렉션은 Columnar TVP 리더로 변환됩니다.
    /// </para>
    /// </summary>
    public static IDataReader ToDataReader<T>(IEnumerable<T> data)
        => Tvp.CreateReaderForBulk(data);

    /// <summary>
    /// <see cref="SqlBulkCopy"/>에 컬럼 매핑을 구성합니다.
    /// <para>
    /// - DataTable: 컬럼 이름 그대로 1:1 매핑<br/>
    /// - POCO: <see cref="TvpAccessorCache"/>에서 가져온 Property 메타 기반 매핑
    /// </para>
    /// </summary>
    public static void ConfigureBulkMappings<T>(SqlBulkCopy bulk, IEnumerable<T> data)
    {
        if (data is DataTable dt)
        {
            foreach (DataColumn col in dt.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        }
        else
        {
            var accessors = TvpAccessorCache.GetAccessors<T>();
            foreach (var prop in accessors.Properties)
                bulk.ColumnMappings.Add(prop.Name, prop.Name);
        }
    }

    #endregion

    // =========================================================================
    // 4. 내부 헬퍼 - 변환/검증/추론
    // =========================================================================

    #region [4. 내부 헬퍼 - 변환/검증/추론]

    /// <summary>
    /// DataTable 기반 TVP의 TableName을 TVP 타입명과 일치하도록 보정합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object ConfigureDataTableTvp(DataTable dt, string? typeName)
    {
        // TableName을 TVP 타입명으로 자동 보정 (잘못된 이름도 덮어쓰기)
        if (!string.IsNullOrEmpty(typeName))
            dt.TableName = typeName;
        return dt;
    }

    /// <summary>
    /// Enum/정수/Decimal 값을 대상 DB 타입에 맞게 변환합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object NormalizeNumericForDbType(object value, SqlDbType dbType)
    {
        if (value is Enum e) return ConvertEnum(e, dbType);
        if (value is DataTable or DataRow or IDataReader or Stream) return value;

        // [수정] Half 타입 처리 추가 (.NET 10)
        // SQL Client는 Half를 직접 지원하지 않으므로 float(System.Single)로 변환
        if (value is Half h) return (float)h;

        return ConvertNumeric(value, dbType);
    }

    private static object ConvertEnum(Enum e, SqlDbType t) => t switch
    {
        SqlDbType.TinyInt => Convert.ToByte(e),
        SqlDbType.SmallInt => Convert.ToInt16(e),
        SqlDbType.Int => Convert.ToInt32(e),
        SqlDbType.BigInt => Convert.ToInt64(e),
        SqlDbType.Decimal or SqlDbType.Money => Convert.ToDecimal(e),
        _ => Convert.ToInt32(e)
    };

    private static object ConvertNumeric(object v, SqlDbType t) => t switch
    {
        SqlDbType.TinyInt => Convert.ToByte(v),
        SqlDbType.SmallInt => Convert.ToInt16(v),
        SqlDbType.Int => Convert.ToInt32(v),
        SqlDbType.BigInt => Convert.ToInt64(v),
        SqlDbType.Decimal or SqlDbType.Money => Convert.ToDecimal(v),
        SqlDbType.Real => Convert.ToSingle(v),
        SqlDbType.Float => Convert.ToDouble(v),
        _ => v
    };

    /// <summary>
    /// Decimal/Money 및 정수형/Enum에 대한 오버플로우를 사전 검증합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckValueOverflow(string paramName, object value, SqlDbType dbType, byte precision, byte scale)
    {
        // TVP/리더/Stream 등은 스킵
        if (value is DataTable or DataRow or IDataReader or Stream)
            return;

        // Enum 우선 처리
        if (value is Enum e)
        {
            if (dbType is SqlDbType.Decimal or SqlDbType.Money)
            {
                if (!DecimalFits(Convert.ToDecimal(e), precision, scale))
                    ThrowOverflow(paramName, dbType, e, precision, scale);
                return;
            }

            var underlying = Enum.GetUnderlyingType(e.GetType());
            if (underlying == typeof(byte) || underlying == typeof(ushort) || underlying == typeof(uint) || underlying == typeof(ulong))
                CheckUnsignedIntegerRange(paramName, Convert.ToUInt64(e), dbType, precision, scale);
            else
                CheckIntegerRange(paramName, Convert.ToInt64(e), dbType, precision, scale);

            return;
        }

        // Decimal/Money
        if (dbType is SqlDbType.Decimal or SqlDbType.Money)
        {
            var dec = Convert.ToDecimal(value);
            if (!DecimalFits(dec, precision, scale))
                ThrowOverflow(paramName, dbType, value, precision, scale);
            return;
        }

        // 정수형 타입 (언더플로우/오버플로우)
        if (IsIntegerType(value))
        {
            if (value is ulong ul)
                CheckUnsignedIntegerRange(paramName, ul, dbType, precision, scale);
            else
                CheckIntegerRange(paramName, Convert.ToInt64(value), dbType, precision, scale);
        }
    }

    /// <summary>
    /// precision/scale 제한 내에서 Decimal 값이 표현 가능한지 검사합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool DecimalFits(decimal value, byte precision, byte scale)
    {
        if (precision == 0 || precision <= scale) return true;

        value = Math.Abs(value);
        decimal integerPart = decimal.Truncate(value);
        int maxIntegerDigits = precision - scale;

        if (maxIntegerDigits >= s_powersOf10.Length) return true;
        return integerPart < s_powersOf10[maxIntegerDigits];
    }

    /// <summary>
    /// 오버플로우 발생 시 실행 컨텍스트를 포함한 상세 예외를 생성합니다.
    /// </summary>
    private static void ThrowOverflow(string paramName, SqlDbType dbType, object value, byte precision, byte scale)
    {
        var baseMessage = $"파라미터 '{paramName}' ({dbType})의 값 {value}은(는) DB 제약(Precision:{precision}, Scale:{scale})을 초과합니다. " +
                          $"SQL 타입: {dbType}, 허용 범위: Decimal({precision},{scale})";
        var ctx = new Context(DbExecutionContextScope.Current, paramName.AsSpan(), ReadOnlySpan<char>.Empty, value.GetType());
        throw new ArgumentOutOfRangeException(paramName, ctx.CreateErrorMessage(baseMessage));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIntegerType(object value)
        => value is sbyte or byte or short or ushort or int or uint or long or ulong;

    private static void CheckIntegerRange(string paramName, long value, SqlDbType dbType, byte precision, byte scale)
    {
        bool overflow = dbType switch
        {
            SqlDbType.TinyInt => value is < 0 or > 255,
            SqlDbType.SmallInt => value is < -32768 or > 32767,
            SqlDbType.Int => value is < int.MinValue or > int.MaxValue,
            _ => false
        };

        if (overflow)
            ThrowOverflow(paramName, dbType, value, precision, scale);
    }

    private static void CheckUnsignedIntegerRange(string paramName, ulong value, SqlDbType dbType, byte precision, byte scale)
    {
        bool overflow = dbType switch
        {
            SqlDbType.TinyInt => value > 255,
            SqlDbType.SmallInt => value > 32767,
            SqlDbType.Int => value > 2_147_483_647,
            SqlDbType.BigInt => value > (ulong)long.MaxValue,
            _ => false
        };

        if (overflow)
            ThrowOverflow(paramName, dbType, value, precision, scale);
    }

    /// <summary>
    /// DateTime 값이 SQL Server의 DATETIME 타입 범위(1753-01-01 ~ 9999-12-31)를 준수하는지 검증합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckDateTimeRange(string paramName, DateTime val, SqlDbType dbType)
    {
        // DateTime2는 0001-01-01부터 지원하므로 검증 제외
        if (dbType != SqlDbType.DateTime)
            return;

        // SQL Server DATETIME 최소/최대 범위
        if (val < System.Data.SqlTypes.SqlDateTime.MinValue.Value || val > System.Data.SqlTypes.SqlDateTime.MaxValue.Value)
        {
             var ctx = new Context(DbExecutionContextScope.Current, paramName.AsSpan(), ReadOnlySpan<char>.Empty, typeof(DateTime));
             throw new ArgumentOutOfRangeException(paramName, 
                 ctx.CreateErrorMessage($"파라미터 '{paramName}'의 값 '{val}'은(는) DATETIME 범위를 벗어납니다. (허용: 1753-01-01 ~ 9999-12-31)"));
        }
    }

    /// <summary>
    /// 객체가 JSON 직렬화 대상인 복합 객체인지 확인합니다.
    /// <para>
    /// <b>[최적화]</b> <see cref="IDataReader"/>와 <see cref="DataTable"/>은 구조적 타입이므로 제외합니다.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsComplexObject(object? v) =>
        v is not (
            null or DBNull or string or DateTime or Guid or decimal or Enum or
            DataTable or IDataReader or System.Data.Common.DbParameter or Stream or
            byte[] // byte[]는 JSON 직렬화 대상에서 제외
        )
        && !v.GetType().IsPrimitive;

    /// <summary>
    /// 스키마 없는 파라미터의 SQL 타입을 추론합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStringColumn(SqlDbType t) =>
        t is SqlDbType.NVarChar or SqlDbType.VarChar or SqlDbType.Char or SqlDbType.NChar
            or SqlDbType.Text or SqlDbType.NText;

    private static SqlDbType InferSqlDbType(object v) => v switch
    {
        DataTable or IDataReader => SqlDbType.Structured, // IDataReader 지원 추가
        int => SqlDbType.Int,
        long => SqlDbType.BigInt,
        string => SqlDbType.NVarChar,
        DateTime => SqlDbType.DateTime2,
        bool => SqlDbType.Bit,
        decimal => SqlDbType.Decimal,
        double => SqlDbType.Float,
        Guid => SqlDbType.UniqueIdentifier,
        byte[] => SqlDbType.VarBinary,
        Stream => SqlDbType.VarBinary,
        // [수정] Half는 SQL Server의 Real(4byte float)로 매핑
        Half => SqlDbType.Real,
        _ => SqlDbType.Variant
    };

    /// <summary>
    /// 객체가 <see cref="TvpRowAttribute"/>가 적용된 항목의 컬렉션인지 확인하고, 맞다면 Reader로 변환합니다.
    /// <para>Raw SQL 바인딩 시 JSON 직렬화 오류를 방지하기 위해 사용됩니다.</para>
    /// </summary>
    private static bool IsTvpCollection(object value, [NotNullWhen(true)] out IDataReader? reader, out string? sqlTypeName)
    {
        // 0. [AOT/Fast Path] Check Source Generator Registry
        //    SG가 생성한 ModuleInitializer에 의해 등록된 Factory가 있다면 Reflection 없이 즉시 변환
        if (Tvp.s_enableGeneratedBinder)
        {
            try
            {
                if (TvpFactoryRegistry.TryGet(value.GetType(), out var factory, out sqlTypeName))
                {
                    reader = factory!(value);
                    return true;
                }
            }
            catch
            {
                // SG 경로 실패 시 Reflection Fallback으로 조용히 전환
                // (일시적 오류나 타입 불일치 등)
            }
        }

        reader = null;
        sqlTypeName = null;
        if (value is IEnumerable and not (string or byte[]))
        {
            var type = value.GetType();
            // 배열 또는 제네릭 리스트의 요소 타입 확인
            var elementType = type.IsArray
                ? type.GetElementType()
                : (type.IsGenericType ? type.GetGenericArguments()[0] : null);

            // 요소 타입을 찾지 못했다면 인터페이스 탐색
            if (elementType == null)
            {
                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        elementType = iface.GetGenericArguments()[0];
                        break;
                    }
                }
            }

            // [TvpRow] 특성이 확인되면 TVP Reader 생성
            if (elementType != null && Attribute.IsDefined(elementType, typeof(TvpRowAttribute)))
            {
                // [AOT Note] MakeGenericMethod는 AOT에서 경고를 유발할 수 있으나,
                // TvpRow가 적용된 타입은 보통 Source Generator에 의해 보존되므로 안전할 가능성이 높습니다.
                var method = typeof(Tvp).GetMethod(nameof(Tvp.CreateReaderForBulk), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(elementType);

                reader = (IDataReader)method.Invoke(null, [value])!;
                
                // [추가] TvpRowAttribute에서 TypeName 추출
                var attr = elementType.GetCustomAttribute<TvpRowAttribute>();
                sqlTypeName = attr?.TypeName;

                return true;
            }
        }
        return false;
    }

    #endregion

    // =========================================================================
    // 5. 내부 모듈 - TVP (Columnar Reader, 캐시)
    // =========================================================================

    #region [5. TVP(Table-Valued Parameter) 엔진]

    /// <summary>
    /// TVP(Table-Valued Parameter) 관련 로직을 전담하는 내부 모듈입니다.
    /// <para>
    /// - DTO → Columnar TVP Reader 변환<br/>
    /// - 스키마 검증(ValidatorCallback) + Bounded Cache<br/>
    /// - ColumnBuffer/Adder/팩터리 관리 및 메트릭 연동
    /// </para>
    /// </summary>
    internal static class Tvp
    {
        #region [5-1. 정적 필드 및 설정]

        /// <summary>TVP 관련 캐시의 최대 크기 기본값입니다.</summary>
        private const int DefaultMaxCacheSize = 10_000;

        /// <summary>캐시 최대 크기입니다. LibDbOptions에서 구성됩니다.</summary>
        private static int s_maxCacheSize = DefaultMaxCacheSize;

        /// <summary>DTO Type → TVP Reader 팩터리 캐시입니다.</summary>
        private static readonly ConcurrentDictionary<Type, Func<IEnumerable, IDataReader>> s_readerCache = new();

        /// <summary>(DTO Type, TVP Name) → 스키마 검증 상태 캐시입니다.</summary>
        private static readonly ConcurrentDictionary<(Type ClrType, string TvpName), TvpValidationState> s_validationCache = new();

        /// <summary>외부에서 주입되는 TVP 스키마 검증 콜백입니다.</summary>
        internal static Func<Type, string, bool>? ValidatorCallback { get; set; }

        /// <summary>Source Generator 기반 TVP 바인딩 사용 여부입니다.</summary>
        internal static bool s_enableGeneratedBinder = true;

        /// <summary>TVP 스키마 검증 상태입니다.</summary>
        private enum TvpValidationState : byte
        {
            NotValidated = 0,
            Success = 1,
            Failed = 2
        }

        #endregion

        #region [5-2. 구성 및 초기화]

        /// <summary>
        /// 라이브러리 옵션을 통해 TVP 캐시 정책을 구성합니다.
        /// <para>LibDbOptions에서 이미 검증된 값을 사용하므로 별도의 유효성 검사를 수행하지 않습니다.</para>
        /// </summary>
        internal static void Configure(LibDbOptions options)
        {
            // MinCacheSize 등의 별도 검증은 LibDbOptions에서 이미 수행한다고 가정합니다.
            s_maxCacheSize = options.MaxCacheSize;
            s_enableGeneratedBinder = options.EnableGeneratedTvpBinder;
        }

        /// <summary>
        /// TVP 관련 캐시 및 버퍼 팩터리를 모두 초기화합니다.
        /// </summary>
        internal static void ClearCaches()
        {
            s_readerCache.Clear();
            s_validationCache.Clear();
            BufferAdderCache.Clear();
            ColumnBufferFactory.Clear();
        }

        #endregion

        #region [5-3. Public 진입점 - SP/Raw SQL/BulkCopy]

        /// <summary>
        /// IEnumerable 컬렉션을 검증된 Columnar TVP <see cref="IDataReader"/>로 변환합니다. (SP 바인딩용)
        /// </summary>
        /// <param name="list">TVP로 전송할 DTO 컬렉션</param>
        /// <param name="meta">SP 파라미터 메타데이터</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static IDataReader CreateReader(IEnumerable list, SpParameterMetadata meta)
        {
            ArgumentNullException.ThrowIfNull(list);

            var listType = list.GetType();
            var elementType = TryGetElementType(listType);
            if (elementType is null)
            {
                var ctx = Context.FromMeta(meta, listType);
                throw new InvalidOperationException(
                    ctx.CreateErrorMessage($"'{listType.Name}'은(는) 지원되지 않는 컬렉션 타입입니다.", "[TVP 바인딩 오류]"));
            }

            // 1. TVP 스키마 검증
            ValidateTvpSchema(meta, elementType);

            // 2. Reader 팩터리 조회 (Bounded Cache)
            if (!s_readerCache.TryGetValue(elementType, out var factory))
            {
                if (s_readerCache.Count >= s_maxCacheSize)
                    s_readerCache.Clear();

                factory = s_readerCache.GetOrAdd(elementType, static t => CreateFactory(t));
            }

            var reader = factory(list);
            if (reader is null)
            {
                var ctx = Context.FromMeta(meta, elementType);
                throw new InvalidOperationException(
                    ctx.CreateErrorMessage("TVP 리더 생성 결과가 null입니다.", "[TVP 바인딩 오류]"));
            }

            return reader;
        }

        /// <summary>
        /// IEnumerable 컬렉션을 검증 없이 빠르게 <see cref="IDataReader"/>로 변환합니다. (BulkCopy용)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static IDataReader CreateReaderForBulk<T>(IEnumerable<T> data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (data is DataTable dt) return dt.CreateDataReader();
            if (data is IDataReader dr) return dr;

            return CreateColumnarTvpReader(data);
        }

        #endregion

        #region [5-4. 핵심 로직 - Columnar TVP Reader 생성 (Patched)]

        /// <summary>
        /// 실제 데이터를 컬럼 기반 버퍼로 변환하여 Columnar TVP Reader를 생성합니다.
        /// <para>
        /// - <typeparamref name="T"/>의 프로퍼티 메타데이터를 기반으로 Typed Accessor를 사용합니다.<br/>
        /// - <b>[JSON 자동 직렬화]</b> 복합 객체가 감지되면 자동으로 JSON 문자열로 변환합니다.<br/>
        /// - 일정 주기마다 셀 값을 샘플링하여 대략적인 페이로드 크기를 추정하고,
        ///   <see cref="DbMetrics.TrackTvpUsage(long, string)"/>로 계측합니다.
        /// </para>
        /// </summary>
        /// <typeparam name="T">TVP로 전송할 DTO 타입</typeparam>
        /// <param name="items">실제 데이터를 담고 있는 컬렉션</param>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static IDataReader CreateColumnarTvpReader<T>(IEnumerable<T> items)
        {
            var accessors = TvpAccessorCache.GetTypedAccessors<T>();
            var props = accessors.Properties;
            var columns = new ColumnBuffer[props.Length];
            var adders = BufferAdderCache.GetAdders(props);

            // 초기 용량 추론 (ICollection<T>이면 정확한 Count를 사용)
            int initialCapacity = items is ICollection<T> c ? c.Count : 1024;

            try
            {
                // 1. 컬럼 버퍼 초기화
                for (int i = 0; i < props.Length; i++)
                    columns[i] = ColumnBufferFactory.Create(props[i].PropertyType, initialCapacity);

                var getters = accessors.TypedAccessors;
                var bufferAdder = accessors.BufferAdder;
                
                long totalBytesSampled = 0;
                int rowCount = 0;

                // [최적화] Source Generator가 생성한 고속 Adder가 있으면 사용 (Zero-Boxing)
                if (bufferAdder != null)
                {
                    // object[]로 캐스팅하여 전달 (인터페이스 변환 비용 최소화)
                    // SG 내부에서 ((ITvpColumn<T>)columns[i]).Add() 호출
                    object[] colRefs = columns; 
                    
                    foreach (var item in items)
                    {
                        rowCount++;
                        bufferAdder(item, colRefs);
                        
                        // [메트릭] 고속 경로에서도 샘플링은 필요하지만,
                        // SG 최적화 모드에서는 편의상 첫 번째 컬럼(보통 PK)이나 단순 카운트 기반으로 추정 가능.
                        // 여기서는 정확한 바이트 계산이 어려우므로(Getter를 안 부르니까), 
                        // 평균적인 Row 크기(예: 64바이트)로 간단히 추산하거나, 
                        // 정확도를 위해 별도 로직을 탈 수 있음. 
                        // 현재 구조상 성능을 위해 루프 내 추가 계산은 최소화.
                        if ((rowCount & 127) == 0) totalBytesSampled += 64; 
                    }
                }

                else
                {
                    // [Zero-Copy 최적화] List<T> 또는 T[]인 경우 Span으로 변환하여 Enumerator 할당 제거
                    if (items is List<T> list)
                    {
                        var span = CollectionsMarshal.AsSpan(list);
                        foreach (var item in span)
                        {
                            rowCount++;
                            ProcessItem(item, props, getters, adders, columns, ref totalBytesSampled, rowCount);
                        }
                    }
                    else if (items is T[] array)
                    {
                        foreach (var item in array)
                        {
                            rowCount++;
                            ProcessItem(item, props, getters, adders, columns, ref totalBytesSampled, rowCount);
                        }
                    }
                    else
                    {
                        // 일반 IEnumerable (Enumerator 할당 발생)
                        foreach (var item in items)
                        {
                            rowCount++;
                            ProcessItem(item, props, getters, adders, columns, ref totalBytesSampled, rowCount);
                        }
                    }
                }

                // 3. 메트릭 기록 (행이 1개 이상일 때만)
                if (rowCount > 0 && totalBytesSampled > 0)
                {
                    // 샘플링(1/128)을 다시 전체로 환산
                    long estimatedBytes = totalBytesSampled << 7;

                    // [계측] TVP 페이로드 사용량 기록
                    //  - TrackTvpUsage는 내부에서 DbExecutionContextScope.Current 등을 활용하여
                    //    DbRequestInfo를 구성하고, TagList에 반영한다고 가정합니다.
                    DbMetrics.TrackTvpUsage(estimatedBytes, typeof(T).Name);
                }

                // 4. Columnar TVP Reader 반환
                return new ColumnarTvpReader(columns, rowCount, accessors.OrdinalMap, accessors.SchemaTable);
            }
            catch
            {
                // 예외 발생 시 버퍼 자원 정리
                foreach (var col in columns)
                    col?.Dispose();

                throw;
            }
        }

        /// <summary>
        /// 값이 복합 객체이고 프로퍼티 타입이 string인 경우 JSON으로 자동 직렬화합니다.
        /// <para>
        /// <b>[사용 시나리오]</b> Bulk Insert 시 DTO의 복합 타입 프로퍼티가
        /// DB의 JSON 컬럼(nvarchar, varchar)과 매핑될 때 자동으로 직렬화합니다.
        /// </para>
        /// </summary>
        /// <param name="value">원본 값</param>
        /// <param name="propertyType">프로퍼티의 타입</param>
        /// <returns>필요 시 JSON 직렬화된 값, 아니면 원본 값</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? AutoSerializeIfNeeded(object? value, Type propertyType)
        {
            // null 또는 DBNull은 그대로 반환
            if (value is null or DBNull)
                return value;

            // 프로퍼티 타입이 string이 아니면 변환 불필요
            if (propertyType != typeof(string))
                return value;

            // 값이 이미 문자열이면 변환 불필요
            if (value is string)
                return value;

            // [AOT Safe] Source Generator 기반 직렬화 권장 (여기서는 동적 Reflection 사용 - 개선 필요)
            // v2.0: AotHybridCacheSerializer 사용 고려
            return JsonSerializer.Serialize(value, S_JsonOptions);
        }

        private static readonly JsonSerializerOptions S_JsonOptions = new() { WriteIndented = false };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ProcessItem<T>(
            T item, 
            System.Reflection.PropertyInfo[] props, 
            Func<T, object?>[] getters, 
            Action<ColumnBuffer, object?>[] adders, 
            ColumnBuffer[] columns, 
            ref long totalBytesSampled, 
            int rowCount)
        {
            for (int i = 0; i < props.Length; i++)
            {
                var val = getters[i](item);
                var processedValue = AutoSerializeIfNeeded(val, props[i].PropertyType);
                adders[i](columns[i], processedValue);

                if ((rowCount & 127) == 0)
                    totalBytesSampled += Sizer.Estimate(processedValue);
            }
        }



        #endregion

        #region [5-5. 헬퍼 - 요소 타입 추론 및 팩터리 생성]

        private static Type? TryGetElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];

            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }

            return null;
        }

        private static Func<IEnumerable, IDataReader> CreateFactory(Type type)
        {
            var method = typeof(Tvp)
                .GetMethod(nameof(CreateColumnarTvpReader), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type);

            var p = Expression.Parameter(typeof(IEnumerable), "items");
            var cast = Expression.Convert(p, typeof(IEnumerable<>).MakeGenericType(type));
            var call = Expression.Call(method, cast);

            return Expression.Lambda<Func<IEnumerable, IDataReader>>(call, p).Compile();
        }

        #endregion

        #region [5-6. TVP 스키마 검증]

        private static void ValidateTvpSchema(SpParameterMetadata meta, Type type)
        {
            if (string.IsNullOrEmpty(meta.UdtTypeName) || ValidatorCallback is null)
                return;

            var key = (ClrType: type, TvpName: meta.UdtTypeName);

            if (!s_validationCache.TryGetValue(key, out var state))
            {
                if (s_validationCache.Count >= s_maxCacheSize)
                    s_validationCache.Clear();

                bool ok = false;
                try
                {
                    ok = ValidatorCallback(type, meta.UdtTypeName);
                }
                catch
                {
                    // 콜백 내부 예외는 실패로 간주 (안전 측)
                }

                state = ok ? TvpValidationState.Success : TvpValidationState.Failed;
                s_validationCache[key] = state;
            }

            if (state == TvpValidationState.Failed)
            {
                var ctx = Context.FromMeta(meta, type);
                throw new InvalidOperationException(
                    ctx.CreateErrorMessage(
                        "TVP 타입과 DTO 구조가 일치하지 않습니다. (스키마 검증 실패)",
                        "[TVP 바인딩 오류]"));
            }
        }

        #endregion

        #region [5-7. ColumnBuffer Adder/Factory 캐시]

        /// <summary>
        /// 컬럼 버퍼에 데이터를 주입하는 Adder 델리게이트를 관리합니다.
        /// </summary>
        internal static class BufferAdderCache
        {
            /// <summary>PropertyType(int, string 등) 별 Adder 캐시입니다.</summary>
            private static readonly ConcurrentDictionary<Type, Action<ColumnBuffer, object?>> s_cache = new();

            /// <summary>캐시를 초기화합니다.</summary>
            internal static void Clear() => s_cache.Clear();

            /// <summary>
            /// 지정된 프로퍼티 집합에 대한 ColumnBuffer Adder 배열을 생성합니다.
            /// </summary>
            public static Action<ColumnBuffer, object?>[] GetAdders(PropertyInfo[] props)
            {
                var ret = new Action<ColumnBuffer, object?>[props.Length];

                for (int i = 0; i < props.Length; i++)
                {
                    var type = props[i].PropertyType;

                    ret[i] = s_cache.GetOrAdd(type, static t => CreateAdder(t));

                    // 캐시가 비정상적으로 커진 경우 방어적 초기화
                    if (s_cache.Count >= s_maxCacheSize)
                        s_cache.Clear();
                }

                return ret;
            }

            /// <summary>
            /// 지정된 CLR 타입에 대한 ColumnBuffer Adder를 생성합니다.
            /// </summary>
            private static Action<ColumnBuffer, object?> CreateAdder(Type type)
            {
                var pBuf = Expression.Parameter(typeof(ColumnBuffer), "buf");
                var pVal = Expression.Parameter(typeof(object), "val");

                var typed = typeof(TypedColumnBuffer<>).MakeGenericType(type);
                var add = typed.GetMethod("Add")!;
                var castBuf = Expression.Convert(pBuf, typed);

                // Nullable이 아닌 값 타입에 null이 들어오면 데이터 무결성 위반으로 예외 발생
                if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
                {
                    var nullCheck = Expression.Equal(pVal, Expression.Constant(null));
                    var errorMsg = $"필수 컬럼 '{type.Name}'에 Null 값이 감지되었습니다.";

                    var throwExp = Expression.Throw(
                        Expression.New(
                            typeof(InvalidOperationException).GetConstructor([typeof(string)])!,
                            Expression.Constant(errorMsg)));

                    var callAdd = Expression.Call(castBuf, add, Expression.Unbox(pVal, type));
                    var body = Expression.IfThenElse(nullCheck, throwExp, callAdd);

                    return Expression.Lambda<Action<ColumnBuffer, object?>>(body, pBuf, pVal).Compile();
                }

                // 참조 타입 또는 Nullable 값 타입
                var call = Expression.Call(castBuf, add, Expression.Convert(pVal, type));
                return Expression.Lambda<Action<ColumnBuffer, object?>>(call, pBuf, pVal).Compile();
            }
        }

        /// <summary>
        /// ColumnBuffer 인스턴스를 리플렉션 없이 고속으로 생성하는 팩터리입니다.
        /// </summary>
        internal static class ColumnBufferFactory
        {
            private static readonly ConcurrentDictionary<Type, Func<int, ColumnBuffer>> s_cache = new();

            internal static void Clear() => s_cache.Clear();

            /// <summary>
            /// 지정된 CLR 타입과 초기 용량으로 ColumnBuffer를 생성합니다.
            /// </summary>
            public static ColumnBuffer Create(Type type, int capacity)
            {
                if (!s_cache.TryGetValue(type, out var factory))
                {
                    if (s_cache.Count >= s_maxCacheSize)
                        s_cache.Clear();

                    factory = s_cache.GetOrAdd(type, static t =>
                    {
                        var p = Expression.Parameter(typeof(int), "capacity");
                        var ctor = typeof(TypedColumnBuffer<>).MakeGenericType(t).GetConstructor([typeof(int)])!;
                        var newExpr = Expression.New(ctor, p);
                        return Expression.Lambda<Func<int, ColumnBuffer>>(newExpr, p).Compile();
                    });
                }

                return factory(capacity);
            }
        }

        #endregion
    }

    #endregion

    // =========================================================================
    // 6. 내부 모듈 - Payload Sizer (보강 버전)
    // =========================================================================

    #region [6. 내부 모듈 - Payload Sizer]

    /// <summary>
    /// TVP 전송 시 셀 값의 대략적인 메모리 사용량을 추정하는 헬퍼입니다.
    /// <para>
    /// - 빈번한 타입(string/byte[]/정수/실수/DateTime 등)을 별도 분기<br/>
    /// - 드문 타입은 보수적으로 16바이트로 계산하여 오차와 비용을 균형화합니다.
    /// </para>
    /// </summary>
    internal static class Sizer
    {
        /// <summary>
        /// 단일 값에 대한 대략적인 바이트 수를 반환합니다.
        /// <para>핫 패스이므로 <see cref="MethodImplOptions.AggressiveInlining"/>을 사용합니다.</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Estimate(object? val)
        {
            if (val is null || val == DBNull.Value) return 0;

            return val switch
            {
                // [참조 타입] 가변 길이
                string s => s.Length * sizeof(char),            // UTF-16
                byte[] b => b.Length,
                char[] c => c.Length * sizeof(char),

                // [값 타입] 고정 길이
                int or uint => sizeof(int),
                long or ulong => sizeof(long),
                short or ushort => sizeof(short),

                float => sizeof(float),
                double => sizeof(double),
                decimal => sizeof(decimal),

                DateTime => sizeof(long),        // Ticks(Int64)
                DateTimeOffset => 16,            // DateTime(8) + Offset 등

                Guid => 16,

                bool => sizeof(bool),
                byte or sbyte => sizeof(byte),

                // [Fallback] 알 수 없는 타입은 보수적으로 16바이트 가정
                _ => 16
            };
        }
    }

    #endregion

    // =========================================================================
    // 7. 내부 모듈 - 바인딩 컨텍스트
    // =========================================================================

    #region [7. 내부 모듈 - 바인딩 컨텍스트]

    /// <summary>
    /// 바인딩/TVP 처리 과정의 컨텍스트 정보를 담는 <see langword="ref struct"/>입니다.
    /// <para>
    /// - Zero-Allocation (스택 전용)<br/>
    /// - Span 기반 문자열 참조<br/>
    /// - 실행 컨텍스트(<see cref="DbExecutionContext"/>)를 포함한 상세 오류 메시지 생성
    /// </para>
    /// </summary>
    /// <param name="execution">현재 실행 중인 DB 컨텍스트</param>
    /// <param name="paramName">파라미터 이름</param>
    /// <param name="tvpTypeName">TVP 타입 이름</param>
    /// <param name="clrType">매핑 대상 CLR 타입</param>
    internal readonly ref struct Context(
        DbExecutionContext? execution,
        ReadOnlySpan<char> paramName,
        ReadOnlySpan<char> tvpTypeName,
        Type clrType)
    {
        /// <summary>현재 실행 컨텍스트</summary>
        public DbExecutionContext? Execution { get; } = execution;

        /// <summary>파라미터 이름</summary>
        public ReadOnlySpan<char> ParamName { get; } = paramName;

        /// <summary>TVP 타입 이름</summary>
        public ReadOnlySpan<char> TvpTypeName { get; } = tvpTypeName;

        /// <summary>매핑 대상 CLR 타입</summary>
        public Type ClrType { get; } = clrType;

        /// <summary>
        /// <see cref="SpParameterMetadata"/>와 CLR 타입을 기반으로 컨텍스트를 생성합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Context FromMeta(SpParameterMetadata meta, Type clrType)
            => new(
                DbExecutionContextScope.Current,
                meta.Name.AsSpan(),
                meta.UdtTypeName.AsSpan(),
                clrType);

        /// <summary>
        /// 상세한 실행 컨텍스트를 포함한 오류 메시지를 생성합니다.
        /// </summary>
        /// <param name="reason">오류 원인 설명</param>
        /// <param name="prefix">메시지 헤더 (기본: "[바인딩 오류]")</param>
        public string CreateErrorMessage(string reason, string prefix = "[바인딩 오류]")
        {
            var sb = new StringBuilder(512);

            sb.Append(prefix).Append(' ').AppendLine(reason);
            sb.AppendLine("--------------------------------------------------");

            sb.Append(" - 파라미터 : ").Append(ParamName).AppendLine();

            if (!TvpTypeName.IsEmpty)
                sb.Append(" - TVP 타입 : ").Append(TvpTypeName).AppendLine();

            sb.Append(" - CLR 타입 : ").AppendLine(ClrType.Name);

            if (Execution is { } exec)
            {
                sb.AppendLine(" - 실행 정보 :");
                sb.Append("   * 인스턴스 : ").AppendLine(exec.InstanceName);
                sb.Append("   * 명령 유형 : ").Append(exec.CommandType).AppendLine();

                // SQL 텍스트 요약 (너무 길면 잘라서 표시)
                var cmdText = exec.CommandText.AsSpan();
                const int maxLen = 100;

                sb.Append("   * SQL 요약 : ");
                if (cmdText.Length > maxLen)
                {
                    sb.Append(cmdText[..maxLen]).Append("... (생략됨)");
                }
                else
                {
                    sb.Append(cmdText);
                }
                sb.AppendLine();

                if (!string.IsNullOrEmpty(exec.CorrelationId))
                    sb.Append("   * 추적 ID  : ").AppendLine(exec.CorrelationId);
            }

            return sb.ToString();
        }
    }

    #endregion
}