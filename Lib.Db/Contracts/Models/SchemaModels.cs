// File: Lib.Db/Contracts/Models/SchemaModels.cs
// 작성일: 2025-12-06
// 설명  : DB 스키마 메타데이터 모델 정의
//         (NameHash 기반 고속 비교 및 메모리 효율 최적화 적용)
#nullable enable

using System.Data;

namespace Lib.Db.Contracts.Models;

#region 공통 스키마 베이스

/// <summary>
/// 모든 DB 스키마 모델의 공통 부모 클래스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>일관성</b>: SP, TVP 등 서로 다른 DB 객체의 공통 정보(이름, 버전, 캐시 시각)를 표준화하여 관리합니다.<br/>
/// - <b>버전 관리</b>: <c>VersionToken</c>을 통해 변경 감지 메커니즘을 통일합니다.
/// </para>
/// <para>
/// 저장 프로시저(SP), TVP 등 DB 객체의 "이름 / 버전 / 캐시 상태"를
/// 일관된 방식으로 관리하기 위한 최소 공통 정보를 제공합니다.
/// </para>
/// </summary>
public abstract record SchemaBase
{
    /// <summary>
    /// DB 객체의 전체 이름입니다.
    /// <para>예: <c>dbo.usp_GetUser</c>, <c>dbo.MyTvpType</c></para>
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// DB 객체의 버전을 나타내는 토큰 값입니다.
    /// <para>
    /// 일반적으로 <c>sys.objects.modify_date</c> 기반의
    /// <see cref="DateTime.Ticks"/> 값으로 생성됩니다.
    /// </para>
    /// <para>
    /// - DB 변경 여부를 O(1)로 판단하기 위한 경량 버전 지표<br/>
    /// - 스키마 캐시 무효화 판단의 핵심 키로 사용됩니다.
    /// </para>
    /// </summary>
    public required long VersionToken { get; init; }

    /// <summary>
    /// 로컬 메모리(캐시)에서 마지막으로 이 스키마의 버전을 확인한 시각입니다.
    /// <para>
    /// DB 재조회 주기 제어(SWR, TTL, Backoff 등)나
    /// 진단/로그 목적의 메타데이터로 활용될 수 있습니다.
    /// </para>
    /// </summary>
    public DateTime LastCheckedAt { get; set; }
}

#endregion

#region 저장 프로시저 스키마

/// <summary>
/// 저장 프로시저(Stored Procedure)의 스키마 정보입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>파라미터 모델링</b>: SP 호출 시 필수적으로 알아야 할 파라미터 상세 정보를 포함합니다.<br/>
/// - <b>불변성</b>: 캐시에서 안전하게 공유할 수 있도록 <c>record</c>를 사용합니다.
/// </para>
/// </summary>
public sealed record SpSchema : SchemaBase
{
    /// <summary>
    /// 저장 프로시저의 파라미터 메타데이터 목록입니다.
    /// <para>
    /// 입력/출력 방향, 타입, 크기, 정밀도, NULL 허용 여부 등을 포함하며,
    /// 호출 전 파라미터 바인딩 검증 및 실행 경로 최적화에 사용됩니다.
    /// </para>
    /// </summary>
    public required SpParameterMetadata[] Parameters { get; init; }
}

/// <summary>
/// 저장 프로시저 파라미터 메타데이터입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>메모리 효율</b>: 대량의 파라미터 정보를 힙 할당 없이 처리하기 위해 <c>readonly record struct</c>를 사용합니다.<br/>
/// - <b>데이터 집약</b>: 비즈니스 로직 없이 데이터만 담고 있는 순수 DTO입니다.
/// </para>
/// </summary>
/// <param name="Name">파라미터 이름 (@UserId 등)</param>
/// <param name="UdtTypeName">사용자 정의 타입(UDT/TVP) 이름 (해당 시)</param>
/// <param name="Size">파라미터 크기 (문자열/바이너리 등)</param>
/// <param name="SqlDbType">SQL Server 데이터 타입</param>
/// <param name="Direction">입력/출력 방향</param>
/// <param name="Precision">숫자형 정밀도</param>
/// <param name="Scale">숫자형 스케일</param>
/// <param name="IsNullable">NULL 허용 여부</param>
/// <param name="HasDefaultValue">기본값 존재 여부</param>
public readonly record struct SpParameterMetadata(
    string Name,
    string? UdtTypeName,
    long Size,
    SqlDbType SqlDbType,
    ParameterDirection Direction,
    byte Precision,
    byte Scale,
    bool IsNullable,
    bool HasDefaultValue
);

#endregion

#region TVP 스키마

/// <summary>
/// 테이블 반환 매개변수(TVP)의 스키마 정보입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>구조 검증</b>: 애플리케이션 DTO와 실제 DB TVP 타입 간의 불일치를 감지하는 기준이 됩니다.<br/>
/// - <b>컬럼 메타데이터</b>: TVP를 구성하는 각 컬럼의 상세 정보를 포함합니다.
/// </para>
/// </summary>
public sealed record TvpSchema : SchemaBase
{
    /// <summary>
    /// TVP를 구성하는 컬럼 메타데이터 목록입니다.
    /// <para>
    /// 컬럼 순서, 타입, NULL 허용 여부, 길이/정밀도 정보 등을 포함합니다.
    /// </para>
    /// </summary>
    public required TvpColumnMetadata[] Columns { get; init; }
}

/// <summary>
/// TVP 내부 컬럼 메타데이터입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>고속 비교(Fast Compare)</b>: <see cref="NameHash"/> 필드를 미리 계산하여 저장함으로써, 대량의 컬럼 비교 시 문자열 비교 비용을 제거합니다.<br/>
/// - <b>메모리 최적화</b>: 수십~수백 개의 컬럼 정보를 힙 할당 없이 스택 기반으로 처리하기 위해 <c>readonly record struct</c>를 사용합니다.
/// </para>
/// </summary>
/// <param name="Name">컬럼 이름</param>
/// <param name="NameHash">컬럼 이름의 사전 계산된 해시 값</param>
/// <param name="MaxLength">최대 길이 (문자열/바이너리 계열)</param>
/// <param name="Ordinal">컬럼 순서 (0-based)</param>
/// <param name="SqlDbType">SQL Server 데이터 타입</param>
/// <param name="Precision">숫자형 정밀도</param>
/// <param name="Scale">숫자형 스케일</param>
/// <param name="IsIdentity">IDENTITY 컬럼 여부</param>
/// <param name="IsComputed">계산 컬럼 여부</param>
/// <param name="IsNullable">NULL 허용 여부</param>
public readonly record struct TvpColumnMetadata(
    string Name,            // 참조형: 컬럼 이름
    int NameHash,           // 해시 값: 고속 컬럼 비교용
    long MaxLength,         // 최대 길이
    int Ordinal,            // 컬럼 순서
    SqlDbType SqlDbType,    // SQL 데이터 타입
    byte Precision,         // 정밀도
    byte Scale,             // 스케일
    bool IsIdentity,        // IDENTITY 여부
    bool IsComputed,        // 계산 컬럼 여부
    bool IsNullable         // NULL 허용 여부
);

#endregion
