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
    public const string Stress = "Stress";
    public const string Chaos = "Chaos";
    public const string Benchmark = "Benchmark";

    private const string EnvironmentVariablePrefix = "LIBDB_TEST_CONNECTION_";
    private const string AllowSchemaInitEnvironmentVariable = "LIBDB_TEST_ALLOW_SCHEMA_INIT";
    private const string LocalSqlServerSection = "LibDbTest:SqlServer";
    private const string LocalSqlDatabaseSection = "LibDbTest:Databases";
    private static readonly char[] s_catalogTokenSeparators = ['_', '-', '.', ' '];
    private static readonly string[] s_knownNames = [Verification, Sorter, Stress, Chaos, Benchmark];

    /// <summary>
    /// 테스트 설정 파일과 환경 변수를 함께 읽는 기본 구성 객체를 생성한다.
    /// </summary>
    public static IConfigurationRoot CreateConfiguration(
        IReadOnlyDictionary<string, string?>? aliasEnvironment = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        AssertNoFileBackedConnectionStringPasswords(configurationOverrides);

        Dictionary<string, string?> baseValues = configurationOverrides is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(configurationOverrides, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string?> aliases = BuildAliasConfiguration(aliasEnvironment);
        Dictionary<string, string?> preliminaryValues = new(baseValues, StringComparer.OrdinalIgnoreCase);
        AddValues(preliminaryValues, aliases);

        IConfigurationRoot preliminary = BuildConfiguration(preliminaryValues);
        Dictionary<string, string?> dynamicValues = new(baseValues, StringComparer.OrdinalIgnoreCase);
        AddValues(dynamicValues, aliases);

        AddLocalSqlServerConnectionStrings(preliminary, dynamicValues);

        IConfigurationRoot generated = BuildConfiguration(dynamicValues);
        string[] configuredNames = BuildConfiguredConnectionNames(generated);
        for (int i = 0; i < configuredNames.Length; i++)
            dynamicValues[$"LibDb:ConnectionStringNames:{i}"] = configuredNames[i];

        return BuildConfiguration(dynamicValues);
    }

    /// <summary>
    /// 지정한 이름의 연결 문자열이 실제로 구성되었는지 확인한다.
    /// </summary>
    public static bool TryGet(IConfiguration configuration, string name, out string connectionString)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? value = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            string alias = GetAliasEnvironmentVariableName(name);
            value = Environment.GetEnvironmentVariable(alias);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            connectionString = string.Empty;
            return false;
        }

        connectionString = value;
        return true;
    }

    /// <summary>
    /// 지정한 이름의 연결 문자열을 반환하거나 DB 통합 테스트를 건너뛴다.
    /// </summary>
    public static string Require(IConfiguration configuration, string name)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? value = configuration.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(value))
            return RequireLocalTestDatabase(value, name);

        string alias = GetAliasEnvironmentVariableName(name);
        value = Environment.GetEnvironmentVariable(alias);
        if (!string.IsNullOrWhiteSpace(value))
            return RequireLocalTestDatabase(value, name);

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
            $"Connection string '{name}' is not marked as a local test database. Use a local SQL Server data source and a database name containing a separated TEST/LOCAL/DEV token, or set '{AllowSchemaInitEnvironmentVariable}=true' for explicit schema initialization.");
    }

    /// <summary>
    /// 실제 DB에 연결하지 않는 옵션/파싱 테스트용 비밀값 없는 연결 문자열을 만든다.
    /// </summary>
    public static string Placeholder(string databaseName)
        => $"Server=localhost;Database={databaseName};Integrated Security=True;TrustServerCertificate=True;Encrypt=False;";

    private static string GetAliasEnvironmentVariableName(string name)
        => EnvironmentVariablePrefix + name.Replace(':', '_').Replace('-', '_').ToUpperInvariant();

    private static Dictionary<string, string?> BuildAliasConfiguration(IReadOnlyDictionary<string, string?>? aliasEnvironment)
    {
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in s_knownNames)
        {
            string aliasName = GetAliasEnvironmentVariableName(name);
            string? value = aliasEnvironment is null
                ? Environment.GetEnvironmentVariable(aliasName)
                : GetAliasValue(aliasEnvironment, aliasName);

            if (!string.IsNullOrWhiteSpace(value))
                aliases[$"ConnectionStrings:{name}"] = value;
        }

        return aliases;
    }

    private static IConfigurationRoot BuildConfiguration(IReadOnlyDictionary<string, string?> values)
        => new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(values)
            .Build();

    private static string[] BuildConfiguredConnectionNames(IConfiguration configuration)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (IConfigurationSection child in configuration.GetSection("LibDb:ConnectionStringNames").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
                names.Add(child.Value);
        }

        foreach (string name in s_knownNames)
        {
            if (TryGet(configuration, name, out _))
                names.Add(name);
        }

        return names.ToArray();
    }

    private static void AddLocalSqlServerConnectionStrings(
        IConfiguration configuration,
        Dictionary<string, string?> dynamicValues)
    {
        foreach (string name in s_knownNames)
        {
            string key = $"ConnectionStrings:{name}";
            if (dynamicValues.ContainsKey(key) || !string.IsNullOrWhiteSpace(configuration.GetConnectionString(name)))
                continue;

            if (TryBuildLocalSqlServerConnectionString(configuration, name, out string connectionString))
                dynamicValues[key] = connectionString;
        }
    }

    private static bool TryBuildLocalSqlServerConnectionString(
        IConfiguration configuration,
        string name,
        out string connectionString)
    {
        string? database = configuration[$"{LocalSqlDatabaseSection}:{name}"];
        if (string.IsNullOrWhiteSpace(database))
        {
            connectionString = string.Empty;
            return false;
        }

        bool integratedSecurity = ReadBoolean(configuration, $"{LocalSqlServerSection}:IntegratedSecurity", defaultValue: false);
        string? userId = configuration[$"{LocalSqlServerSection}:UserId"];
        if (!string.IsNullOrWhiteSpace(configuration[$"{LocalSqlServerSection}:Password"]))
            throw new InvalidOperationException(
                $"{LocalSqlServerSection}:Password must not be configured. Use {LocalSqlServerSection}:PasswordEnvironmentVariable instead.");

        string? password = null;
        string? passwordEnvironmentVariable = configuration[$"{LocalSqlServerSection}:PasswordEnvironmentVariable"];

        if (!integratedSecurity && !string.IsNullOrWhiteSpace(passwordEnvironmentVariable))
            password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable);

        string dataSource = ReadString(configuration, $"{LocalSqlServerSection}:DataSource", "127.0.0.1");
        if (!IsLocalDataSource(dataSource))
        {
            connectionString = string.Empty;
            return false;
        }

        if (!integratedSecurity && (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password)))
        {
            connectionString = string.Empty;
            return false;
        }

        SqlConnectionStringBuilder builder = new()
        {
            DataSource = dataSource,
            InitialCatalog = database,
            IntegratedSecurity = integratedSecurity,
            MultipleActiveResultSets = ReadBoolean(configuration, $"{LocalSqlServerSection}:MultipleActiveResultSets", defaultValue: true),
            TrustServerCertificate = ReadBoolean(configuration, $"{LocalSqlServerSection}:TrustServerCertificate", defaultValue: true),
            ConnectTimeout = ReadInt(configuration, $"{LocalSqlServerSection}:ConnectTimeoutSeconds", defaultValue: 15),
            ApplicationName = ReadString(configuration, $"{LocalSqlServerSection}:ApplicationName", "Lib.Db.IntegrationTests")
        };

        string encrypt = ReadString(configuration, $"{LocalSqlServerSection}:Encrypt", "False");
        builder["Encrypt"] = encrypt;

        if (!integratedSecurity)
        {
            builder.UserID = userId;
            builder.Password = password;
        }

        connectionString = builder.ConnectionString;
        return true;
    }

    private static string ReadString(IConfiguration configuration, string key, string defaultValue)
    {
        string? value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static bool ReadBoolean(IConfiguration configuration, string key, bool defaultValue)
        => bool.TryParse(configuration[key], out bool value) ? value : defaultValue;

    private static int ReadInt(IConfiguration configuration, string key, int defaultValue)
        => int.TryParse(configuration[key], out int value) ? value : defaultValue;

    private static bool IsLocalDataSource(string dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
            return false;

        string normalized = dataSource.Trim();
        if (normalized.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..];

        string host = normalized.Split([',', '\\'], 2, StringSplitOptions.TrimEntries)[0];
        return string.Equals(host, ".", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "(local)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddValues(
        Dictionary<string, string?> target,
        IReadOnlyDictionary<string, string?> values)
    {
        foreach ((string key, string? value) in values)
            target[key] = value;
    }

    private static bool IsExplicitSchemaInitializationAllowed()
        => bool.TryParse(Environment.GetEnvironmentVariable(AllowSchemaInitEnvironmentVariable), out bool allowed) && allowed;

    private static bool IsSchemaInitializationAllowed(string connectionString)
    {
        try
        {
            SqlConnectionStringBuilder builder = new(connectionString);
            return IsLocalDataSource(builder.DataSource) && IsSafeSchemaCatalogName(builder.InitialCatalog);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string RequireLocalTestDatabase(string connectionString, string name)
    {
        if (IsSchemaInitializationAllowed(connectionString))
            return connectionString;

        throw SkipException.ForSkip(
            $"Connection string '{name}' is not marked as a local test database. Use a local SQL Server data source and a database name containing a separated TEST/LOCAL/DEV token.");
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

    private static void AssertNoFileBackedConnectionStringPasswords(
        IReadOnlyDictionary<string, string?>? configurationOverrides)
    {
        IConfigurationRoot jsonConfiguration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        foreach (string name in s_knownNames)
        {
            if (ContainsPasswordToken(jsonConfiguration.GetConnectionString(name)))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{name} must not contain a password in appsettings.json. Use {EnvironmentVariablePrefix}{name.ToUpperInvariant()} or {LocalSqlServerSection}:PasswordEnvironmentVariable instead.");
            }
        }

        if (configurationOverrides is null)
            return;

        foreach ((string key, string? value) in configurationOverrides)
        {
            if (key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) &&
                ContainsPasswordToken(value))
            {
                throw new InvalidOperationException(
                    $"{key} must not contain a password in file-backed test configuration. Use environment variables instead.");
            }
        }
    }

    private static bool ContainsPasswordToken(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            SqlConnectionStringBuilder builder = new(connectionString);
            return !string.IsNullOrWhiteSpace(builder.Password);
        }
        catch (ArgumentException)
        {
            return connectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
                connectionString.Contains("Pwd=", StringComparison.OrdinalIgnoreCase);
        }
    }
}
