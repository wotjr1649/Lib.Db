// ============================================================================
// 파일: Lib.Db/Extensions/LibDbHealthCheckExtensions.cs
// 설명: Lib.Db HealthCheck 확장 메서드
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Lib.Db HealthCheck 확장 메서드입니다.
/// </summary>
public static class LibDbHealthCheckExtensions
{
    /// <summary>
    /// SQL DB 헬스 체크를 등록합니다.
    /// <para>
    /// <b>[특징]</b> Throttling으로 과도한 DB 호출 방지 (최소 1초 간격)
    /// </para>
    /// </summary>
    /// <param name="builder">헬스 체크 빌더</param>
    /// <param name="name">헬스 체크 이름 (기본: sql_db)</param>
    /// <param name="tags">태그 목록</param>
    /// <returns>헬스 체크 빌더</returns>
    public static IHealthChecksBuilder AddLibDbHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "sql_db",
        params string[] tags)
    {
        return builder.AddCheck<ThrottledDbHealthCheck>(
            name,
            tags: tags.Length > 0 ? tags: new[] { "db", "ready" });
    }

    /// <summary>
    /// Throttled DB HealthCheck 구현체
    /// <para>
    /// 실제 SELECT 1을 수행하되, 과도한 호출을 방지합니다.
    /// </para>
    /// </summary>
    private sealed class ThrottledDbHealthCheck : IHealthCheck
    {
        private static HealthCheckResult s_lastResult =
            HealthCheckResult.Healthy("Initial State");
        private static long s_lastCheckTick;
        private static readonly long s_throttleTicks =
            TimeSpan.FromSeconds(1).Ticks; // 최소 1초 간격

        private readonly IDbConnectionFactory _connFactory;
        private readonly LibDbOptions _options;
        private readonly object _lock = new();

        public ThrottledDbHealthCheck(
            IDbConnectionFactory connFactory,
            LibDbOptions options)
        {
            _connFactory = connFactory;
            _options = options;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken ct = default)
        {
            long currentTick = DateTime.UtcNow.Ticks;
            long lastTick = Interlocked.Read(ref s_lastCheckTick);

            // 1. 스로틀링: 최근 검사 결과가 유효하면 재사용
            if (currentTick - lastTick < s_throttleTicks)
            {
                return s_lastResult;
            }

            // 2. 실제 DB 검사 (락을 통해 중복 실행 방지)
            if (Monitor.TryEnter(_lock))
            {
                try
                {
                    // 더블 체크
                    if (DateTime.UtcNow.Ticks - Interlocked.Read(ref s_lastCheckTick) < s_throttleTicks)
                        return s_lastResult;

                    // 첫 번째 연결 문자열 사용
                    var firstInstance = _options.ConnectionStrings.Keys.FirstOrDefault()
                        ?? throw new InvalidOperationException("HealthCheck: 설정된 DB 인스턴스가 없습니다.");

                    await using var conn = await _connFactory.CreateConnectionAsync(firstInstance, ct).ConfigureAwait(false);
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    cmd.CommandTimeout = 2; // 2초 초과 시 실패 간주
                    await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

                    s_lastResult = HealthCheckResult.Healthy("DB Connection OK");
                    Interlocked.Exchange(ref s_lastCheckTick, DateTime.UtcNow.Ticks);
                }
                catch (Exception ex)
                {
                    s_lastResult = HealthCheckResult.Unhealthy($"DB Connection Failed: {ex.Message}", ex);
                    Interlocked.Exchange(ref s_lastCheckTick, DateTime.UtcNow.Ticks);
                }
                finally
                {
                    Monitor.Exit(_lock);
                }
            }

            return s_lastResult;
        }
    }
}
