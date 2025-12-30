// ============================================================================
// File : Lib.Db/Contracts/Mapping/MappingContracts.cs
// 역할 : SQL 매핑(파라미터/결과) 계약 인터페이스 정의
// ============================================================================

#nullable enable

using System.Data.Common;
using Lib.Db.Contracts.Models; // SpSchema 참조
using Microsoft.Data.SqlClient;

namespace Lib.Db.Contracts.Mapping;

#region SQL 매퍼 계약

/// <summary>
/// SQL 실행 단위(<see cref="SqlCommand"/>)에 대해
/// 입력 파라미터 바인딩 및 결과/출력 파라미터 역매핑을 수행하는 공통 매퍼 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>양방향 매핑 통합</b>: 입력 바인딩(C# → DB)과 결과 매핑(DB → C#)을 하나의 인터페이스로 묶어 응집도를 높입니다.<br/>
/// - <b>SP 메타데이터 지원</b>: 단순 이름 매칭을 넘어, DB 스키마 정보(<see cref="SpSchema"/>)를 활용한 정밀한 파라미터 제어를 지원합니다.
/// </para>
/// <para>
/// 구현체는 DTO / Dictionary / DataRow 등 다양한 입력/출력 모델을 지원할 수 있으며,
/// <see cref="SpSchema"/>가 제공될 경우 "SP 메타데이터 기반 정책"을 적용할 수 있습니다.
/// </para>
/// </summary>
/// <typeparam name="T">매핑 대상 타입(입력 DTO 또는 결과 DTO 등)</typeparam>
internal interface ISqlMapper<T>
{
    #region 입력 매핑

    /// <summary>
    /// 입력 파라미터를 <see cref="SqlCommand"/>에 바인딩(매핑)합니다.
    /// <para>
    /// <paramref name="schema"/>가 제공되면(= SP 호출로 간주),
    /// 저장 프로시저 메타데이터(<see cref="SpSchema"/>)를 기반으로 다음 정책을 적용합니다.
    /// </para>
    /// <para>
    /// <b>[파라미터 바인딩 정책]</b>
    /// <list type="bullet">
    /// <item>
    /// DTO/Dictionary/DataRow에 멤버/키/컬럼이 <b>없으면</b> →
    /// DB의 DEFAULT를 사용하도록 파라미터를 <b>생략</b>합니다.
    /// </item>
    /// <item>
    /// 멤버/키/컬럼은 존재하지만 값이 <c>null</c>이면 →
    /// DB DEFAULT가 아니라 <b>명시적 NULL</b>(DBNull)로 바인딩합니다.
    /// </item>
    /// <item>
    /// 조건: (NOT NULL) + (DEFAULT 없음) + (입력 파라미터)인데,
    /// DTO/Dictionary/DataRow에 멤버/키/컬럼이 <b>없으면</b> →
    /// <see cref="LibDbOptions.StrictRequiredParameterCheck"/>가 <c>true</c>인 경우
    /// 예외를 발생시켜 조기 실패(Fail-Fast)합니다.
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>[주의]</b> <paramref name="schema"/>가 <c>null</c>인 경우는
    /// Raw SQL 실행(또는 메타데이터 없이 실행)로 간주할 수 있으며,
    /// 이때는 구현체가 보유한 자체 규칙으로 파라미터를 바인딩합니다.
    /// </para>
    /// </summary>
    /// <param name="cmd">매핑 대상 <see cref="SqlCommand"/> 인스턴스</param>
    /// <param name="parameters">파라미터 값이 담긴 <typeparamref name="T"/> 인스턴스</param>
    /// <param name="schema">SP 메타데이터 스키마(없으면 Raw SQL로 간주 가능)</param>
    void MapParameters(SqlCommand cmd, T parameters, SpSchema? schema);

    #endregion

    #region 출력 매핑

    /// <summary>
    /// Output / InputOutput 파라미터 값을 <typeparamref name="T"/> 인스턴스에 역매핑합니다.
    /// <para>
    /// SP 실행이 완료된 뒤 <see cref="SqlCommand.Parameters"/> 컬렉션에서 값을 꺼내
    /// 대상 객체에 반영합니다.
    /// </para>
    /// </summary>
    /// <param name="cmd">실행이 완료된 <see cref="SqlCommand"/> 인스턴스</param>
    /// <param name="parameters">값을 되돌려 받을 대상 <typeparamref name="T"/> 인스턴스</param>
    void MapOutputParameters(SqlCommand cmd, T parameters);

    #endregion

    #region 결과 매핑

    /// <summary>
    /// <see cref="DbDataReader"/>가 가리키는 현재 행(Row)을
    /// <typeparamref name="T"/>로 역직렬화(매핑)합니다.
    /// <para>
    /// 구현체는 컬럼 Ordinal 캐싱, NameHash 매칭, Expression Tree/소스 제너레이터 등
    /// 성능 최적화 전략을 적용할 수 있습니다.
    /// </para>
    /// </summary>
    /// <param name="reader">현재 행을 가리키는 <see cref="DbDataReader"/> 인스턴스</param>
    /// <returns>매핑된 <typeparamref name="T"/> 인스턴스</returns>
    T MapResult(DbDataReader reader);

    #endregion
}

#endregion

#region 매퍼 팩터리 계약

/// <summary>
/// 타입별 매퍼(<see cref="ISqlMapper{T}"/>) 인스턴스를 제공하는 팩터리 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>캐싱 및 재사용</b>: 매퍼 생성 비용을 줄이기 위해 한 번 생성된 매퍼를 캐싱하고 재사용합니다.<br/>
/// - <b>매퍼 전략 캡슐화</b>: SourceGen, Expression, Reflection 등 어떤 매퍼 구현체를 사용할지 결정하는 로직을 팩터리 내부로 숨깁니다.
/// </para>
/// </summary>
internal interface IMapperFactory
{
    /// <summary>
    /// 지정된 <typeparamref name="T"/> 타입을 처리할 수 있는
    /// <see cref="ISqlMapper{T}"/> 인스턴스를 반환합니다.
    /// </summary>
    /// <typeparam name="T">매핑 대상 타입</typeparam>
    /// <returns><typeparamref name="T"/> 타입을 처리할 수 있는 매퍼 인스턴스</returns>
    ISqlMapper<T> GetMapper<T>();
}

#endregion
