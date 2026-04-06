// ============================================================================
// 파일: Lib.Db/Contracts/Infrastructure/IDbInterceptor.cs
// 설명: 사용자 수준 DB 쿼리 인터셉터 계약 (DI 등록 가능)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

namespace Lib.Db.Contracts.Infrastructure;

#region 인터셉터 인터페이스

/// <summary>
/// DB 명령 실행 전후를 가로채는 사용자 수준 인터셉터 인터페이스입니다.
/// <para><b>[설계 의도]</b> 로깅, 감사, 메트릭, 쿼리 변환 등을 실행 파이프라인에
/// 비침투적으로 삽입할 수 있도록 합니다.</para>
/// <para>
/// 기존 내부 인터셉터(<see cref="Lib.Db.Contracts.Execution.IDbCommandInterceptor"/>)와 별도로,
/// 사용자가 DI를 통해 등록할 수 있는 고수준 API를 제공합니다.
/// </para>
/// </summary>
public interface IDbInterceptor
{
    /// <summary>명령 실행 직전에 호출됩니다.</summary>
    /// <param name="context">인터셉션 컨텍스트 (명령 정보, 상태 전달용)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>실행 계속 여부를 나타내는 <see cref="DbInterceptionResult"/></returns>
    ValueTask<DbInterceptionResult> OnExecutingAsync(
        DbInterceptionContext context, CancellationToken ct);

    /// <summary>명령 실행 성공 직후에 호출됩니다.</summary>
    /// <param name="context">인터셉션 컨텍스트 (ElapsedMs, Result 포함)</param>
    /// <param name="ct">취소 토큰</param>
    ValueTask OnExecutedAsync(
        DbInterceptionContext context, CancellationToken ct);

    /// <summary>명령 실행 실패 시 호출됩니다.</summary>
    /// <param name="context">인터셉션 컨텍스트 (Exception 포함)</param>
    /// <param name="ct">취소 토큰</param>
    ValueTask OnErrorAsync(
        DbInterceptionContext context, CancellationToken ct);
}

#endregion

#region 인터셉터 실행 결과

/// <summary>인터셉터 실행 결과를 나타냅니다.</summary>
public enum DbInterceptionResult
{
    /// <summary>실행을 계속합니다.</summary>
    Continue,

    /// <summary>실행을 억제합니다 (DB 호출 건너뜀).</summary>
    Suppress
}

#endregion

#region 인터셉션 컨텍스트

/// <summary>
/// 인터셉션 컨텍스트 — 실행 전/후/에러 시 인터셉터에 전달되는 정보를 담습니다.
/// <para><b>[설계 의도]</b> 인터셉터 간 데이터 전달 및 실행 메타데이터 공유를 위한 컨텍스트 객체입니다.</para>
/// </summary>
public sealed class DbInterceptionContext
{
    /// <summary>실행할 명령 텍스트 (SP 이름 또는 SQL)</summary>
    public required string CommandText { get; init; }

    /// <summary>명령 유형</summary>
    public required System.Data.CommandType CommandType { get; init; }

    /// <summary>대상 인스턴스 이름</summary>
    public required string InstanceName { get; init; }

    /// <summary>실행 시작 시각 (UTC)</summary>
    public DateTime StartTime { get; init; } = DateTime.UtcNow;

    /// <summary>실행 소요 시간 (밀리초, OnExecuted/OnError에서 설정)</summary>
    public long? ElapsedMs { get; set; }

    /// <summary>실행 결과 (OnExecuted에서 설정)</summary>
    public object? Result { get; set; }

    /// <summary>발생한 예외 (OnError에서 설정)</summary>
    public Exception? Exception { get; set; }

    /// <summary>사용자 정의 상태 (인터셉터 간 데이터 전달)</summary>
    public Dictionary<string, object?> State { get; } = [];
}

#endregion
