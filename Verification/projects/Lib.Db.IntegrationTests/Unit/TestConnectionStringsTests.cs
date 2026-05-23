// ============================================================================
// 파일: Unit/TestConnectionStringsTests.cs
// 설명: 테스트 연결 문자열 구성 해석 유닛 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Xunit.Sdk;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class TestConnectionStringsTests
{
    private static readonly string[] s_connectionEnvironmentVariables =
    [
        "ConnectionStrings__Verification",
        "ConnectionStrings__Sorter",
        "ConnectionStrings__Stress",
        "ConnectionStrings__Chaos",
        "ConnectionStrings__Benchmark",
        "LIBDB_TEST_CONNECTION_VERIFICATION",
        "LIBDB_TEST_CONNECTION_SORTER",
        "LIBDB_TEST_CONNECTION_STRESS",
        "LIBDB_TEST_CONNECTION_CHAOS",
        "LIBDB_TEST_CONNECTION_BENCHMARK"
    ];

    [Fact]
    public void TCS01_CreateConfiguration_ShouldNormalizeAliasEnvironmentVariables()
    {
        const string verificationAlias = "LIBDB_TEST_CONNECTION_VERIFICATION";
        const string sorterAlias = "LIBDB_TEST_CONNECTION_SORTER";
        string verification = TestConnectionStrings.Placeholder("VerificationAlias");
        string sorter = TestConnectionStrings.Placeholder("SorterAlias");

        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [verificationAlias] = verification,
            [sorterAlias] = sorter
        };

        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        configuration.GetConnectionString(TestConnectionStrings.Verification).Should().Be(verification);
        configuration.GetConnectionString(TestConnectionStrings.Sorter).Should().Be(sorter);
    }

    [Fact]
    public void TCS02_RequireSafeSchemaInitialization_ShouldAllowTestDatabaseName()
    {
        const string verificationAlias = "LIBDB_TEST_CONNECTION_VERIFICATION";
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [verificationAlias] = TestConnectionStrings.Placeholder("LIBDB_VERIFICATION_TEST")
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Verification);

        act.Should().NotThrow();
    }

    [Fact]
    public void TCS03_RequireSafeSchemaInitialization_ShouldSkipNonTestDatabaseName()
    {
        const string sorterAlias = "LIBDB_TEST_CONNECTION_SORTER";
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [sorterAlias] = TestConnectionStrings.Placeholder("ProductionDb")
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Sorter);

        act.Should().Throw<SkipException>()
            .WithMessage("*local test database*");
    }

    [Fact]
    public void TCS04_RequireSafeSchemaInitialization_ShouldRejectDevSubstringWithoutTokenBoundary()
    {
        const string verificationAlias = "LIBDB_TEST_CONNECTION_VERIFICATION";
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [verificationAlias] = TestConnectionStrings.Placeholder("ProdDevelopmentArchive")
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Verification);

        act.Should().Throw<SkipException>()
            .WithMessage("*TEST/LOCAL/DEV token*");
    }

    [Fact]
    public void TCS05_RequireSafeSchemaInitialization_ShouldAllowSeparatedLocalToken()
    {
        const string verificationAlias = "LIBDB_TEST_CONNECTION_VERIFICATION";
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            [verificationAlias] = TestConnectionStrings.Placeholder("LIBDB-LOCAL-VERIFICATION")
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Verification);

        act.Should().NotThrow();
    }

    [Fact]
    public void TCS06_CreateConfiguration_ShouldGenerateLocalSqlConnectionFromAppsettingsWithoutFilePassword()
    {
        const string passwordEnvironmentVariable = "LIBDB_TEST_GENERATED_PASSWORD_UNIT";
        WithIsolatedConnectionEnvironment(() => WithEnvironmentVariable(passwordEnvironmentVariable, "unit-only-value", () =>
        {
            IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(
                configurationOverrides: LocalSqlOverrides(
                    passwordEnvironmentVariable,
                    dataSource: "tcp:127.0.0.1,1433"));

            TestConnectionStrings.TryGet(configuration, TestConnectionStrings.Verification, out string connectionString)
                .Should()
                .BeTrue();

            SqlConnectionStringBuilder builder = new(connectionString);
            builder.DataSource.Should().Be("tcp:127.0.0.1,1433");
            builder.InitialCatalog.Should().Be("LIBDB_VERIFICATION_TEST");
            builder.UserID.Should().Be("SA");
            builder.Password.Should().NotBeNullOrWhiteSpace();
        }));
    }

    [Fact]
    public void TCS07_CreateConfiguration_ShouldNotGenerateConnectionForRemoteDataSource()
    {
        const string passwordEnvironmentVariable = "LIBDB_TEST_GENERATED_PASSWORD_UNIT";
        WithIsolatedConnectionEnvironment(() => WithEnvironmentVariable(passwordEnvironmentVariable, "unit-only-value", () =>
        {
            IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(
                configurationOverrides: LocalSqlOverrides(
                    passwordEnvironmentVariable,
                    dataSource: "sql.example.internal"));

            TestConnectionStrings.TryGet(configuration, TestConnectionStrings.Verification, out _)
                .Should()
                .BeFalse();
        }));
    }

    [Fact]
    public void TCS08_CreateConfiguration_ShouldRejectPasswordStoredInAppsettings()
    {
        Dictionary<string, string?> overrides = LocalSqlOverrides(
            "LIBDB_TEST_GENERATED_PASSWORD_UNIT",
            dataSource: "127.0.0.1");
        overrides["LibDbTest:SqlServer:Password"] = "forbidden";

        Action act = () => WithIsolatedConnectionEnvironment(() =>
            TestConnectionStrings.CreateConfiguration(configurationOverrides: overrides));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LibDbTest:SqlServer:Password*PasswordEnvironmentVariable*");
    }

    [Fact]
    public void TCS09_CreateConfiguration_ShouldPreferAliasEnvironmentOverGeneratedLocalSqlConnection()
    {
        const string passwordEnvironmentVariable = "LIBDB_TEST_GENERATED_PASSWORD_UNIT";
        string aliasConnection = TestConnectionStrings.Placeholder("LIBDB_ALIAS_TEST");
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["LIBDB_TEST_CONNECTION_VERIFICATION"] = aliasConnection
        };

        WithEnvironmentVariable(passwordEnvironmentVariable, "unit-only-value", () =>
        {
            IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(
                aliasEnvironment: aliases,
                configurationOverrides: LocalSqlOverrides(
                    passwordEnvironmentVariable,
                    dataSource: "127.0.0.1"));

            configuration.GetConnectionString(TestConnectionStrings.Verification).Should().Be(aliasConnection);
        });
    }

    [Fact]
    public void TCS10_CreateConfiguration_ShouldIgnoreCurrentDirectoryAppsettingsJson()
    {
        string originalDirectory = Directory.GetCurrentDirectory();
        string tempDirectory = Path.Combine(Path.GetTempPath(), "libdb-cwd-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "ConnectionStrings": {
                    "Verification": "Server=remote.example;Database=LIBDB_REMOTE_TEST;Integrated Security=True;"
                  }
                }
                """);

            Directory.SetCurrentDirectory(tempDirectory);
            IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration();

            if (TestConnectionStrings.TryGet(configuration, TestConnectionStrings.Verification, out string connectionString))
            {
                SqlConnectionStringBuilder builder = new(connectionString);
                builder.DataSource.Should().NotBe("remote.example");
                builder.InitialCatalog.Should().NotBe("LIBDB_REMOTE_TEST");
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void TCS11_RequireSafeSchemaInitialization_ShouldRejectRemoteDataSourceEvenForTestCatalog()
    {
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["LIBDB_TEST_CONNECTION_VERIFICATION"] =
                "Server=tcp:sql.example.internal;Database=LIBDB_REMOTE_TEST;Integrated Security=True;"
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.RequireSafeSchemaInitialization(
            configuration,
            TestConnectionStrings.Verification);

        act.Should().Throw<SkipException>()
            .WithMessage("*local test database*");
    }

    [Fact]
    public void TCS12_CreateConfiguration_ShouldRejectPasswordInsideConnectionStringsOverrides()
    {
        SqlConnectionStringBuilder builder = new()
        {
            DataSource = "127.0.0.1",
            InitialCatalog = "LIBDB_VERIFICATION_TEST",
            UserID = "SA",
            Password = "forbidden"
        };
        Dictionary<string, string?> overrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Verification"] = builder.ConnectionString
        };

        Action act = () => TestConnectionStrings.CreateConfiguration(configurationOverrides: overrides);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Verification*password*");
    }

    [Fact]
    public void TCS13_CreateConfiguration_ShouldPublishGeneratedNamesForLibDbRegistration()
    {
        const string passwordEnvironmentVariable = "LIBDB_TEST_GENERATED_PASSWORD_UNIT";
        WithEnvironmentVariable(passwordEnvironmentVariable, "unit-only-value", () =>
        {
            IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(
                configurationOverrides: LocalSqlOverrides(
                    passwordEnvironmentVariable,
                    dataSource: "127.0.0.1"));

            configuration.GetSection("LibDb:ConnectionStringNames")
                .GetChildren()
                .Select(static section => section.Value)
                .Should()
                .Contain(TestConnectionStrings.Verification);
        });
    }

    [Fact]
    public void TCS14_Require_ShouldRejectRemoteFullConnectionBeforeLiveDbExecution()
    {
        Dictionary<string, string?> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["LIBDB_TEST_CONNECTION_VERIFICATION"] =
                "Server=tcp:sql.example.internal;Database=LIBDB_REMOTE_TEST;Integrated Security=True;"
        };
        IConfigurationRoot configuration = TestConnectionStrings.CreateConfiguration(aliases);

        Action act = () => TestConnectionStrings.Require(configuration, TestConnectionStrings.Verification);

        act.Should().Throw<SkipException>()
            .WithMessage("*local test database*");
    }

    private static Dictionary<string, string?> LocalSqlOverrides(
        string passwordEnvironmentVariable,
        string dataSource)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["LibDbTest:SqlServer:DataSource"] = dataSource,
            ["LibDbTest:SqlServer:UserId"] = "SA",
            ["LibDbTest:SqlServer:PasswordEnvironmentVariable"] = passwordEnvironmentVariable,
            ["LibDbTest:SqlServer:Encrypt"] = "False",
            ["LibDbTest:SqlServer:TrustServerCertificate"] = "True",
            ["LibDbTest:Databases:Verification"] = "LIBDB_VERIFICATION_TEST"
        };

    private static void WithEnvironmentVariable(string name, string value, Action action)
    {
        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    private static void WithIsolatedConnectionEnvironment(Action action)
    {
        Dictionary<string, string?> previous = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in s_connectionEnvironmentVariables)
        {
            previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }

        try
        {
            action();
        }
        finally
        {
            foreach ((string name, string? value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
