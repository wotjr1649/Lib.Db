// ============================================================================
// 파일: Unit/DbSessionSecurityTests.cs
// 설명: IDbSession 인스턴스 선택 보안 경계 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using Lib.Db.Contracts.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Unit;

[Trait("Category", "Unit")]
public sealed class DbSessionSecurityTests
{
    [Theory]
    [InlineData("Raw:Server=localhost;Database=TEST;Encrypt=False")]
    [InlineData("raw:Server=localhost;Database=TEST;Encrypt=False")]
    [InlineData("Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
    public async Task Use_ShouldRejectRawConnectionStringPrefix(string instanceName)
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IDbSession session = scope.ServiceProvider.GetRequiredService<IDbSession>();

        Action act = () => session.Use(instanceName);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*UseConnectionString*")
            .And.Message.Should().NotContain("placeholder");
    }

    [Theory]
    [InlineData("Raw:Server=localhost;Database=TEST;Encrypt=False")]
    [InlineData("Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
    public async Task UseSchema_ShouldRejectRawConnectionStringInstanceName(string instanceName)
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IDbSession session = scope.ServiceProvider.GetRequiredService<IDbSession>();

        Action act = () => session.UseSchema(instanceName);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*UseConnectionString*")
            .And.Message.Should().NotContain("placeholder");
    }

    [Theory]
    [InlineData("Raw:Server=localhost;Database=TEST;Encrypt=False")]
    [InlineData("Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
    public async Task BeginTransactionAsync_ShouldRejectRawConnectionStringInstanceName(string instanceName)
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IDbSession session = scope.ServiceProvider.GetRequiredService<IDbSession>();

        Func<Task> act = () => session.BeginTransactionAsync(
            instanceName,
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*UseConnectionString*")
            .WithMessage("*연결 문자열*");
    }

    [Theory]
    [InlineData("Raw:Server=localhost;Database=TEST;Encrypt=False")]
    [InlineData("Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
    public async Task BulkInsertAsync_ShouldRejectRawConnectionStringInstanceName(string instanceName)
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IDbSession session = scope.ServiceProvider.GetRequiredService<IDbSession>();

        Func<Task> act = () => session.BulkInsertAsync(
            instanceName,
            "[dbo].[Target]",
            new[] { new RawBlockRow(1) },
            ct: TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*UseConnectionString*")
            .WithMessage("*연결 문자열*");
    }

    [Fact]
    public async Task UseConnectionString_ProductionProfile_ShouldApplySecurityValidation()
    {
        await using ServiceProvider provider = CreateProvider(options =>
        {
            options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
            options.ConnectionStrings["Default"] =
                "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=False";
        });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IDbSession session = scope.ServiceProvider.GetRequiredService<IDbSession>();

        Action act = () => session.UseConnectionString(
            "Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=False;TrustServerCertificate=True");

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*production security profile*");
    }

    [Theory]
    [InlineData("Raw:Server=localhost;Database=TEST;Encrypt=False")]
    [InlineData("raw:Server=localhost;Database=TEST;Encrypt=False")]
    [InlineData("Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
    public async Task DbConnectionFactory_ShouldRejectRawConnectionStringInstanceNameBeforeOpening(string instanceName)
    {
        await using ServiceProvider provider = CreateProvider();
        IDbConnectionFactory factory = provider.GetRequiredService<IDbConnectionFactory>();

        Func<Task> act = () => factory.CreateConnectionAsync(
            instanceName,
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*UseConnectionString*")
            .WithMessage("*연결 문자열*");
    }

    [Fact]
    public async Task DbConnectionFactory_MissingInstance_ShouldRedactConnectionStringLikeInput()
    {
        await using ServiceProvider provider = CreateProvider();
        IDbConnectionFactory factory = provider.GetRequiredService<IDbConnectionFactory>();
        const string rawConnectionString =
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";

        Func<Task> act = () => factory.CreateConnectionAsync(
            rawConnectionString,
            TestContext.Current.CancellationToken);

        var assertion = await act.Should()
            .ThrowAsync<ArgumentException>();

        ArgumentException exception = assertion.Which;
        exception.Message.Should().NotContain("placeholder");
        exception.Message.Should().NotContain(rawConnectionString);
        exception.Message.Should().Contain("ConnectionString:[redacted]");
    }

    [Fact]
    public async Task DbConnectionFactory_RegisterAdHocInstance_ShouldRejectConnectionStringShapeKey()
    {
        await using ServiceProvider provider = CreateProvider();
        IDbConnectionFactory factory = provider.GetRequiredService<IDbConnectionFactory>();
        const string rawConnectionString =
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";

        Action act = () => factory.RegisterAdHocInstance(
            rawConnectionString,
            "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False");

        ArgumentException exception = act.Should()
            .Throw<ArgumentException>()
            .Which;

        exception.Message.Should().Contain("ConnectionString:[redacted]");
        exception.Message.Should().NotContain("placeholder");
        exception.Message.Should().NotContain(rawConnectionString);
    }

    [Fact]
    public async Task DbConnectionFactory_RegisterAdHocInstance_ShouldRejectMalformedConnectionStringShapeKey()
    {
        await using ServiceProvider provider = CreateProvider();
        IDbConnectionFactory factory = provider.GetRequiredService<IDbConnectionFactory>();
        const string rawConnectionString =
            "Server='unterminated;Database=TEST;User Id=app_user;Password=placeholder";

        Action act = () => factory.RegisterAdHocInstance(
            rawConnectionString,
            "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False");

        ArgumentException exception = act.Should()
            .Throw<ArgumentException>()
            .Which;

        exception.Message.Should().Contain("ConnectionString:[redacted]");
        exception.Message.Should().NotContain("Password=");
        exception.Message.Should().NotContain("User Id=");
        exception.Message.Should().NotContain("Database=TEST");
        exception.Message.Should().NotContain(rawConnectionString);
    }

    [Fact]
    public async Task DbConnectionFactory_CreateConnectionAsync_ShouldRejectPreRegisteredSensitiveAdHocKey()
    {
        const string rawConnectionString =
            "Raw:Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";
        Lib.Db.Repository.DbConnectionFactory factory = new(TestOptionsFactory.CreateValidOptions());
        System.Reflection.FieldInfo field = typeof(Lib.Db.Repository.DbConnectionFactory)
            .GetField("_adhocConnections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var adHocConnections = (System.Collections.Concurrent.ConcurrentDictionary<string, string>)field.GetValue(factory)!;
        adHocConnections[rawConnectionString] =
            "Server=localhost;Database=TEST;Encrypt=True;TrustServerCertificate=False";

        Func<Task> act = () => factory.CreateConnectionAsync(
            rawConnectionString,
            TestContext.Current.CancellationToken);

        ArgumentException exception = (await act.Should()
            .ThrowAsync<ArgumentException>())
            .Which;

        exception.Message.Should().Contain("Raw:[redacted]");
        exception.Message.Should().NotContain("placeholder");
        exception.Message.Should().NotContain(rawConnectionString);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    public async Task DbConnectionFactory_DefaultFallback_ShouldBeCaseInsensitive(string instanceName)
    {
        string connectionString =
            "Server=127.0.0.1,1;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1";
        await using ServiceProvider provider = CreateProvider(options =>
        {
            options.ConnectionStringNames = ["Primary"];
            options.ConnectionStrings = new Dictionary<string, string>
            {
                ["Primary"] = connectionString
            };
        });
        IDbConnectionFactory factory = provider.GetRequiredService<IDbConnectionFactory>();

        Exception? exception = null;
        try
        {
            await using SqlConnection connection = await factory.CreateConnectionAsync(
                instanceName,
                TestContext.Current.CancellationToken);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        exception.Should().NotBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task UseConnectionString_NonProductionProfile_ShouldAllowAdHocRegistration()
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IDbSession session = scope.ServiceProvider.GetRequiredService<IDbSession>();

        Action act = () => session.UseConnectionString(
            "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=False;TrustServerCertificate=True");

        act.Should().NotThrow();
    }

    private static ServiceProvider CreateProvider(Action<LibDbOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLibDb(options =>
        {
            LibDbOptions valid = TestOptionsFactory.CreateValidOptions();
            options.ConnectionStringNames = valid.ConnectionStringNames;
            options.ConnectionStrings = valid.ConnectionStrings;
            options.EnableSharedMemoryCache = false;
            options.Mars = MarsPolicy.Disabled;
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed record RawBlockRow(int Id);
}
