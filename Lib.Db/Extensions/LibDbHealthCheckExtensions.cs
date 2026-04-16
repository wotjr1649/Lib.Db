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
            tags: tags.Length > 0 ? tags : new[] { "db", "ready" });
    }

    /// <summary>
    /// Throttled DB HealthCheck 구현체
    /// <para>
    /// 실제 SELECT 1을 수행하되, 과도한 호출을 방지합니다.
    /// </para>
    /// <para>
    /// <b>[설계 의도]</b> 이전 구현에서 static 필드를 사용하면 여러 인스턴스가 DI에 등록될 경우
    /// 인스턴스 Lock이 static 상태를 보호하지 못하는 문제가 있었습니다.
    /// 모든 상태를 인스턴스 필드로 변경하여 각 인스턴스가 독립적인 스로틀링 상태를 유지합니다.
    /// 스로틀 간격은 <see cref="LibDbOptions.HealthCheckThrottleSeconds"/>에서 읽어 설정에 따라 동적으로 결정됩니다.
    /// </para>
    /// </summary>
    private sealed class ThrottledDbHealthCheck : IHealthCheck
    {
        // 인스턴스 필드로 변경: static → instance (동시 다중 인스턴스 시 Lock이 상태를 올바르게 보호)
        // [BUG-12 수정] HealthCheckResult는 struct이므로 volatile 적용 불가(CS0677).
        // 스로틀 경로의 stale read는 HealthCheck 캐싱 특성상 허용 가능하며,
        // 쓰기는 _lock 내에서만 수행하여 일관성을 보장합니다.
        private HealthCheckResult _lastResult =
            HealthCheckResult.Healthy("Initial State");
        private long _lastCheckTick;
        private readonly long _throttleTicks;

        private readonly IDbConnectionFactory _connFactory;
        private readonly LibDbOptions _options;
        private readonly Lock _lock = new();

        public ThrottledDbHealthCheck(
            IDbConnectionFactory connFactory,
            LibDbOptions options)
        {
            _connFactory = connFactory;
            _options = options;
            // LibDbOptions.HealthCheckThrottleSeconds 설정값 반영 (기본 1초)
            _throttleTicks = TimeSpan.FromSeconds(options.HealthCheckThrottleSeconds).Ticks;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken ct = default)
        {
            long currentTick = DateTime.UtcNow.Ticks;
            long lastTick = Interlocked.Read(ref _lastCheckTick);

            // 1. 스로틀링: 최근 검사 결과가 유효하면 재사용
            if (currentTick - lastTick < _throttleTicks)
            {
                return _lastResult;
            }

            // 2. 실제 DB 검사 (Lock을 통해 중복 실행 방지)
            if (_lock.TryEnter())
            {
                try
                {
                    // 더블 체크
                    if (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastCheckTick) < _throttleTicks)
                        return _lastResult;

                    // 첫 번째 연결 문자열 사용
                    string? firstInstance = _options.ConnectionStrings.Keys.FirstOrDefault()
                        ?? throw new InvalidOperationException("HealthCheck: 설정된 DB 인스턴스가 없습니다.");

                    await using SqlConnection conn = await _connFactory.CreateConnectionAsync(firstInstance, ct).ConfigureAwait(false);
                    await using SqlCommand cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    cmd.CommandTimeout = _options.HealthCheckTimeoutSeconds;
                    await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

                    _lastResult = HealthCheckResult.Healthy("DB Connection OK");
                    Interlocked.Exchange(ref _lastCheckTick, DateTime.UtcNow.Ticks);
                }
                catch (Exception ex)
                {
                    _lastResult = HealthCheckResult.Unhealthy($"DB Connection Failed: {ex.Message}", ex);
                    Interlocked.Exchange(ref _lastCheckTick, DateTime.UtcNow.Ticks);
                }
                finally
                {
                    _lock.Exit();
                }
            }

            return _lastResult;
        }
    }
}
