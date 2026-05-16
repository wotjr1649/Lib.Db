// ============================================================================
// 파일: Lib.Db/Configuration/LibDbOptionsValidator.cs
// 역할: LibDbOptions 설정 값 검증 (IValidateOptions 구현)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Lib.Db.Configuration;

/// <summary>
/// <see cref="LibDbOptions"/> 설정 값의 무결성을 검증합니다.
/// <para>
/// <b>[적용 시점]</b><br/>
/// DI 컨테이너에서 Options를 해결할 때 자동으로 실행됩니다.
/// </para>
/// </summary>
internal sealed class LibDbOptionsValidator : IValidateOptions<LibDbOptions>
{
    public ValidateOptionsResult Validate(string? name, LibDbOptions options)
    {
        List<string> errors = new List<string>(capacity: 10);

        // [1] ConnectionStringNames 검증
        if (options.ConnectionStringNames is not { Count: > 0 })
        {
            errors.Add("최소 1개 이상의 연결 문자열 이름이 필요합니다.");
        }
        else
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < options.ConnectionStringNames.Count; i++)
            {
                string csName = options.ConnectionStringNames[i];

                // 공백/빈 문자열 검증
                if (string.IsNullOrWhiteSpace(csName))
                {
                    errors.Add($"ConnectionStringNames[{i}]가 비어있거나 공백입니다.");
                    continue;
                }

                // 중복 검증
                if (!seen.Add(csName))
                {
                    errors.Add($"ConnectionStringNames에 중복 키 '{csName}'이(가) 있습니다.");
                    continue;
                }

                // ConnectionStrings에 키 존재 검증
                if (!options.ConnectionStrings.TryGetValue(csName, out string? connStr))
                {
                    string registeredKeys = string.Join(", ", options.ConnectionStrings.Keys);
                    errors.Add($"ConnectionStringNames의 '{csName}'이(가) ConnectionStrings에 없습니다. 등록된 키: [{registeredKeys}]");
                    continue;
                }

                // 빈 연결 문자열 검증
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    errors.Add($"'{csName}'의 연결 문자열이 비어있습니다.");
                    continue;
                }

                // 연결 문자열 형식 및 보안 프로필 검증
                try
                {
                    SqlConnectionStringBuilder builder = new(connStr);
                    ValidateConnectionSecurityProfile(options, csName, builder, errors);
                }
                catch (ArgumentException)
                {
                    errors.Add($"'{csName}'의 연결 문자열 형식이 잘못되었습니다.");
                }
            }
        }

        // [2] Resilience 설정 검증
        if (options.EnableResilience)
        {
            LibDbOptions.ResilienceOptions r = options.Resilience;

            if (r.MaxRetryCount < 0)
                errors.Add("Resilience.MaxRetryCount는 0 이상이어야 합니다.");

            if (r.BaseRetryDelayMs < 0)
                errors.Add("Resilience.BaseRetryDelayMs는 0 이상이어야 합니다.");

            if (r.MaxRetryDelayMs < r.BaseRetryDelayMs)
                errors.Add($"Resilience.MaxRetryDelayMs({r.MaxRetryDelayMs})는 BaseRetryDelayMs({r.BaseRetryDelayMs}) 이상이어야 합니다.");

            if (r.CircuitBreakerFailureRatio is < 0.0 or > 1.0)
                errors.Add($"Resilience.CircuitBreakerFailureRatio({r.CircuitBreakerFailureRatio})는 0.0~1.0 범위여야 합니다.");

            if (r.CircuitBreakerThreshold <= 0)
                errors.Add("Resilience.CircuitBreakerThreshold는 1 이상이어야 합니다.");

            if (r.CircuitBreakerBreakDurationMs <= 0)
                errors.Add("Resilience.CircuitBreakerBreakDurationMs는 0보다 커야 합니다.");
        }

        // [3] Chaos 설정 검증
        if (options.Chaos.Enabled)
        {
            ChaosOptions c = options.Chaos;

            if (c.MinLatencyMs > c.MaxLatencyMs)
                errors.Add($"Chaos.MinLatencyMs({c.MinLatencyMs})는 MaxLatencyMs({c.MaxLatencyMs}) 이하여야 합니다.");

            if (c.ExceptionRate is < 0.0 or > 1.0)
                errors.Add($"Chaos.ExceptionRate({c.ExceptionRate})는 0.0~1.0 범위여야 합니다.");

            if (c.LatencyRate is < 0.0 or > 1.0)
                errors.Add($"Chaos.LatencyRate({c.LatencyRate})는 0.0~1.0 범위여야 합니다.");
        }

        // [4] SharedMemoryCache 검증
        if (options.SharedMemoryCache != null)
        {
            if (options.SharedMemoryCache.MaxCacheSizeBytes <= 0)
                errors.Add("SharedMemoryCache.MaxCacheSizeBytes는 0보다 커야 합니다.");
        }

        // [5] (T6-6) MarsPolicy.ForceEnable: 연결 문자열 파싱 가능 여부 사전 검증
        // PostConfigure에서 MARS 자동 주입 시 파싱 실패가 발생하면 등록 자체가 불가하므로,
        // 유효성 검사 단계에서 미리 파싱 오류를 감지합니다.
        if (options.Mars == MarsPolicy.ForceEnable)
        {
            foreach (KeyValuePair<string, string> kvp in options.ConnectionStrings)
            {
                try
                {
                    _ = new SqlConnectionStringBuilder(kvp.Value);
                }
                catch
                {
                    errors.Add($"ConnectionString '{kvp.Key}' 파싱 실패 (MARS 자동 주입 불가).");
                }
            }
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateConnectionSecurityProfile(
        LibDbOptions options,
        string connectionName,
        SqlConnectionStringBuilder builder,
        List<string> errors)
    {
        if (options.ConnectionSecurityProfile != ConnectionSecurityProfile.Production)
            return;

        string encrypt = builder.Encrypt.ToString();
        if (string.Equals(encrypt, "False", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(encrypt, "Optional", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"ConnectionString '{connectionName}' production security profile requires Encrypt=True/Mandatory/Strict.");
        }

        if (builder.TrustServerCertificate &&
            !options.AllowProductionTrustServerCertificateWaiver)
        {
            errors.Add(
                $"ConnectionString '{connectionName}' production security profile does not allow TrustServerCertificate=True without an explicit waiver.");
        }

        if (!builder.IntegratedSecurity &&
            string.Equals(builder.UserID, "sa", StringComparison.OrdinalIgnoreCase) &&
            !options.AllowProductionSaLoginWaiver)
        {
            errors.Add(
                $"ConnectionString '{connectionName}' production security profile does not allow privileged SQL login without an explicit waiver.");
        }
    }
}
