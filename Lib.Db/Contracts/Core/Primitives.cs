// ============================================================================
// 파일: Lib.Db.Contracts/Core/Primitives.cs
// 설명: 기본 프리미티브/트레이트/옵션(TVP 검증 모드 포함) 정의
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;


namespace Lib.Db.Contracts.Core;

#region 트레이트 정의

/// <summary>
/// DB 객체 종류별 특성(기본 스키마, 표시 이름 등)을 정의하는 정적 인터페이스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>유연한 확장성</b>: 새로운 DB 객체 타입(예: View, Sequence)이 추가되더라도 인터페이스 구현만으로 확장 가능합니다.<br/>
/// - <b>타입 안전성</b>: <see cref="DbObjectName{TTrait}"/>와 결합하여 컴파일 시점에 객체 타입을 구분합니다.<br/>
/// - <b>C# 11+ 활용</b>: Static Abstract Members를 사용하여 제네릭 타입 내에서 정적 메타데이터에 접근합니다.
/// </para>
/// </summary>
public interface IDbObjectTrait
{
    /// <summary>사용자에게 표시할 한글 객체 타입명입니다. (예: "저장 프로시저", "TVP")</summary>
    static abstract string DisplayName { get; }

    /// <summary>스키마가 생략되었을 때 사용할 기본 스키마입니다. (예: "dbo")</summary>
    static abstract string DefaultSchema { get; }
}

/// <summary>저장 프로시저(SP) 특성 정의</summary>
public readonly struct SpTrait : IDbObjectTrait
{
    /// <inheritdoc />
    public static string DisplayName => "저장 프로시저(SP)";

    /// <inheritdoc />
    public static string DefaultSchema => "dbo";
}

/// <summary>테이블 값 매개변수(TVP) 특성 정의</summary>
public readonly struct TvpTrait : IDbObjectTrait
{
    /// <inheritdoc />
    public static string DisplayName => "테이블 값 매개변수(TVP)";

    /// <inheritdoc />
    public static string DefaultSchema => "dbo";
}

#endregion

#region 제네릭 DB 객체 식별자 정의

/// <summary>
/// 스키마와 객체 이름을 포함하는 불변(Immutable) 식별자입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>팬텀 타입 활용</b>: <typeparamref name="TTrait"/>를 사용하여 SP 이름과 TVP 이름을 서로 다른 타입으로 취급, 혼용 실수를 방지합니다.<br/>
/// - <b>고성능 파싱</b>: <see cref="ISpanParsable{TSelf}"/> 구현으로 문자열 할당 없는(Zero-Allocation) 파싱을 지원합니다.
/// </para>
/// </summary>
/// <typeparam name="TTrait">객체의 특성을 정의하는 마커 타입입니다.</typeparam>
/// <param name="Schema">데이터베이스 스키마 이름 (예: dbo)</param>
/// <param name="Name">데이터베이스 객체 이름 (예: usp_GetList)</param>
public readonly record struct DbObjectName<TTrait>(string Schema, string Name)
    : IParsable<DbObjectName<TTrait>>, ISpanParsable<DbObjectName<TTrait>>, IFormattable
    where TTrait : struct, IDbObjectTrait
{
    #region 생성자 및 검증 로직

    /// <summary>
    /// 스키마와 이름을 초기화하고 유효성을 검증합니다.
    /// </summary>
    public string Schema { get; init; } = string.IsNullOrWhiteSpace(Schema)
        ? throw new ArgumentException($"{TTrait.DisplayName}의 스키마 이름이 비어 있습니다.", nameof(Schema))
        : Schema;

    /// <summary>
    /// 객체 이름을 초기화하고 유효성을 검증합니다.
    /// </summary>
    public string Name { get; init; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException($"{TTrait.DisplayName}의 이름이 비어 있습니다.", nameof(Name))
        : Name;

    /// <summary>
    /// "Schema.Name" 형식의 전체 이름입니다.
    /// </summary>
    public string FullName => $"{Schema}.{Name}";

    #endregion

    #region 파싱 로직

    /// <summary>
    /// 문자열을 파싱하여 객체를 생성합니다. 실패 시 예외가 발생합니다.
    /// <para>지원 형식: "Schema.Name" 또는 "Name"</para>
    /// </summary>
    /// <exception cref="ArgumentException">파싱 실패 시 한글 메시지 예외 발생</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DbObjectName<TTrait> Parse(string s, IFormatProvider? provider = null)
    {
        if (string.IsNullOrWhiteSpace(s))
            ThrowHelper_ArgumentNullOrEmpty(nameof(s));

        return Parse(s.AsSpan(), provider);
    }

    /// <summary>
    /// Span을 파싱하여 객체를 생성합니다. (Zero-Allocation 지향)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DbObjectName<TTrait> Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null)
    {
        if (!TryParse(s, provider, out DbObjectName<TTrait> result))
        {
            ThrowHelper_InvalidFormat(s.ToString());
        }
        return result;
    }

    /// <summary>
    /// 문자열 파싱을 시도합니다. 예외를 발생시키지 않습니다.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out DbObjectName<TTrait> result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = default;
            return false;
        }
        return TryParse(s.AsSpan(), provider, out result);
    }

    /// <summary>
    /// Span 파싱을 시도합니다. (핵심 파싱 로직)
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DbObjectName<TTrait> result)
    {
        // 1. 공백 제거
        ReadOnlySpan<char> span = s.Trim();
        if (span.IsEmpty)
        {
            result = default;
            return false;
        }

        // 2. 점(.) 위치 탐색
        int dotIndex = span.IndexOf('.');

        string schema;
        string name;

        if (dotIndex < 0)
        {
            // 점이 없으면 기본 스키마 사용
            schema = TTrait.DefaultSchema;
            name = span.ToString();
        }
        else
        {
            // 점이 있으면 분리
            schema = span[..dotIndex].ToString();
            name = span[(dotIndex + 1)..].ToString();
        }

        // 3. 구성 요소 유효성 재확인 (빈 문자열 체크)
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name))
        {
            result = default;
            return false;
        }

        // 4. 생성자 호출 없이 직접 할당 (생성자 중복 검증 회피 및 struct 성능 최적화)
        // 주의: record struct의 기본 생성자는 public이어야 하므로, 
        // 검증된 값으로 안전하게 인스턴스를 만듭니다.
        result = new DbObjectName<TTrait>(schema, name);
        return true;
    }

    #endregion

    #region 형변환 및 포맷팅

    /// <inheritdoc />
    public override string ToString() => FullName;

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => FullName;

    /// <summary>
    /// DB 객체 식별자를 전체 이름 문자열로 변환합니다.
    /// </summary>
    public static implicit operator string(DbObjectName<TTrait> id) => id.FullName;

    /// <summary>
    /// 문자열을 DB 객체 식별자로 변환합니다.
    /// </summary>
    public static implicit operator DbObjectName<TTrait>(string value) => Parse(value);

    #endregion

    #region 예외 헬퍼 (Cold Path)

    [DoesNotReturn]
    private static void ThrowHelper_ArgumentNullOrEmpty(string paramName)
    {
        throw new ArgumentException($"{TTrait.DisplayName} 이름이 비어 있습니다. (입력값: null 또는 공백)", paramName);
    }

    [DoesNotReturn]
    private static void ThrowHelper_InvalidFormat(string input)
    {
        throw new ArgumentException(
            $"{TTrait.DisplayName} 형식이 올바르지 않습니다. " +
            $"입력값: \"{input}\", 기대 형식: \"{TTrait.DefaultSchema}.Name\" 또는 \"Name\"");
    }

    #endregion
}

#endregion

#region DB 인스턴스 식별자 정의

/// <summary>
/// DB 인스턴스를 식별하는 논리적 ID입니다.
/// </summary>
/// <param name="Value">원본 식별 문자열</param>
public readonly record struct DbInstanceId(string Value)
{
    /// <summary>원본 값을 보관합니다. null이거나 공백일 수 없습니다.</summary>
    public string Value { get; init; } = string.IsNullOrWhiteSpace(Value)
        ? throw new ArgumentNullException(nameof(Value), "DB 인스턴스 ID는 비어 있을 수 없습니다.")
        : Value;

    /// <summary>레거시 진단/redaction 대상인 "Raw:" 접두사 값인지 확인합니다.</summary>
    public bool IsRawConnectionString => Value.StartsWith("Raw:", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>
    /// 인스턴스 식별자를 원본 문자열 값으로 변환합니다.
    /// </summary>
    public static implicit operator string(DbInstanceId id) => id.Value;

    /// <summary>
    /// 문자열을 인스턴스 식별자로 변환합니다.
    /// </summary>
    public static implicit operator DbInstanceId(string value) => new(value);
}

#endregion

#region BulkInsert 옵션 정의

/// <summary>
/// SqlBulkCopy 기반 대량 INSERT 옵션입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>불변 초기화</b>: <c>init</c> 속성으로 생성 후 변경을 방지합니다.<br/>
/// - <b>안전한 기본값</b>: 일반적인 벌크 작업에 적합한 기본값을 제공합니다.<br/>
/// - <b>AOT 비호환 공지</b>: BulkInsertAsync는 Reflection 기반이므로 AOT 빌드에서는 사용할 수 없습니다.
/// </para>
/// </summary>
public sealed class BulkInsertOptions
{
    /// <summary>배치당 행 수 (기본: 5,000)</summary>
    public int BatchSize { get; init; } = 5_000;

    /// <summary>명령 타임아웃 (초, 기본: 600)</summary>
    public int TimeoutSeconds { get; init; } = 600;

    /// <summary>스트리밍 활성화 (기본: true)</summary>
    public bool EnableStreaming { get; init; } = true;

    /// <summary>INSERT 트리거 실행 여부</summary>
    public bool FireTriggers { get; init; }

    /// <summary>제약 조건 검사 여부</summary>
    public bool CheckConstraints { get; init; }

    /// <summary>IDENTITY 값 유지 여부</summary>
    public bool KeepIdentity { get; init; }
}

#endregion

#region AOT-safe Bulk 옵션/결과 정의

/// <summary>
/// AOT-safe 벌크 쓰기 작업의 공통 옵션입니다.
/// </summary>
public class BulkWriteOptions
{
    /// <summary>배치당 행 수 (기본: 5,000)</summary>
    public int BatchSize { get; init; } = 5_000;

    /// <summary>명령 타임아웃 (초, 기본: 600)</summary>
    public int TimeoutSeconds { get; init; } = 600;

    /// <summary>스트리밍 활성화 (기본: true)</summary>
    public bool EnableStreaming { get; init; } = true;

    /// <summary>로컬 트랜잭션 사용 여부 (기본: true)</summary>
    public bool UseTransaction { get; init; } = true;

    /// <summary>INSERT 트리거 실행 여부</summary>
    public bool FireTriggers { get; init; }

    /// <summary>제약 조건 검사 여부</summary>
    public bool CheckConstraints { get; init; } = true;

    /// <summary>IDENTITY 값 유지 여부</summary>
    public bool KeepIdentity { get; init; }

    /// <summary>
    /// 작업 종류와 무관한 스칼라 옵션 범위만 검증합니다.
    /// </summary>
    public virtual void Validate()
    {
        if (BatchSize <= 0)
            throw new InvalidOperationException("Bulk batch size must be greater than zero.");

        if (TimeoutSeconds <= 0)
            throw new InvalidOperationException("Bulk timeout seconds must be greater than zero.");
    }
}

/// <summary>
/// API-level 벌크 merge에서 수행할 staged action 플래그입니다.
/// </summary>
[Flags]
public enum BulkMergeActions
{
    /// <summary>선택된 action 없음</summary>
    None = 0,

    /// <summary>대상에 존재하는 key를 update</summary>
    UpdateMatched = 1,

    /// <summary>대상에 없는 key를 insert</summary>
    InsertMissing = 2,

    /// <summary>대상에 존재하는 key를 delete</summary>
    DeleteMatched = 4,

    /// <summary>소스에 없는 대상 행을 delete (v2.4.0 미지원)</summary>
    DeleteNotMatchedBySource = 8
}

/// <summary>
/// AOT-safe 벌크 merge 옵션입니다.
/// </summary>
public sealed class BulkMergeOptions : BulkWriteOptions
{
    private const BulkMergeActions KnownActions =
        BulkMergeActions.UpdateMatched
        | BulkMergeActions.InsertMissing
        | BulkMergeActions.DeleteMatched
        | BulkMergeActions.DeleteNotMatchedBySource;

    /// <summary>수행할 merge action입니다. 기본값은 update matched + insert missing입니다.</summary>
    public BulkMergeActions Actions { get; init; } =
        BulkMergeActions.UpdateMatched | BulkMergeActions.InsertMissing;

    /// <inheritdoc />
    public override void Validate()
    {
        base.Validate();

        if ((Actions & ~KnownActions) != 0)
            throw new InvalidOperationException("Bulk merge options contain an unknown merge action.");

        if (Actions == BulkMergeActions.None)
            throw new InvalidOperationException("Bulk merge actions cannot be empty.");

        if ((Actions & BulkMergeActions.DeleteNotMatchedBySource) != 0)
            throw new InvalidOperationException("DeleteNotMatchedBySource is not supported by Lib.Db v2.4.0 bulk merge.");

        if ((Actions & BulkMergeActions.DeleteMatched) != 0
            && Actions != BulkMergeActions.DeleteMatched)
        {
            throw new InvalidOperationException("DeleteMatched is exclusive in Lib.Db v2.4.0 bulk merge.");
        }
    }
}

/// <summary>
/// 벌크 upsert 결과 카운트입니다.
/// </summary>
/// <param name="Inserted">삽입된 행 수</param>
/// <param name="Updated">수정된 행 수</param>
public readonly record struct BulkUpsertResult(long Inserted, long Updated)
{
    /// <summary>총 영향 행 수입니다.</summary>
    public long TotalAffected => Inserted + Updated;
}

/// <summary>
/// 벌크 merge 결과 카운트입니다.
/// </summary>
/// <param name="Inserted">삽입된 행 수</param>
/// <param name="Updated">수정된 행 수</param>
/// <param name="Deleted">삭제된 행 수</param>
public readonly record struct BulkMergeResult(long Inserted, long Updated, long Deleted)
{
    /// <summary>총 영향 행 수입니다.</summary>
    public long TotalAffected => Inserted + Updated + Deleted;
}

#endregion

#region JSON 컬럼 어트리뷰트 정의

/// <summary>
/// JSON 컬럼을 C# 타입으로 자동 역직렬화하는 어트리뷰트입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// DB에서 조회한 JSON 문자열 컬럼에 이 어트리뷰트를 지정하면,
/// 확장 메서드를 통해 자동으로 역직렬화할 수 있습니다.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class JsonColumnAttribute : Attribute;

#endregion

#region TVP 검증 모드 정의

/// <summary>
/// TVP(Table-Valued Parameter) 스키마 검증 모드를 정의합니다.
/// </summary>
public enum TvpValidationMode
{
    /// <summary>
    /// (기본값) 스키마 불일치 시 예외를 발생시켜 실행을 즉시 중단합니다. 
    /// <para>데이터 무결성을 최우선으로 할 때 사용합니다.</para>
    /// </summary>
    Strict,

    /// <summary>
    /// 불일치 시 Error 로그만 남기고 실행을 계속합니다. 
    /// <para>서비스 중단 없는 가용성을 우선할 때 사용합니다.</para>
    /// </summary>
    LogOnly,

    /// <summary>
    /// 검증을 수행하지 않습니다. 
    /// <para>극한의 성능이 필요하거나, 스키마가 100% 신뢰할 수 있는 상태일 때 사용합니다.</para>
    /// </summary>
    None
}

#endregion
