// ============================================================================
// 파일: Lib.Db/Configuration/ConnectionSecurityProfile.cs
// 설명: 연결 문자열 보안 검증 프로필
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

namespace Lib.Db.Configuration;

/// <summary>
/// 연결 문자열에 적용할 보안 검증 강도를 나타냅니다.
/// </summary>
public enum ConnectionSecurityProfile
{
    /// <summary>
    /// 로컬 개발 및 테스트에 적합한 기본 프로필입니다.
    /// </summary>
    Development = 0,

    /// <summary>
    /// 운영 환경 기준의 연결 문자열 검증을 적용합니다.
    /// </summary>
    Production = 1
}
