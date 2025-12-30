// ============================================================================
// File: Lib.Db.TvpGen/TypeMappingRegistry.cs
// Role: TvpAccessorGenerator와 ResultAccessorGenerator가 공유하는
//       C# ↔ SQL Server 타입 매핑 단일 진실 원천 (Single Source of Truth)
//
// ───────────────────────────────────────────────────────────────────────────
// ✅ 목적: 타입 매핑 로직 중복 제거 및 일관성 보장
// ---------------------------------------------------------------------------
// 1) FullyQualified 기반 타입 판별 (type.Name 충돌 방지)
// 2) DateTime2 옵션 지원 (SQL Server 2008+ 권장)
// 3) Nullable/Enum unwrap 통합
// 4) DbDataReader API 선택 최적화
// ============================================================================

#nullable enable

using Microsoft.CodeAnalysis;
using System.Collections.Frozen;

namespace Lib.Db.TvpGen;

#region [타입 매핑 레지스트리] C# ↔ SQL Server 중앙 관리

/// <summary>
/// C# 타입과 SQL Server 타입 간 매핑을 중앙 관리하는 레지스트리입니다.
/// <para>
/// ✅ 목적: TvpAccessorGenerator와 ResultAccessorGenerator의 타입 매핑 일관성 보장
/// </para>
/// <para>
/// ✅ 특징:
/// <list type="bullet">
/// <item><description><b>FullyQualified 기반</b>: type.Name 대신 ToDisplayString(FullyQualifiedFormat) 사용</description></item>
/// <item><description><b>DateTime2 옵션</b>: SQL Server 2008+ 권장 타입 선택 가능</description></item>
/// <item><description><b>Nullable/Enum unwrap</b>: 단일 헬퍼로 통합</description></item>
/// </list>
/// </para>
/// </summary>
internal static class TypeMappingRegistry
{
    // [아이디어 2 적용] 성능과 관리 편의를 위한 사전 정의
    private static readonly FrozenDictionary<string, string> _primitiveReaderMap = new Dictionary<string, string>
    {
        ["global::System.Boolean"] = "GetBoolean",
        ["global::System.Byte"] = "GetByte",
        ["global::System.Int16"] = "GetInt16",
        ["global::System.Int32"] = "GetInt32",
        ["global::System.Int64"] = "GetInt64",
        ["global::System.Single"] = "GetFloat",
        ["global::System.Double"] = "GetDouble",
        ["global::System.Decimal"] = "GetDecimal",
        ["global::System.String"] = "GetString",
        ["global::System.DateTime"] = "GetDateTime",
        ["global::System.Guid"] = "GetGuid"
    }.ToFrozenDictionary();

    #region [Unwrap Helpers] Nullable<T> / Enum underlying 타입 추출

    /// <summary>
    /// Nullable&lt;T&gt;라면 T를, 아니면 원래 타입을 반환합니다.
    /// <para>
    /// 예: Nullable&lt;int&gt; → int, int → int
    /// </para>
    /// </summary>
    public static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nt &&
            nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            nt.TypeArguments.Length == 1)
        {
            return nt.TypeArguments[0];
        }
        return type;
    }

    /// <summary>
    /// enum이면 underlying 타입을, 아니면 원래 타입을 반환합니다.
    /// <para>
    /// 예: Status (short 기반 enum) → short, int → int
    /// </para>
    /// </summary>
    public static ITypeSymbol UnwrapEnumUnderlying(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nt &&
            nt.TypeKind == TypeKind.Enum &&
            nt.EnumUnderlyingType is not null)
        {
            return nt.EnumUnderlyingType;
        }
        return type;
    }

    #endregion

    #region [C# → SQL Server 타입 매핑] FullyQualified 기반

    /// <summary>
    /// C# 타입을 SQL Server SqlDbType 이름으로 매핑합니다.
    /// <para>
    /// ⚠️ 매핑 결과 변경 시 TVP 스키마 검증(StaticValidator)에 영향을 줄 수 있습니다.
    /// </para>
    /// </summary>
    /// <param name="type">C# 타입 심볼</param>
    /// <param name="useDatetime2">
    /// DateTime 타입을 DateTime2로 매핑할지 여부
    /// <para>기본값: false (호환성 유지)</para>
    /// <para>SQL Server 2008+: DateTime2 권장 (더 넓은 범위, 높은 정밀도)</para>
    /// </param>
    /// <returns>SQL Server SqlDbType 이름 (예: "Int", "NVarChar", "Date")</returns>
    public static string GetSqlDbTypeName(ITypeSymbol type, bool useDatetime2 = false)
    {
        type = UnwrapNullable(type);
        type = UnwrapEnumUnderlying(type);

        // byte[] → VarBinary
        if (type is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte)
            return "VarBinary";

        // SpecialType 기반 매핑 (기본형)
        if (TryGetSpecialTypeSqlName(type.SpecialType, useDatetime2, out var sqlType))
            return sqlType;

        // FullyQualified 기반 매핑 (안전성 향상)
        // ✅ CRITICAL: type.Name 대신 FullyQualifiedFormat 사용하여 충돌 방지
        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fullName switch
        {
            "global::System.Guid" => "UniqueIdentifier",
            "global::System.DateTimeOffset" => "DateTimeOffset",
            "global::System.TimeSpan" => "Time",
            "global::System.DateOnly" => "Date",
            "global::System.TimeOnly" => "Time",
            "global::System.Half" => "Real",
            // ⚠️ Int128/UInt128: SQL Server 네이티브 미지원 → Variant (TVP004 에러 발생)
            // 향후 SQL Server에서 지원 시 Decimal(38,0) / Decimal(39,0)로 매핑 가능
            "global::System.Int128" => "Variant",
            "global::System.UInt128" => "Variant",
            _ => "Variant"  // 지원되지 않는 타입 (TVP004 에러 발생)
        };
    }

    private static bool TryGetSpecialTypeSqlName(SpecialType specialType, bool useDatetime2, out string sqlName)
    {
        sqlName = specialType switch
        {
            SpecialType.System_Boolean => "Bit",
            SpecialType.System_Byte => "TinyInt",
            SpecialType.System_Int16 => "SmallInt",
            SpecialType.System_Int32 => "Int",
            SpecialType.System_Int64 => "BigInt",
            SpecialType.System_Single => "Real",
            SpecialType.System_Double => "Float",
            SpecialType.System_Decimal => "Decimal",
            SpecialType.System_String => "NVarChar",
            SpecialType.System_DateTime => useDatetime2 ? "DateTime2" : "DateTime",  // ✅ DateTime2 옵션
            _ => null!
        };

        return sqlName is not null;
    }

    #endregion

    #region [DbDataReader API 선택] C# 타입 → reader.GetXxx() 메서드

    /// <summary>
    /// DbDataReader에서 값을 읽는 표현식을 생성합니다.
    /// <para>
    /// ✅ 원칙:
    /// <list type="bullet">
    /// <item><description>기본형: GetInt32/GetString 등 전용 API 사용 (성능 최적화)</description></item>
    /// <item><description>Enum: underlying 타입 읽고 캐스팅 (예: (Status)r.GetInt16(ord))</description></item>
    /// <item><description>기타: GetFieldValue&lt;T&gt; 사용 (T는 FullyQualified)</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="typeSymbol">C# 타입 심볼</param>
    /// <param name="ordVar">ordinal 변수명 (예: "ord_Id")</param>
    /// <returns>reader 표현식 (예: "r.GetInt32(ord_Id)", "(Status)r.GetInt16(ord_Status)")</returns>
    public static string GetReaderValueExpression(ITypeSymbol typeSymbol, string ordVar)
    {
        // Nullable 처리 로직 (기존 유지)
        bool isNullable = typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        ITypeSymbol effective = isNullable ? ((INamedTypeSymbol)typeSymbol).TypeArguments[0] : typeSymbol;

        // Enum 처리 로직 (기존 유지)
        if (effective.TypeKind == TypeKind.Enum && effective is INamedTypeSymbol e && e.EnumUnderlyingType is not null)
        {
            var enumFull = effective.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var prim = GetPrimitiveReadExpr(e.EnumUnderlyingType, ordVar, e.EnumUnderlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            return $"({enumFull})({prim})";
        }

        return GetPrimitiveReadExpr(effective, ordVar, typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static string GetPrimitiveReadExpr(ITypeSymbol t, string ordVar, string fallbackTypeFull)
    {
        // 1. SpecialType 기반 시도 (가장 빠른 경로)
        if (TryGetSpecialTypeReaderExpr(t.SpecialType, ordVar, out var expr))
            return expr;

        // 2. FullyQualified 기반 Fallback (테스트 환경 및 SpecialType 인식 실패 대응)
        var full = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // 사전에 정의된 프리미티브 매핑 확인
        if (_primitiveReaderMap.TryGetValue(full, out var methodName))
            return $"r.{methodName}({ordVar})";

        // 3. 확장 타입 매핑 (기존 유지 및 개선)
        return full switch
        {
            "global::System.DateTimeOffset" => $"r.GetFieldValue<global::System.DateTimeOffset>({ordVar})",
            "global::System.TimeSpan" => $"r.GetFieldValue<global::System.TimeSpan>({ordVar})",
            "global::System.DateOnly" => $"r.GetFieldValue<global::System.DateOnly>({ordVar})",
            "global::System.TimeOnly" => $"r.GetFieldValue<global::System.TimeOnly>({ordVar})",
            "global::System.Half" => $"r.GetFieldValue<global::System.Half>({ordVar})",

            // [아이디어 3 적용] 추적성 향상을 위한 Fallback 생성
            _ => $"r.GetFieldValue<{fallbackTypeFull}>({ordVar}) /* Trace: MapByFallback */"
        };
    }

    private static bool TryGetSpecialTypeReaderExpr(SpecialType specialType, string ordVar, out string expr)
    {
        expr = specialType switch
        {
            SpecialType.System_Boolean => $"r.GetBoolean({ordVar})",
            SpecialType.System_Byte => $"r.GetByte({ordVar})",
            SpecialType.System_Int16 => $"r.GetInt16({ordVar})",
            SpecialType.System_Int32 => $"r.GetInt32({ordVar})",
            SpecialType.System_Int64 => $"r.GetInt64({ordVar})",
            SpecialType.System_Single => $"r.GetFloat({ordVar})",
            SpecialType.System_Double => $"r.GetDouble({ordVar})",
            SpecialType.System_Decimal => $"r.GetDecimal({ordVar})",
            SpecialType.System_String => $"r.GetString({ordVar})",
            SpecialType.System_DateTime => $"r.GetDateTime({ordVar})",
            _ => null!
        };

        return expr is not null;
    }

    #endregion
}

#endregion
