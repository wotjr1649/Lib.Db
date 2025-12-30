// ============================================================================
// 파일: Lib.Db/Infrastructure/Resilience/DefaultResilienceComponents.cs
// 설명: Resilience 파이프라인 기본 구성 요소
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using Lib.Db.Contracts.Infrastructure;
using Microsoft.Data.SqlClient;
using Polly;

namespace Lib.Db.Infrastructure.Resilience;

/// <summary>
/// Resilience가 비활성화된 경우 사용하는 No-Op 구현체입니다.
/// <para>
/// IsEnabled = false를 반환하여 핫 패스 최적화를 허용합니다 (파이프라인 할당 우회).
/// </para>
/// </summary>
internal sealed class NoOpResiliencePipelineProvider : IResiliencePipelineProvider
{
    public bool IsEnabled => false;

    // 계약을 충족하기 위해 빈 파이프라인을 반환합니다. (IsEnabled가 접근을 차단해야 함)
    public ResiliencePipeline Pipeline => ResiliencePipeline.Empty;
}

/// <summary>
/// SQL Server용 일시적 오류 감지 기본 구현체입니다.
/// </summary>
internal sealed class DefaultTransientSqlErrorDetector : ITransientSqlErrorDetector
{
    public bool IsTransient(Exception ex)
    {
        if (ex is SqlException sqlEx)
        {
            // 컴렉션의 오류 중 일시적 오류가 있는지 확인
            foreach (SqlError error in sqlEx.Errors)
            {
                if (IsTransientError(error.Number)) 
                    return true;
            }
            return false;
        }

        return ex is TimeoutException;
    }

    // 일반적인 SQL 일시적 오류 코드 (Unit Testing용 Internal)
    internal static bool IsTransientError(int number) => number switch
    {
        1205 => true,  // 교착 상태 희생자
        -2 => true,    // 클라이언트 시간 초과
        53 => true,    // 네트워크 경로 찾을 수 없음
        233 => true,   // 전송 레벨 오류
        10053 => true, // 전송 레벨 오류
        10054 => true, // 전송 레벨 오류
        10060 => true, // 네트워크 시간 초과
        40613 => true, // Azure DB 데이터베이스 사용 불가
        40197 => true, // Azure DB 요청 처리 중
        40501 => true, // Azure DB 서비스 사용 중
        49918 => true, // Azure DB 요청 처리 중
        _ => false
    };
}
