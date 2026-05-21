// ============================================================================
// 파일: Binding/DataBindingTests.cs
// 설명: DbBinder 데이터 바인딩 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.Core;
using Lib.Db.Execution.Binding;

namespace Lib.Db.IntegrationTests.Binding;

public sealed class DataBindingTests : IDisposable
{
    public DataBindingTests()
    {
        DbBinder.ConfigureTvp(new LibDbOptions());
        DbBinder.ValidatorCallback = null;
    }

    public void Dispose()
    {
        DbBinder.ConfigureTvp(new LibDbOptions());
        DbBinder.ValidatorCallback = null;
    }

    [Fact]
    public void DB01_BindParameter_ShouldThrow_WhenStrictAndNull()
    {
        using SqlCommand cmd = new();
        SpParameterMetadata meta = new()
        {
            Name = "p1",
            IsNullable = false,
            Direction = ParameterDirection.Input,
            SqlDbType = SqlDbType.Int
        };

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            DbBinder.BindParameter(cmd, meta, null, strictCheck: true));

        Assert.Contains("필수값입니다", ex.Message);
        Assert.Contains("NOT NULL", ex.Message);
    }

    [Fact]
    public void DB02_CheckValueOverflow_ShouldThrow_WhenDecimalExceedsPrecision()
    {
        using SqlCommand cmd = new();
        SpParameterMetadata meta = new()
        {
            Name = "pDec",
            SqlDbType = SqlDbType.Decimal,
            Precision = 4,
            Scale = 2,
            Direction = ParameterDirection.Input
        };

        decimal val = 100.00m;

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbBinder.BindParameter(cmd, meta, val, strictCheck: false));

        Assert.Contains("DB 제약", ex.Message);
        Assert.Contains("Precision:4", ex.Message);
    }

    [Fact]
    public void DB02_CheckValueOverflow_ShouldThrow_WhenTinyIntOverflow_Renamed()
    {
        using SqlCommand cmd = new();
        SpParameterMetadata meta = new()
        {
            Name = "pTiny",
            SqlDbType = SqlDbType.TinyInt,
            Direction = ParameterDirection.Input
        };

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbBinder.BindParameter(cmd, meta, 256, strictCheck: false));

        Assert.Contains("DB 제약", ex.Message);
    }

    [Fact]
    public void DB03_BindRaw_ShouldSerialize_ComplexObject()
    {
        using SqlCommand cmd = new();
        object complexObj = new { Name = "TestUser", Age = 30 };

        DbBinder.BindRawParameter(cmd, "pJson", complexObj);

        SqlParameter param = cmd.Parameters["@pJson"];
        Assert.Equal(SqlDbType.NVarChar, param.SqlDbType);

        string? json = param.Value as string;
        Assert.NotNull(json);
        Assert.Contains("TestUser", json);
        Assert.Contains("30", json);
    }

    [Fact]
    public void DB03_BindRaw_ShouldBindDateOnly_AsSqlDate()
    {
        using SqlCommand cmd = new();
        DateOnly value = new(2026, 5, 17);

        DbBinder.BindRawParameter(cmd, "pDate", value);

        SqlParameter param = cmd.Parameters["@pDate"];
        Assert.Equal(SqlDbType.Date, param.SqlDbType);
        DateTime dateTime = Assert.IsType<DateTime>(param.Value);
        Assert.Equal(new DateTime(2026, 5, 17), dateTime);
    }

    [Fact]
    public void DB03_BindRaw_ShouldBindTimeOnly_AsSqlTime()
    {
        using SqlCommand cmd = new();
        TimeOnly value = new(14, 30, 15);

        DbBinder.BindRawParameter(cmd, "pTime", value);

        SqlParameter param = cmd.Parameters["@pTime"];
        Assert.Equal(SqlDbType.Time, param.SqlDbType);
        TimeSpan timeSpan = Assert.IsType<TimeSpan>(param.Value);
        Assert.Equal(new TimeSpan(14, 30, 15), timeSpan);
    }

    [Fact]
    public void DB04_LegacyTvp_ShouldWork_WhenSgDisabled()
    {
        DbBinder.ConfigureTvp(new LibDbOptions { EnableGeneratedTvpBinder = false });

        try
        {
            List<SimpleDto> list =
            [
                new SimpleDto { Id = 1, Name = "A" },
                new SimpleDto { Id = 2, Name = "B" }
            ];

            DbBinder.ValidatorCallback = (t, s) => true;

            System.Data.IDataReader reader = DbBinder.ToDataReader(list);
            Assert.NotNull(reader);

            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetValue(0));
            Assert.True(reader.Read());
            Assert.Equal(2, reader.GetValue(0));
            Assert.False(reader.Read());
        }
        finally
        {
            DbBinder.ValidatorCallback = null;
        }
    }

    [Fact]
    public void DB04_TvpValidationCallback_ShouldRunOnEveryStructuredBinding()
    {
        List<SimpleDto> list =
        [
            new SimpleDto { Id = 1, Name = "A" }
        ];
        SpParameterMetadata meta = new(
            Name: "@Rows",
            UdtTypeName: "dbo.SimpleDtoList",
            Size: 0,
            SqlDbType: SqlDbType.Structured,
            Direction: ParameterDirection.Input,
            Precision: 0,
            Scale: 0,
            IsNullable: false,
            HasDefaultValue: false);

        int calls = 0;
        DbBinder.ValidatorCallback = (_, _) => ++calls == 1;

        using SqlCommand first = new();
        DbBinder.BindParameter(first, meta, list, strictCheck: true);

        using SqlCommand second = new();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            DbBinder.BindParameter(second, meta, list, strictCheck: true));

        Assert.Equal(2, calls);
        Assert.Contains("스키마 검증 실패", ex.Message);
    }

    [Fact]
    public void DB04_UseHighPerformanceDb_ShouldUseCurrentExecutionInstance_WhenValidatingTvp()
    {
        RecordingTvpSchemaValidator validator = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ITvpSchemaValidator>(validator)
            .AddSingleton(new LibDbOptions { ConnectionStringNames = ["Primary"] })
            .BuildServiceProvider();

        using IHost host = new HostStub(provider);
        host.UseHighPerformanceDb();

        using var scope = DbExecutionContextScope.Enter(
            "TenantB",
            "dbo.usp_SaveRows",
            CommandType.StoredProcedure);

        List<SimpleDto> list =
        [
            new SimpleDto { Id = 1, Name = "A" }
        ];
        SpParameterMetadata meta = new(
            Name: "@Rows",
            UdtTypeName: "dbo.SimpleDtoList",
            Size: 0,
            SqlDbType: SqlDbType.Structured,
            Direction: ParameterDirection.Input,
            Precision: 0,
            Scale: 0,
            IsNullable: false,
            HasDefaultValue: false);

        using SqlCommand command = new();
        DbBinder.BindParameter(command, meta, list, strictCheck: true);

        Assert.Equal(1, validator.Calls);
        Assert.Equal("TenantB", validator.InstanceHash);
    }

    [Fact]
    public void DB04_UseHighPerformanceDb_ShouldFailClosedWithoutExecutionInstance_WhenMultiInstance()
    {
        RecordingTvpSchemaValidator validator = new();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<ITvpSchemaValidator>(validator)
            .AddSingleton(new LibDbOptions { ConnectionStringNames = ["Primary", "Secondary"] })
            .BuildServiceProvider();

        using IHost host = new HostStub(provider);
        host.UseHighPerformanceDb();

        List<SimpleDto> list =
        [
            new SimpleDto { Id = 1, Name = "A" }
        ];
        SpParameterMetadata meta = new(
            Name: "@Rows",
            UdtTypeName: "dbo.SimpleDtoList",
            Size: 0,
            SqlDbType: SqlDbType.Structured,
            Direction: ParameterDirection.Input,
            Precision: 0,
            Scale: 0,
            IsNullable: false,
            HasDefaultValue: false);

        using SqlCommand command = new();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            DbBinder.BindParameter(command, meta, list, strictCheck: true));

        Assert.Equal(0, validator.Calls);
        Assert.Contains("스키마 검증 실패", ex.Message);
    }

    [Fact]
    public void DB05_DateTime_LegacyRange_ShouldThrow()
    {
        using SqlCommand cmd = new();
        SpParameterMetadata meta = new()
        {
            Name = "pDt",
            SqlDbType = SqlDbType.DateTime,
            Direction = ParameterDirection.Input
        };

        DateTime val = new(1000, 1, 1);

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbBinder.BindParameter(cmd, meta, val, strictCheck: false));

        Assert.Contains("1753", ex.Message);
    }

    [Fact]
    public void DB06_BindingErrors_ShouldRedactCommandTextAndInputValues()
    {
        using SqlCommand cmd = new();
        using var scope = DbExecutionContextScope.Enter(
            "primary",
            "SELECT * FROM dbo.SecretTable WHERE SecretToken = @pDec",
            CommandType.Text);
        SpParameterMetadata meta = new()
        {
            Name = "pDec",
            SqlDbType = SqlDbType.Decimal,
            Precision = 4,
            Scale = 2,
            Direction = ParameterDirection.Input
        };

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbBinder.BindParameter(cmd, meta, 100.00m, strictCheck: false));

        Assert.Contains("[redacted]", ex.Message);
        Assert.DoesNotContain("SELECT", ex.Message);
        Assert.DoesNotContain("SecretTable", ex.Message);
        Assert.DoesNotContain("100.00", ex.Message);
    }

    [Fact]
    public void DB07_RequiredAndDateTimeErrors_ShouldRedactSensitiveDetails()
    {
        using var scope = DbExecutionContextScope.Enter(
            "primary",
            "EXEC dbo.usp_SaveSecret @password",
            CommandType.StoredProcedure);

        using SqlCommand requiredCommand = new();
        SpParameterMetadata requiredMeta = new()
        {
            Name = "password",
            IsNullable = false,
            Direction = ParameterDirection.Input,
            SqlDbType = SqlDbType.NVarChar
        };

        ArgumentException required = Assert.Throws<ArgumentException>(() =>
            DbBinder.BindParameter(requiredCommand, requiredMeta, null, strictCheck: true));

        Assert.Contains("[redacted]", required.Message);
        Assert.DoesNotContain("usp_SaveSecret", required.Message);

        using SqlCommand dateCommand = new();
        SpParameterMetadata dateMeta = new()
        {
            Name = "createdAt",
            SqlDbType = SqlDbType.DateTime,
            Direction = ParameterDirection.Input
        };

        ArgumentOutOfRangeException dateRange = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbBinder.BindParameter(dateCommand, dateMeta, new DateTime(1000, 1, 1), strictCheck: false));

        Assert.DoesNotContain("1000", dateRange.Message);
    }

    private sealed class SimpleDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class RecordingTvpSchemaValidator : ITvpSchemaValidator
    {
        public int Calls { get; private set; }

        public string? InstanceHash { get; private set; }

        public Task ValidateAsync<T>(
            string tvpTypeName,
            TvpAccessors<T> accessors,
            string instanceHash,
            CancellationToken ct)
        {
            Calls++;
            InstanceHash = instanceHash;
            return Task.CompletedTask;
        }
    }

    private sealed class HostStub(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;

        public void Dispose()
        {
            if (Services is IDisposable disposable)
                disposable.Dispose();
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
