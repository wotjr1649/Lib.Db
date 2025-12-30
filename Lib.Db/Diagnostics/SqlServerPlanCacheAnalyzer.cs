using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lib.Db.Contracts.Execution;

using Lib.Db.Contracts.Diagnostics;

namespace Lib.Db.Diagnostics;

#region SQL Server 실행 계획 분석기 (Plan Cache Analyzer)

/// <summary>
/// SQL Server의 DMV(sys.dm_exec_query_stats)를 활용한 실행 계획 분석기입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// - DMV를 직접 조회하여 별도의 모니터링 에이전트 없이 애플리케이션 레벨에서 성능 병목을 진단합니다.<br/>
/// - <see cref="IDbExecutor"/>를 통해 쿼리를 실행하므로 기존 연결 설정과 보안 정책을 그대로 따릅니다.
/// </para>
/// </summary>
public class SqlServerPlanCacheAnalyzer : IQueryAnalyzer
{
    private readonly IDbExecutor _executor;

    public SqlServerPlanCacheAnalyzer(IDbExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<IEnumerable<QueryPerformanceInfo>> AnalyzeSlowQueriesAsync(int top = 10, CancellationToken cancellationToken = default)
    {
        // 기본 인스턴스 해시 사용 (필요 시 파라미터화)
        string instanceHash = "Default"; 

        var sql = $@"
            SELECT TOP (@Top)
                st.text AS QueryText,
                qs.execution_count AS ExecutionCount,
                qs.total_worker_time AS TotalWorkerTime,
                qs.total_elapsed_time AS TotalElapsedTime,
                (qs.total_elapsed_time / qs.execution_count) AS AvgElapsedTime,
                qs.last_elapsed_time AS LastElapsedTime,
                qs.last_execution_time AS LastExecutionTime
            FROM sys.dm_exec_query_stats AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
            ORDER BY qs.total_elapsed_time DESC;";

        var parameters = new { Top = top };

        var stream = _executor.QueryAsync<object, QueryPerformanceInfo>(
            sql, 
            parameters,
            instanceHash,
            System.Data.CommandType.Text,
            DbExecutionOptions.Default,
            cancellationToken);

        var list = new List<QueryPerformanceInfo>();
        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            list.Add(item);
        }

        return list;
    }
}

#endregion