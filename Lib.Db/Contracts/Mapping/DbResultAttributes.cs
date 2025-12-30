// ============================================================================
// 파일명: Lib.Db/Contracts/Mapping/DbResultAttributes.cs
// 역할  : 결과 매핑 및 파라미터 바인딩 제어용 Attribute / 마커 인터페이스 정의
// 설명  :
//   - Source Generator 기반 결과 매핑 식별
//   - Native AOT 친화적 고성능 매핑 지원
//   - Raw SQL 파라미터 타입/크기/정밀도 명시 제어
// ============================================================================

#nullable enable

using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Contracts.Mapping;

#region 결과 매핑용 계약 (Source Generator)

/// <summary>
/// 이 특성이 부여된 타입에 대해 Source Generator가
/// <see cref="SqlDataReader"/> → DTO 매핑 코드를 자동 생성합니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>Native AOT 지원</b>: 런타임 리플렉션 없이 컴파일 타임에 생성된 코드로 동작하여 AOT 호환성을 보장합니다.<br/>
/// - <b>고성능 매핑</b>: 런타임 비용(Boxing/Unboxing, Reflection)을 제거하여 Zero-Allocation에 가까운 성능을 제공합니다.
/// </para>
/// <para>
/// <b>주의:</b> Source Generator가 코드를 확장해야 하므로
/// 대상 타입은 반드시 <c>partial</c>로 선언되어야 합니다.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class DbResultAttribute : Attribute
{
}

/// <summary>
/// Source Generator가 생성한 결과 매퍼가 구현해야 하는 마커 인터페이스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>정적 다형성</b>: <c>static abstract</c> 메서드를 통해 인터페이스 수준에서 정적 팩터리 패턴을 강제합니다.<br/>
/// - <b>매퍼 발견</b>: 팩터리가 이 인터페이스 구현 여부를 통해 최적화된 매퍼(Source Generated) 존재를 감지합니다.
/// </para>
/// </summary>
/// <typeparam name="T">매핑될 결과 DTO 타입</typeparam>
public interface IMapableResult<T>
{
    /// <summary>
    /// <see cref="SqlDataReader"/>로부터 현재 행(Row)을 읽어
    /// <typeparamref name="T"/> 인스턴스를 생성합니다.
    /// <para>
    /// 이 메서드는 개발자가 직접 구현하지 않으며,
    /// Source Generator에 의해 자동으로 생성됩니다.
    /// </para>
    /// </summary>
    /// <param name="reader">SQL 결과를 제공하는 <see cref="SqlDataReader"/> 인스턴스</param>
    /// <returns>매핑된 <typeparamref name="T"/> 인스턴스</returns>
    static abstract T Map(SqlDataReader reader);
}

#endregion

#region 파라미터 바인딩 제어용 계약 (Raw SQL / LOB)

/// <summary>
/// DTO 프로퍼티가 SQL 파라미터로 바인딩될 때의
/// 데이터 타입, 크기, 정밀도 등의 세부 동작을 제어하는 특성입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>명시적 제어</b>: 자동 타입 추론에 의존하지 않고, DB 스키마에 정확히 맞는 파라미터 타입을 지정합니다.<br/>
/// - <b>성능 최적화</b>: 특히 문자열/바이너리(LOB)의 올바른 크기 지정(Size=-1 등)으로 불필요한 재할당을 방지합니다.
/// </para>
/// <para>
/// 주로 다음 상황에서 사용됩니다.
/// <list type="bullet">
/// <item>Raw SQL 실행 시 자동 타입 추론을 피하고 싶을 때</item>
/// <item>대용량 문자열/바이너리(LOB) 처리 시 명시적 설정이 필요할 때</item>
/// <item>Decimal 정밀도/스케일을 엄격히 제어해야 할 때</item>
/// </list>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class DbParameterAttribute : Attribute
{
    /// <summary>
    /// SQL 데이터 타입입니다.
    /// <para>
    /// 기본값은 <see cref="SqlDbType.Variant"/>로,
    /// 매퍼가 자동으로 타입을 추론하도록 합니다.
    /// </para>
    /// </summary>
    public SqlDbType DbType { get; set; } = SqlDbType.Variant;

    /// <summary>
    /// 파라미터 크기입니다. (문자열 길이 또는 바이트 수)
    /// <para>
    /// <c>-1</c>로 설정하면 <c>varchar(max)</c>, <c>nvarchar(max)</c>,
    /// <c>varbinary(max)</c>와 같은 MAX 타입으로 처리됩니다.
    /// </para>
    /// </summary>
    public int Size { get; set; }

    /// <summary>
    /// 소수점 전체 자릿수(Precision)입니다.
    /// <para>주로 <c>decimal</c> 계열 타입에 사용됩니다.</para>
    /// </summary>
    public byte Precision { get; set; }

    /// <summary>
    /// 소수점 이하 자릿수(Scale)입니다.
    /// <para>주로 <c>decimal</c> 계열 타입에 사용됩니다.</para>
    /// </summary>
    public byte Scale { get; set; }

    /// <summary>
    /// 이 특성이 실제로 설정되었는지 여부를 나타냅니다.
    /// <para>
    /// 모든 값이 기본값이면(<c>Variant / 0 / 0 / 0</c>)
    /// 매퍼는 이 특성을 무시하고 자동 추론 경로를 사용할 수 있습니다.
    /// </para>
    /// </summary>
    internal bool IsConfigured =>
        DbType != SqlDbType.Variant ||
        Size != 0 ||
        Precision != 0 ||
        Scale != 0;
}

#endregion
