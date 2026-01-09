// ============================================================================
// 파일: Lib.Db/Caching/CachingInfrastructure.cs
// 설명: [Architecture] 캐싱 인프라 스트럭처 (범위, 키 생성, 헬퍼)
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Lib.Db.Caching;

#region 캐시 범위 정의

/// <summary>
/// 캐시 데이터의 공유 범위를 정의합니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// </para>
/// <list type="bullet">
/// <item><description><strong>보안 및 격리</strong>: 민감 데이터가 다른 사용자 세션과 공유되지 않도록 격리 수준을 제어합니다.</description></item>
/// <item><description><strong>유연성</strong>: CI/CD 환경이나 개발자 로컬 머신 등 다양한 환경에 맞춰 범위를 조정할 수 있습니다.</description></item>
/// </list>
/// </remarks>
public enum CacheScope
{
    /// <summary>
    /// 로컬 사용자별로 격리됩니다. (기본값)
    /// <para>동일 머신의 다른 사용자는 이 캐시에 접근할 수 없습니다.</para>
    /// </summary>
    User = 0,

    /// <summary>
    /// 머신 전체에서 공유됩니다.
    /// <para>IIS 워커 프로세스 간 캐시 공유 시 유리하지만, 보안에 유의해야 합니다.</para>
    /// </summary>
    Machine = 1,
}

#endregion

#region 기본 격리 키 생성기

/// <summary>
/// SHA256 해시를 사용하여 결정론적인 격리 키를 생성하는 기본 구현체입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 연결 문자열과 같은 민감 정보를 직접 키로 사용하지 않고, SHA256 해시를 통해 보안성을 높이고 일정한 길이를 보장합니다.
/// 이를 통해 캐시 경로나 뮤텍스 이름 생성 시 발생할 수 있는 길이 제한이나 특수 문자 문제를 예방합니다.
/// </para>
/// <para><strong>Primary Constructor 적용</strong>: .NET 10 / C# 12 표준을 준수합니다.</para>
/// </remarks>
public sealed class IsolationKeyGenerator() : Lib.Db.Contracts.Cache.IIsolationKeyGenerator // Primary Constructor (Empty but explicit)
{
    /// <inheritdoc/>
    public string Generate(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "Global";

        // 연결 문자열 정규화 (소문자, 공백 제거 등은 하지 않음 - 민감할 수 있음)
        // SHA256 -> Hex String (32 chars)
        var bytes = Encoding.UTF8.GetBytes(connectionString);
        var hash = SHA256.HashData(bytes);
        
        // 너무 길지 않게 앞 16자리만 사용 (충돌 확률 극히 낮음 for Isolation purpose)
        return Convert.ToHexString(hash).Substring(0, 16);
    }
}

#endregion

#region 캐시 내부 유틸리티

/// <summary>
/// 캐시 시스템 내부에서 사용되는 경로 해결, 뮤텍스 접두사 생성 등의 유틸리티를 제공합니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 운영체제별 차이(Windows vs Linux)와 실행 환경(User vs Machine Scope)의 복잡성을 캡슐화합니다.
/// </para>
/// </summary>
internal static class CacheInternalHelpers
{
    /// <summary>
    /// 현재 운영체제 사용자의 SID 해시를 반환합니다. (Windows 전용)
    /// </summary>
    public static string GetUserSidHash()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                // [Performance] WindowsIdentity.GetCurrent()는 비용이 있으므로 실제로는 캐싱이 권장되나,
                // 이 메서드 호출 빈도가 낮으므로(초기화 시) 직접 호출합니다.
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var sid = identity.User?.Value ?? "NoUser";
                
                // [Deterministic] 프로세스 간 합의를 위해 결정론적 해시 사용
                return GetDeterministicShortHash(sid);
            }
            catch
            {
                return "Unknown";
            }
        }
        return "Nix"; // Non-Windows
    }

    /// <summary>
    /// 옵션에 따라 적절한 뮤텍스 이름 접두사를 생성합니다.
    /// </summary>
    /// <remarks>
    /// <para><strong>Global\\ vs Local\\</strong>: Machine 스코프는 Global 네임스페이스를 사용하여 세션 간 공유를 지원합니다.</para>
    /// </remarks>
    public static string GetMutexPrefix(SharedMemoryCacheOptions options)
    {
        return GetMutexPrefix(options, "Cache");
    }

    /// <summary>
    /// 옵션에 따라 적절한 뮤텍스 이름 접두사를 생성합니다. (Subsystem 지정 가능)
    /// </summary>
    public static string GetMutexPrefix(SharedMemoryCacheOptions options, string subsystem)
    {
        if (options.Scope == CacheScope.Machine)
        {
            return $"Global\\Lib.Db.{subsystem}_{options.IsolationKey ?? "Shared"}_";
        }
        else
        {
            var userHash = GetUserSidHash();
            var basePath = ResolveBasePath(options);
            var pathHash = GetDeterministicShortHash(basePath);
            var isoKey = options.IsolationKey ?? "Default";
            return $"Local\\Lib.Db.{subsystem}_{userHash}_{pathHash}_{isoKey}_";
        }
    }

    /// <summary>
    /// 캐시 파일이 저장될 기본 경로를 절대 경로로 반환합니다.
    /// </summary>
    public static string ResolveBasePath(SharedMemoryCacheOptions options)
    {
        var raw = options.BasePath;
        
        // 환경 변수 확장 (%TEMP% 등)
        var expanded = Environment.ExpandEnvironmentVariables(raw);

        if (Path.IsPathRooted(expanded))
            return expanded;

        // 상대 경로인 경우, 실행 파일 위치 기준
        return Path.Combine(AppContext.BaseDirectory, expanded);
    }

    /// <summary>
    /// 문자열에 대한 결정론적이고 짧은 해시를 반환합니다.
    /// </summary>
    private static string GetDeterministicShortHash(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "NoPath";
        
        // 정규화: 경로 구분자 통일
        var normalized = input.Trim().ToLowerInvariant().Replace('\\', '/');
        if (normalized.EndsWith("/")) normalized = normalized[..^1];

        // SHA256 사용
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        
        // 8글자만 (충돌 가능성 감수, 경로 식별용)
        return Convert.ToHexString(hash).Substring(0, 8);
    }
}

#endregion
