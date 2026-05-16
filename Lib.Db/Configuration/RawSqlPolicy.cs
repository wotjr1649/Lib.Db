// ============================================================================
// 파일: Lib.Db/Configuration/RawSqlPolicy.cs
// 설명: Text 기반 Raw SQL 실행 정책
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

namespace Lib.Db.Configuration;

/// <summary>
/// <see cref="System.Data.CommandType.Text"/> 명령 실행을 제한하는 정책입니다.
/// </summary>
public enum RawSqlPolicy
{
    /// <summary>
    /// Raw SQL 텍스트 실행을 허용합니다. 기존 동작과 동일합니다.
    /// </summary>
    Allow = 0,

    /// <summary>
    /// 모든 Raw SQL 텍스트 실행을 차단합니다. 저장 프로시저 호출은 영향을 받지 않습니다.
    /// </summary>
    DenyAllText = 1,

    /// <summary>
    /// 쓰기, 권한, 스키마 변경, DB 운영 계열로 분류되는 Raw SQL 텍스트만 차단합니다.
    /// <para>
    /// 주석과 문자열/식별자 리터럴을 건너뛴 뒤 위험 토큰을 보수적으로 탐지하지만 완전한 SQL 파서가 아닙니다.
    /// 복합 SQL 차단을 보안 경계로 요구하는 운영 환경에는 <see cref="DenyAllText"/>를 권장합니다.
    /// </para>
    /// </summary>
    DenyWriteText = 2
}
