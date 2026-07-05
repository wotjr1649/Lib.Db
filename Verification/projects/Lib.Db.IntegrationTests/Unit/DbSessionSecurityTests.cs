// ============================================================================
// 파일: Unit/DbSessionSecurityTests.cs
// 설명: IDbSession 인스턴스 선택 보안 경계 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Core;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Unit;

[Trait("Category", "Unit")]
public sealed class DbSessionSecurityTests
{
    [Fact]
    public async Task DisposeAsync_ShouldRedactCleanupFailures()
    {
        const string secretMarker = "Server=prod;User Id=sa;Password=dispose-secret";
        Mock<IDbExecutor> executor = new();
        executor.As<IAsyncDisposable>()
            .Setup(x => x.DisposeAsync())
            .Returns(ValueTask.FromException(new InvalidOperationException(secretMarker)));

        var session = new DbSession(
            Mock.Of<IDbExecutorFactory>(),
            Mock.Of<IDbConnectionFactory>(),
            TestOptionsFactory.CreateValidOptions());
        var state = new DbInstanceState
        {
            InstanceName = secretMarker,
            ConnectionHash = "hash",
            ActiveExecutor = executor.Object
        };

        System.Reflection.FieldInfo field = typeof(DbSession)
            .GetField("_instances", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var instances = (System.Collections.Concurrent.ConcurrentDictionary<string, DbInstanceState>)field.GetValue(session)!;
        instances.TryAdd(secretMarker, state).Should().BeTrue();

        Func<Task> act = async () => await session.DisposeAsync();

        AggregateException exception = (await act.Should()
            .ThrowAsync<AggregateException>())
            .Which;
        string rendered = exception.ToString();
        rendered.Should().NotContain(secretMarker);
        rendered.Should().NotContain("dispose-secret");
        rendered.Should().NotContain("Password=");
        exception.InnerExceptions.Should().ContainSingle();
        exception.InnerExceptions[0].InnerException.Should().BeNull();
        exception.InnerExceptions[0].Message.Should().Contain(nameof(InvalidOperationException));
        exception.InnerExceptions[0].Message.Should().Contain("ConnectionString:[redacted]");
    }

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

    [Theory]
    [InlineData("dbo.Target;DROP TABLE dbo.Target")]
    [InlineData("dbo.Target -- comment")]
    [InlineData("server.database.schema.Target")]
    [InlineData("[dbo].Target")]
    [InlineData("dbo.[Target]")]
    public async Task BulkInsertAsync_ShouldRejectUnsafeDestinationTableBeforeOpeningConnection(string destinationTable)
    {
        Mock<IDbConnectionFactory> connectionFactory = new(MockBehavior.Strict);
        await using DbSession session = new(
            Mock.Of<IDbExecutorFactory>(),
            connectionFactory.Object,
            TestOptionsFactory.CreateValidOptions());

        var result = await session.BulkInsertAsync(
            "Default",
            destinationTable,
            new[] { new RawBlockRow(1) },
            options: null,
            ct: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Message.Should().NotContain("DROP");
        connectionFactory.Verify(x => x.CreateConnectionAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
