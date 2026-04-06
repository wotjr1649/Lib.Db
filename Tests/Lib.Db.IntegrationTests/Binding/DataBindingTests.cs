// ============================================================================
// 파일: Binding/DataBindingTests.cs
// 설명: DbBinder 데이터 바인딩 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Microsoft.Data.SqlClient;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Core;
using Lib.Db.Execution.Binding;

namespace Lib.Db.IntegrationTests.Binding;

public sealed class DataBindingTests : IDisposable
{
    public DataBindingTests()
    {
        DbBinder.ConfigureTvp(new LibDbOptions());
    }

    public void Dispose()
    {
        DbBinder.ConfigureTvp(new LibDbOptions());
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

    private sealed class SimpleDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
