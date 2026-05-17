// ============================================================================
// 파일: Infrastructure/TestConnectionStrings.cs
// 설명: DB 통합 테스트용 연결 문자열 해석 헬퍼
// 대상: .NET 10 / C# 14
// ============================================================================

using Microsoft.Data.SqlClient;
using Xunit.Sdk;

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// DB 통합 테스트에서 연결 문자열을 코드에 하드코딩하지 않도록 구성 값을 해석한다.
/// </summary>
public static class TestConnectionStrings
{
    public const string Verification = "Verification";
    public const string Sorter = "Sorter";

    private const string EnvironmentVariablePrefix = "LIBDB_TEST_CONNECTION_";
    private const string AllowSchemaInitEnvironmentVariable = "LIBDB_TEST_ALLOW_SCHEMA_INIT";
    private static readonly char[] s_catalogTokenSeparators = ['_', '-', '.', ' '];
    private static readonly string[] s_knownNames = [Verification, Sorter];

    /// <summary>
    /// 테스트 설정 파일과 환경 변수를 함께 읽는 기본 구성 객체를 생성한다.
    /// </summary>
    public static IConfigurationRoot CreateConfiguration(IReadOnlyDictionary<string, string?>? aliasEnvironment = null)
    {
        Dictionary<string, string?> aliases = [];

        foreach (string name in s_knownNames)
        {
            string aliasName = GetAliasEnvironmentVariableName(name);
            string? value = aliasEnvironment is null
                ? Environment.GetEnvironmentVariable(aliasName)
                : GetAliasValue(aliasEnvironment, aliasName);

            if (!string.IsNullOrWhiteSpace(value))
                aliases[$"ConnectionStrings:{name}"] = value;
        }

        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(aliases)
            .Build();
    }

    /// <summary>
    /// 지정한 이름의 연결 문자열을 반환하거나 DB 통합 테스트를 건너뛴다.
    /// </summary>
    public static string Require(IConfiguration configuration, string name)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? value = configuration.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        string alias = GetAliasEnvironmentVariableName(name);
        value = Environment.GetEnvironmentVariable(alias);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        throw SkipException.ForSkip(
            $"Connection string '{name}' is not configured. Set 'ConnectionStrings__{name}' or '{alias}' before running database integration tests.");
    }

    /// <summary>
    /// 연결 문자열에 제한된 최대 풀 크기를 적용한다.
    /// </summary>
    public static string WithMaxPoolSize(string connectionString, int maxPoolSize)
    {
        SqlConnectionStringBuilder builder = new(connectionString)
        {
            MaxPoolSize = maxPoolSize
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// 테스트 스키마 초기화가 안전한 테스트 DB에서만 실행되도록 검증한다.
    /// </summary>
    public static void RequireSafeSchemaInitialization(IConfiguration configuration, string name)
    {
        string connectionString = Require(configuration, name);
        if (IsSchemaInitializationAllowed(connectionString) || IsExplicitSchemaInitializationAllowed())
            return;

        throw SkipException.ForSkip(
            $"Connection string '{name}' is not marked as a test database. Use a database name containing a separated TEST/LOCAL/DEV token or set '{AllowSchemaInitEnvironmentVariable}=true' for explicit schema initialization.");
    }

    /// <summary>
    /// 실제 DB에 연결하지 않는 옵션/파싱 테스트용 비밀값 없는 연결 문자열을 만든다.
    /// </summary>
    public static string Placeholder(string databaseName)
        => $"Server=localhost;Database={databaseName};Integrated Security=True;TrustServerCertificate=True;Encrypt=False;";

    private static string GetAliasEnvironmentVariableName(string name)
        => EnvironmentVariablePrefix + name.Replace(':', '_').Replace('-', '_').ToUpperInvariant();

    private static bool IsExplicitSchemaInitializationAllowed()
        => bool.TryParse(Environment.GetEnvironmentVariable(AllowSchemaInitEnvironmentVariable), out bool allowed) && allowed;

    private static bool IsSchemaInitializationAllowed(string connectionString)
    {
        try
        {
            string catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            return IsSafeSchemaCatalogName(catalog);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafeSchemaCatalogName(string catalog)
    {
        if (string.IsNullOrWhiteSpace(catalog))
            return false;

        string[] tokens = catalog.Split(
            s_catalogTokenSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string token in tokens)
        {
            if (string.Equals(token, "TEST", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "LOCAL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "DEV", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetAliasValue(IReadOnlyDictionary<string, string?> values, string name)
    {
        if (values.TryGetValue(name, out string? value))
            return value;

        foreach ((string key, string? item) in values)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }
}
