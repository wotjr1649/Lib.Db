// ============================================================================
// 파일: Lib.Db/Contracts/Diagnostics/IQueryAnalyzer.cs
// 설명: 쿼리 분석기 인터페이스 — 실행 계획 및 성능 분석 계약 정의
// 대상: .NET 10 / C# 14
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lib.Db.Contracts.Diagnostics;

#region 성능 분석 결과 (Performance Info)

/// <summary>
/// 분석된 쿼리 성능 정보입니다.
/// </summary>
public record QueryPerformanceInfo(
    string QueryText,
    long ExecutionCount,
    long TotalWorkerTime,
    long TotalElapsedTime,
    double AvgElapsedTime,
    long LastElapsedTime,
    DateTime LastExecutionTime
);

#endregion

#region 분석 인터페이스 (Analyzer Interface)

/// <summary>
/// 데이터베이스 쿼리 성능을 분석하는 인터페이스입니다.
/// </summary>
public interface IQueryAnalyzer
{
    /// <summary>
    /// 상위 N개의 느린 쿼리를 비동기로 조회합니다.
    /// </summary>
    Task<IEnumerable<QueryPerformanceInfo>> AnalyzeSlowQueriesAsync(int top = 10, CancellationToken cancellationToken = default);
}

#endregion
