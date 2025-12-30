// ============================================================================
// 파일: Lib.Db/Core/Runtime.cs
// 설명: 실행 컨텍스트(AsyncLocal) + 런타임 브리지 + AOT(Json) 컨텍스트
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;

namespace Lib.Db.Core;

#region 실행 컨텍스트
// DbExecutionContext

/// <summary>
/// 현재 실행 중인 DB 명령의 문맥 정보를 담는 불변 구조체입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>단방향 전파</b>: AsyncLocal을 통해 호출 스택 깊숙한 곳까지 컨텍스트를 전달합니다.<br/>
/// - <b>불변성(Immutability)</b>: 구조체가 불변이므로 멀티스레드 환경에서 안전하게 공유됩니다.<br/>
/// - <b>관진단 용이성</b>: CorrelationId와 CommandText를 포함하여 분산 추적 및 로깅에 필수적인 정보를 제공합니다.
/// </para>
/// </summary>
internal readonly record struct DbExecutionContext(
    string InstanceName,
    string CommandText,
    CommandType CommandType,
    bool IsTransactional = false,
    string? CorrelationId = null,
    string? DatabaseName = null)
{
    /// <summary>
    /// 일반 명령용 컨텍스트를 생성합니다.
    /// </summary>
    /// <param name="instanceName">논리 인스턴스 이름 또는 해시</param>
    /// <param name="commandText">실행할 SQL 텍스트 또는 SP 이름</param>
    /// <param name="commandType">명령 유형</param>
    /// <param name="isTransactional">트랜잭션 경계 안에서 실행되는지 여부</param>
    /// <param name="correlationId">상위 호출과 연계되는 상관 ID</param>
    /// <param name="databaseName">대상 DB 이름 (알 수 없으면 null)</param>
    public static DbExecutionContext ForCommand(
        string instanceName,
        string commandText,
        CommandType commandType,
        bool isTransactional = false,
        string? correlationId = null,
        string? databaseName = null)
        => new(instanceName, commandText, commandType, isTransactional, correlationId, databaseName);
}

#endregion

#region 실행 컨텍스트 스코프
// DbExecutionContextScope

/// <summary>
/// <see cref="DbExecutionContext"/> 를 AsyncLocal Stack 기반으로 관리하는 스코프 관리자입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>중첩 지원(Nested Scope)</b>: Stack 구조를 사용하여 트랜잭션 내 트랜잭션 등 중첩 호출 시 컨텍스트를 보존합니다.<br/>
/// - <b>리소스 안전성</b>: <see cref="IDisposable"/> 패턴을 강제하여 스코프 이탈 시 반드시 이전 상태로 복구되도록 보장합니다.<br/>
/// - <b>비침투적 설계</b>: 비즈니스 로직에 영향을 주지 않고, 횡단 관심사(Logging, Metrics)에만 컨텍스트를 노출합니다.
/// </para>
/// </summary>
internal static class DbExecutionContextScope
{
    // [개선] 단일 값 대신 Stack 구조 사용
    private static readonly AsyncLocal<Stack<DbExecutionContext>?> s_contextStack = new();

    /// <summary>
    /// 현재 최상위 컨텍스트를 반환합니다 (Stack의 Peek).
    /// <para>
    /// Nested Context인 경우 가장 최근에 Enter된 컨텍스트가 반환됩니다.
    /// </para>
    /// </summary>
    public static DbExecutionContext? Current
    {
        get
        {
            var stack = s_contextStack.Value;
            return stack is { Count: > 0 } ? stack.Peek() : null;
        }
    }

    /// <summary>
    /// 이미 생성된 <see cref="DbExecutionContext"/> 를 사용하여 새 스코프를 엽니다.
    /// <para>
    /// Stack에 Push되며, Dispose 시 자동으로 Pop됩니다.
    /// </para>
    /// </summary>
    public static Scope Enter(DbExecutionContext context)
    {
        var stack = s_contextStack.Value;
        if (stack is null)
        {
            // 일반적으로 Nested 깊이 < 4
            stack = new Stack<DbExecutionContext>(capacity: 4);
            s_contextStack.Value = stack;
        }

        stack.Push(context);
        return new Scope(stack);
    }

    /// <summary>
    /// 개별 필드 값을 기반으로 새 <see cref="DbExecutionContext"/> 를 생성하고 스코프를 엽니다.
    /// </summary>
    public static Scope Enter(
        string instanceName,
        string commandText,
        CommandType commandType,
        bool isTransactional = false,
        string? correlationId = null,
        string? databaseName = null)
        => Enter(DbExecutionContext.ForCommand(instanceName, commandText, commandType,
            isTransactional, correlationId, databaseName));

    /// <summary>
    /// Dispose 시 이전 컨텍스트로 복원되는 스코프 핸들입니다.
    /// <para>
    /// Stack에서 Pop하여 이전 컨텍스트를 자동 복원합니다.
    /// </para>
    /// </summary>
    internal struct Scope : IDisposable
    {
        private readonly Stack<DbExecutionContext>? _stack;

        internal Scope(Stack<DbExecutionContext> stack) => _stack = stack;

        /// <summary>
        /// 스코프를 종료하고, 이전 컨텍스트를 복원합니다.
        /// <para>
        /// Stack에서 Pop하여 Nested Context를 안전하게 해제합니다.
        /// 이중 호출을 허용하며, 예외를 던지지 않습니다.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            if (_stack is { Count: > 0 })
                _stack.Pop();
        }
    }
}

#endregion

#region 라이브러리 런타임 브리지
// LibDbRuntime

/// <summary>
/// Lib.Db 런타임 전체의 정적 상태를 구성하고, 테스트/특수 시나리오에서
/// 안전하게 리셋할 수 있도록 하는 중앙 브리지입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>중앙 집중 구성</b>: TVP 검증, 메트릭, 캐시 정책 등 전역 설정을 한 곳에서 관리하여 일관성을 보장합니다.<br/>
/// - <b>테스트 격리 지원</b>: <see cref="ResetForTesting"/>을 통해 테스트 간 정적 상태 오염을 방지합니다.<br/>
/// - <b>런타임 안전 모드</b>: 구성 여부를 추적하여 필수 설정(TVP Validatior 등)이 누락된 상태로 실행되는 것을 방지합니다.
/// </para>
/// </summary>
public static class LibDbRuntime
{
    private static readonly object s_sync = new();

    /// <summary>
    /// 현재 프로세스에서 Lib.Db가 최소 한 번이라도 <see cref="Configure"/> 되었는지 여부입니다.
    /// <para>운영 로직에서 강제할 필요는 없지만, 진단/테스트 시 유용하게 사용할 수 있습니다.</para>
    /// </summary>
    public static bool IsConfigured { get; private set; }

    #region 구성 엔트리 포인트

    /// <summary>
    /// Lib.Db의 정적 상태를 구성합니다.
    /// <para>
    /// 일반적으로 애플리케이션 시작 시 한 번 호출하는 것을 권장합니다.<br/>
    /// - TVP 캐시 정책 구성 (<see cref="LibDbOptions"/> 기반)<br/>
    /// - TVP 스키마 검증 브리지 구성(<paramref name="tvpValidator"/> 콜백)<br/>
    /// - 메트릭 수집 전역 On/Off 설정
    /// </para>
    /// </summary>
    /// <param name="options">Lib.Db 전역 옵션 (TVP 캐시 크기 등)</param>
    /// <param name="tvpValidator">
    /// TVP 스키마 강제 검증에 사용할 콜백입니다.<br/>
    /// 매 호출 시 DTO 타입과 TVP 타입명을 받아, 일치하면 <c>true</c>, 불일치하면 <c>false</c> 를 반환해야 합니다.
    /// </param>
    /// <param name="enableMetrics">
    /// <c>true</c> 인 경우 메트릭을 활성화하고, <c>false</c> 인 경우 메트릭을 전역 비활성화합니다.<br/>
    /// <c>null</c> 이면 이전 설정을 유지합니다.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> 가 <see langword="null"/> 인 경우 발생합니다.
    /// </exception>
    public static void Configure(
        LibDbOptions options,
        Func<Type, string, bool>? tvpValidator = null,
        bool? enableMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (s_sync)
        {
            // 1) TVP 캐시 및 버퍼 정책 구성
            //    - MaxCacheSize 등은 LibDbOptions에서 이미 검증되었다고 가정합니다.
            DbBinder.ConfigureTvp(options);

            // 2) TVP 스키마 검증 브리지 구성
            //    - null 인 경우 검증 없이 동작하며, ADO.NET 수준에서 런타임 오류가 발생할 수 있습니다.
            DbBinder.ValidatorCallback = tvpValidator;

            // 3) 메트릭 전역 On/Off
            if (enableMetrics is { } flag)
                DbMetrics.IsEnabled = flag;

            IsConfigured = true;
        }
    }

    /// <summary>
    /// TVP 스키마 검증 브리지만 별도로 구성할 때 사용하는 헬퍼입니다.
    /// <para>
    /// 이미 <see cref="Configure(LibDbOptions, Func{Type, string, bool}?, bool?)"/> 를 통해
    /// TVP 캐시 정책을 구성한 뒤, 검증 정책만 교체하고 싶을 때 사용합니다.
    /// </para>
    /// </summary>
    /// <param name="tvpValidator">
    /// DTO 타입과 TVP 타입명을 받아 일치 여부를 판단하는 검증 콜백입니다.<br/>
    /// <c>null</c> 을 전달하면 이후 TVP는 검증 없이 전송됩니다.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ConfigureTvpValidation(Func<Type, string, bool>? tvpValidator)
    {
        lock (s_sync)
        {
            DbBinder.ValidatorCallback = tvpValidator;
            IsConfigured = true;
        }
    }

    /// <summary>
    /// 메트릭 수집의 전역 활성/비활성 상태를 설정합니다.
    /// </summary>
    /// <param name="enabled">
    /// <c>true</c> : 메트릭 활성 / <c>false</c> : 전역 비활성 (모든 Track* 호출 무시)
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ConfigureMetrics(bool enabled)
    {
        DbMetrics.IsEnabled = enabled;
        IsConfigured = true;
    }

    #endregion

    #region 테스트 및 리셋 지원

    /// <summary>
    /// 테스트/개발 환경에서 Lib.Db의 정적 상태를 초기화합니다.
    /// <para>
    /// - TVP 검증 콜백 제거 (<see cref="DbBinder.ValidatorCallback"/> = null)<br/>
    /// - TVP/버퍼 관련 캐시 모두 초기화 (<see cref="DbBinder.ClearTvpCaches"/>)<br/>
    /// - 메트릭 전역 설정 초기화 (<see cref="DbMetrics.ResetForTesting"/> 호출)<br/>
    /// </para>
    /// <para>
    /// <b>운영 환경에서는 호출하지 않는 것을 강력히 권장</b>합니다.
    /// </para>
    /// </summary>
    public static void ResetForTesting()
    {
        lock (s_sync)
        {
            // 1) TVP 정적 상태 초기화
            DbBinder.ValidatorCallback = null;
            DbBinder.ClearTvpCaches();

            // 2) 메트릭 전역 상태 초기화
            DbMetrics.ResetForTesting();

            IsConfigured = false;
        }
    }

    #endregion
}

#endregion

#region 문자열 전처리기 (Internal)

/// <summary>
/// SIMD 가속 및 SearchValues를 활용한 초고속 문자열 정리(Sanitize) 유틸리티입니다.
/// </summary>
internal static class StringPreprocessor
{
    // SearchValues는 .NET 8+에서 CPU 벡터 명령어를 사용하여 
    // IndexOfAny 등의 검색을 획기적으로 가속화합니다.
    private static readonly System.Buffers.SearchValues<char> s_whitespace = System.Buffers.SearchValues.Create(
        "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0" +
        "\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A" +
        "\u2028\u2029\u202F\u205F\u3000\u200B\u2060\u200C\u200D\uFEFF\u00AD\u180E"
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> Sanitize(string? input)
        => input is null ? ReadOnlySpan<char>.Empty : Sanitize(input.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> Sanitize(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty) return ReadOnlySpan<char>.Empty;

        // 1. 앞뒤 공백 제거
        span = TrimAllWhitespace(span);
        if (span.IsEmpty) return ReadOnlySpan<char>.Empty;

        // 2. NULL 문자(\0) 이후 절삭 (C++ 연동 데이터 등에서 발생 가능)
        int nullIndex = span.IndexOf('\0');
        if (nullIndex >= 0)
        {
            span = span[..nullIndex]; // Range Slicing
            span = TrimEndAllWhitespace(span);
        }

        return span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> TrimAllWhitespace(ReadOnlySpan<char> span)
    {
        int start = span.IndexOfAnyExcept(s_whitespace);
        if (start < 0) return ReadOnlySpan<char>.Empty;

        int end = span.LastIndexOfAnyExcept(s_whitespace);
        return span.Slice(start, end - start + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> TrimEndAllWhitespace(ReadOnlySpan<char> span)
    {
        int end = span.LastIndexOfAnyExcept(s_whitespace);
        return end < 0 ? ReadOnlySpan<char>.Empty : span.Slice(0, end + 1);
    }
}
#endregion