// ============================================================================
// 파일: Unit/SchemaWarmupServiceCoverageTests.cs
// 설명: SchemaWarmupService 실행 경로 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Reflection;
using Lib.Db.Contracts.Schema;
using Lib.Db.Diagnostics;
using Lib.Db.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class SchemaWarmupServiceCoverageTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldSkipWhenConnectionStringNamesAreEmpty()
    {
        Mock<ISchemaService> schema = new();
        LibDbOptions options = new()
        {
            PrewarmSchemas = ["dbo"]
        };
        SetConnectionStringNames(options, []);
        SchemaWarmupService service = CreateService(schema.Object, options);

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        schema.Verify(
            x => x.PreloadSchemaAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipWhenSchemaCachingIsDisabled()
    {
        Mock<ISchemaService> schema = new();
        SchemaWarmupService service = CreateService(schema.Object, new LibDbOptions
        {
            EnableSchemaCaching = false,
            ConnectionStringNames = ["Primary"],
            PrewarmSchemas = ["dbo"]
        });

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        schema.Verify(
            x => x.PreloadSchemaAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipWhenPrewarmSchemasAreEmpty()
    {
        Mock<ISchemaService> schema = new();
        SchemaWarmupService service = CreateService(schema.Object, new LibDbOptions
        {
            ConnectionStringNames = ["Primary"],
            PrewarmSchemas = []
        });

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        schema.Verify(
            x => x.PreloadSchemaAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipWhenOnlyBlankSchemasAreConfigured()
    {
        Mock<ISchemaService> schema = new();
        SchemaWarmupService service = CreateService(schema.Object, new LibDbOptions
        {
            ConnectionStringNames = ["Primary"],
            PrewarmSchemas = [" ", ""]
        });

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        schema.Verify(
            x => x.PreloadSchemaAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreloadValidSchemasAndContinueAfterTargetFailure()
    {
        Mock<ISchemaService> schema = new();
        schema
            .Setup(x => x.PreloadSchemaAsync(
                It.Is<IEnumerable<string>>(items => items.SequenceEqual(new[] { "dbo", "sales" })),
                "Primary",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreloadResult(LoadedItemsCount: 3, MissingSchemas: ["missing"]));
        schema
            .Setup(x => x.PreloadSchemaAsync(
                It.Is<IEnumerable<string>>(items => items.SequenceEqual(new[] { "dbo", "sales" })),
                "Reporting",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("warmup failure"));
        SchemaWarmupService service = CreateService(schema.Object, new LibDbOptions
        {
            ConnectionStringNames = ["Primary", "Reporting"],
            PrewarmSchemas = ["dbo", "", "sales"],
            PrewarmMaxConcurrency = 1
        });

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        schema.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldTreatCancellationAsHostShutdown()
    {
        using CancellationTokenSource cts = new();
        Mock<ISchemaService> schema = new();
        schema
            .Setup(x => x.PreloadSchemaAsync(
                It.IsAny<IEnumerable<string>>(),
                "Primary",
                It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        SchemaWarmupService service = CreateService(schema.Object, new LibDbOptions
        {
            ConnectionStringNames = ["Primary"],
            PrewarmSchemas = ["dbo"],
            PrewarmMaxConcurrency = 1
        });

        await ExecuteAsync(service, cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCoverEnabledLoggerSkipPaths()
    {
        var logger = new EnabledLogger<SchemaWarmupService>();

        await ExecuteAsync(
            CreateService(
                new Mock<ISchemaService>().Object,
                new LibDbOptions
                {
                    EnableSchemaCaching = false,
                    ConnectionStringNames = ["Primary"],
                    PrewarmSchemas = ["dbo"]
                },
                logger),
            TestContext.Current.CancellationToken);

        LibDbOptions noConnectionNames = new()
        {
            PrewarmSchemas = ["dbo"]
        };
        SetConnectionStringNames(noConnectionNames, []);

        await ExecuteAsync(
            CreateService(new Mock<ISchemaService>().Object, noConnectionNames, logger),
            TestContext.Current.CancellationToken);

        await ExecuteAsync(
            CreateService(
                new Mock<ISchemaService>().Object,
                new LibDbOptions
                {
                    ConnectionStringNames = ["Primary"],
                    PrewarmSchemas = []
                },
                logger),
            TestContext.Current.CancellationToken);

        await ExecuteAsync(
            CreateService(
                new Mock<ISchemaService>().Object,
                new LibDbOptions
                {
                    ConnectionStringNames = ["Primary"],
                    PrewarmSchemas = [" ", ""]
                },
                logger),
            TestContext.Current.CancellationToken);

        logger.InformationCount.Should().Be(4);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCoverEnabledLoggerTargetWarningPaths()
    {
        Mock<ISchemaService> schema = new();
        schema
            .Setup(x => x.PreloadSchemaAsync(
                It.Is<IEnumerable<string>>(items => items.SequenceEqual(new[] { "dbo", "sales" })),
                "Primary",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreloadResult(LoadedItemsCount: 3, MissingSchemas: ["missing"]));
        schema
            .Setup(x => x.PreloadSchemaAsync(
                It.Is<IEnumerable<string>>(items => items.SequenceEqual(new[] { "dbo", "sales" })),
                "Reporting",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("warmup failure"));
        var logger = new EnabledLogger<SchemaWarmupService>();
        SchemaWarmupService service = CreateService(
            schema.Object,
            new LibDbOptions
            {
                ConnectionStringNames = ["Primary", "Reporting"],
                PrewarmSchemas = ["dbo", "", "sales"],
                PrewarmMaxConcurrency = 1
            },
            logger);

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        schema.VerifyAll();
        logger.WarningCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCoverEnabledLoggerCancellationPaths()
    {
        using CancellationTokenSource cts = new();
        Mock<ISchemaService> schema = new();
        schema
            .Setup(x => x.PreloadSchemaAsync(
                It.IsAny<IEnumerable<string>>(),
                "Primary",
                It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var logger = new EnabledLogger<SchemaWarmupService>();
        SchemaWarmupService service = CreateService(
            schema.Object,
            new LibDbOptions
            {
                ConnectionStringNames = ["Primary"],
                PrewarmSchemas = ["dbo"],
                PrewarmMaxConcurrency = 1
            },
            logger);

        await ExecuteAsync(service, cts.Token);

        logger.InformationCount.Should().Be(3);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 3, 3)]
    [InlineData(2048, 5, 5)]
    [InlineData(1, 5, 1)]
    public void GetEffectiveConcurrency_ShouldClampRequestedValue(int requested, int workItemCount, int expected)
    {
        int result = SchemaWarmupService.GetEffectiveConcurrency(
            requested,
            workItemCount,
            processorCount: 4);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetEffectiveConcurrency_ShouldFallbackWhenProcessorCountIsInvalid()
    {
        SchemaWarmupService.GetEffectiveConcurrency(
                requested: 0,
                workItemCount: 3,
                processorCount: 0)
            .Should()
            .Be(1);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(3, 3)]
    public void NormalizeWarmupConcurrency_ShouldUseAtLeastOne(int requested, int expected)
    {
        SchemaWarmupService.NormalizeWarmupConcurrency(requested).Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCatchUnexpectedErrorsAfterWarmup()
    {
        Mock<ISchemaService> schema = new();
        schema
            .Setup(x => x.PreloadSchemaAsync(
                It.Is<IEnumerable<string>>(items => items.SequenceEqual(new[] { "dbo" })),
                "Primary",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreloadResult(LoadedItemsCount: 1, MissingSchemas: []));
        var logger = new ThrowingInformationLogger<SchemaWarmupService>(throwOnInformationCall: 3);
        var service = new SchemaWarmupService(
            schema.Object,
            new LibDbOptions
            {
                ConnectionStringNames = ["Primary"],
                PrewarmSchemas = ["dbo"],
                PrewarmMaxConcurrency = 1
            },
            logger);

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        logger.WarningCount.Should().Be(1);
    }

    [Fact]
    public void CreateDiagnosticRequestInfo_ShouldRedactInstanceIdAndUseWarmupShape()
    {
        DbRequestInfo info = SchemaWarmupService.CreateDiagnosticRequestInfo(
            "Raw:Server=localhost;Database=SecretDb;User Id=user",
            schemaCount: 2);

        info.Operation.Should().Be("SCHEMA_WARMUP");
        info.Target.Should().Be("bulk-load");
        info.CommandKind.Should().Be("Warmup");
        info.IsTransactional.Should().BeFalse();
        info.CorrelationId.Should().StartWith("warmup:");
        info.CorrelationId.Should().EndWith(":2");
        info.InstanceId.Should().Be("Raw:[redacted]");
        info.InstanceId.Should().NotContain("SecretDb");
    }

    [Fact]
    public void SchemaWarmupService_ShouldValidateConstructorArguments()
    {
        Mock<ISchemaService> schema = new();
        LibDbOptions options = new();
        ILogger<SchemaWarmupService> logger = NullLogger<SchemaWarmupService>.Instance;

        Action nullSchema = () => _ = new SchemaWarmupService(null!, options, logger);
        Action nullOptions = () => _ = new SchemaWarmupService(schema.Object, null!, logger);
        Action nullLogger = () => _ = new SchemaWarmupService(schema.Object, options, null!);

        nullSchema.Should().Throw<ArgumentNullException>().WithParameterName("schemaService");
        nullOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        nullLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipWhenConnectionStringNamesAreNull()
    {
        Mock<ISchemaService> schema = new();
        LibDbOptions options = new()
        {
            PrewarmSchemas = ["dbo"]
        };
        SetConnectionStringNames(options, null);
        SchemaWarmupService service = CreateService(schema.Object, options);

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        schema.Verify(
            x => x.PreloadSchemaAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipWhenPrewarmSchemasAreNull()
    {
        Mock<ISchemaService> schema = new();
        LibDbOptions options = new()
        {
            ConnectionStringNames = ["Primary"]
        };
        SetPrewarmSchemas(options, null);
        SchemaWarmupService service = CreateService(schema.Object, options);

        await ExecuteAsync(service, TestContext.Current.CancellationToken);

        schema.Verify(
            x => x.PreloadSchemaAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void CreateDiagnosticRequestInfo_ShouldPreserveNullDiagnosticShapeWhenRedactorReturnsNull()
    {
        string? instanceId = null;

        // Current redaction only returns null for a null instance id, so this verifies the reachable null diagnostic shape.
        DbDiagnosticRedactor.RedactInstanceId(instanceId).Should().BeNull();

        DbRequestInfo info = SchemaWarmupService.CreateDiagnosticRequestInfo(instanceId!, schemaCount: 0);

        info.InstanceId.Should().BeNull();
        info.CorrelationId.Should().Be("warmup::0");
    }

    private static SchemaWarmupService CreateService(ISchemaService schemaService, LibDbOptions options)
        => new(
            schemaService,
            options,
            NullLogger<SchemaWarmupService>.Instance);

    private static SchemaWarmupService CreateService(
        ISchemaService schemaService,
        LibDbOptions options,
        ILogger<SchemaWarmupService> logger)
        => new(schemaService, options, logger);

    private static void SetConnectionStringNames(LibDbOptions options, IReadOnlyList<string>? value)
    {
        FieldInfo field = typeof(LibDbOptions).GetField(
            "<ConnectionStringNames>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(options, value);
    }

    private static void SetPrewarmSchemas(LibDbOptions options, List<string>? value)
    {
        FieldInfo field = typeof(LibDbOptions).GetField(
            "<PrewarmSchemas>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(options, value);
    }

    private static async Task ExecuteAsync(SchemaWarmupService service, CancellationToken cancellationToken)
    {
        MethodInfo method = typeof(SchemaWarmupService).GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(service, [cancellationToken])!;
        await task;
    }

    private sealed class ThrowingInformationLogger<T>(int throwOnInformationCall) : ILogger<T>
    {
        private int _informationCount;

        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information &&
                Interlocked.Increment(ref _informationCount) == throwOnInformationCall)
            {
                throw new InvalidOperationException("logger failure");
            }

            if (logLevel == LogLevel.Warning)
                WarningCount++;
        }
    }

    private sealed class EnabledLogger<T> : ILogger<T>
    {
        private int _informationCount;
        private int _warningCount;

        public int InformationCount => Volatile.Read(ref _informationCount);

        public int WarningCount => Volatile.Read(ref _warningCount);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
                Interlocked.Increment(ref _informationCount);

            if (logLevel == LogLevel.Warning)
                Interlocked.Increment(ref _warningCount);
        }
    }
}
