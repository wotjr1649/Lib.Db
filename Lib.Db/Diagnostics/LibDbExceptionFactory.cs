// ============================================================================
// 파일: Lib.Db/Diagnostics/LibDbExceptionFactory.cs
// 설명: 라이브러리 전반에서 발생하는 예외를 중앙 집중식으로 생성하는 팩토리입니다.
//       모든 예외 메시지는 한국어로 제공되며, 일관된 식별 체계를 따릅니다.
// ============================================================================

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Lib.Db.Diagnostics;

/// <summary>
/// [Internal] 예외 생성 팩토리
/// </summary>
internal static class LibDbExceptionFactory
{
    #region 예외 생성 팩토리 (Exception Factory)
    /// <summary>
    /// 필수 의존성 또는 설정이 누락되었을 때 발생하는 예외를 생성합니다.
    /// </summary>
    [DoesNotReturn]
    public static void ThrowArgumentNull(string paramName, [CallerMemberName] string? caller = null)
    {
        throw new ArgumentNullException(paramName, 
            $"[{caller}] 필수 인자 '{paramName}'가 누락되었습니다. (값이 null일 수 없습니다)");
    }

    /// <summary>
    /// 유효하지 않은 인자가 전달되었을 때 발생하는 예외를 생성합니다.
    /// </summary>
    [DoesNotReturn]
    public static void ThrowArgument(string message, string paramName, [CallerMemberName] string? caller = null)
    {
        throw new ArgumentException($"[{caller}] {message}", paramName);
    }

    /// <summary>
    /// 객체의 상태가 작업을 수행하기에 올바르지 않을 때 발생하는 예외를 생성합니다.
    /// </summary>
    [DoesNotReturn]
    public static void ThrowInvalidOperation(string message, [CallerMemberName] string? caller = null)
    {
        throw new InvalidOperationException($"[{caller}] {message}");
    }

    /// <summary>
    /// 특정 기능을 지원하지 않을 때 발생하는 예외를 생성합니다.
    /// </summary>
    [DoesNotReturn]
    public static void ThrowNotSupported(string message)
    {
        throw new NotSupportedException(message);
    }

    /// <summary>
    /// 객체가 이미 해제되었는데 접근하려 할 때 발생하는 예외를 생성합니다.
    /// </summary>
    [DoesNotReturn]
    public static void ThrowObjectDisposed(string objectName)
    {
        throw new ObjectDisposedException(objectName, $"'{objectName}' 객체가 이미 해제(Dispose)되었습니다. 더 이상 사용할 수 없습니다.");
    }

    public static Exception CreateFailedToCreateAccessor(string typeName)
    {
        return new InvalidOperationException(
            $"DTO '{typeName}'에 대한 TVP Accessor(Getter)를 생성할 수 없습니다. " +
            "public 속성이 존재하지 않거나 접근할 수 없습니다.");
    }

    public static Exception CreateTvpValidationFailed(string udtName, string reason)
    {
        return new InvalidOperationException(
            $"TVP(사용자 정의 테이블 타입) '{udtName}' 검증에 실패했습니다. 원인: {reason}");
    }

    public static Exception CreateCommandExecutionFailed(string commandText, Exception inner)
    {
        return new InvalidOperationException(
            $"명령 실행 중 오류가 발생했습니다. (Target: {Truncate(commandText, 50)})", inner);
    }

    public static Exception CreateSchemaMismatch(string spName, int errorCode)
    {
        return new InvalidOperationException(
            $"저장 프로시저 '{spName}'의 스키마 불일치(Error: {errorCode})가 감지되었습니다. " +
            "스키마 캐시가 만료되었거나 파라미터 정의가 DB와 다릅니다.");
    }
    
    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "...";
    }

    #endregion
}
