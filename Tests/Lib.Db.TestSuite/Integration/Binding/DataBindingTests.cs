using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient; // Changed from System.Data.SqlClient
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Core;
using Lib.Db.Execution.Binding;
using Xunit;

namespace Lib.Db.Verification.Tests.Binding;

public class DataBindingTests : IDisposable
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
        using var cmd = new SqlCommand();
        var meta = new SpParameterMetadata
        {
            Name = "p1",
            IsNullable = false,
            Direction = ParameterDirection.Input,
            SqlDbType = SqlDbType.Int
        };

        var ex = Assert.Throws<ArgumentException>(() => 
            DbBinder.BindParameter(cmd, meta, null, strictCheck: true));

        Assert.Contains("필수값입니다", ex.Message);
        Assert.Contains("NOT NULL", ex.Message);
    }

    [Fact]
    public void DB02_CheckValueOverflow_ShouldThrow_WhenDecimalExceedsPrecision()
    {
        using var cmd = new SqlCommand();
        var meta = new SpParameterMetadata
        {
            Name = "pDec",
            SqlDbType = SqlDbType.Decimal,
            Precision = 4,
            Scale = 2,
            Direction = ParameterDirection.Input
        };

        var val = 100.00m;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbBinder.BindParameter(cmd, meta, val, strictCheck: false));

        Assert.Contains("DB 제약", ex.Message);
        Assert.Contains("Precision:4", ex.Message);
    }

    [Fact]
    public void DB02_CheckValueOverflow_ShouldThrow_WhenTinyIntOverflow_Renamed() // Renamed
    {
        using var cmd = new SqlCommand();
        var meta = new SpParameterMetadata
        {
            Name = "pTiny",
            SqlDbType = SqlDbType.TinyInt,
            Direction = ParameterDirection.Input
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbBinder.BindParameter(cmd, meta, 256, strictCheck: false));
        
        Assert.Contains("DB 제약", ex.Message);
    }

    [Fact]
    public void DB03_BindRaw_ShouldSerialize_ComplexObject()
    {
        using var cmd = new SqlCommand();
        var complexObj = new { Name = "TestUser", Age = 30 };
        
        DbBinder.BindRawParameter(cmd, "pJson", complexObj);

        var param = cmd.Parameters["@pJson"];
        Assert.Equal(SqlDbType.NVarChar, param.SqlDbType);
        
        var json = param.Value as string;
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
            var list = new List<SimpleDto> 
            { 
                new SimpleDto { Id = 1, Name = "A" }, 
                new SimpleDto { Id = 2, Name = "B" } 
            };

            DbBinder.ValidatorCallback = (t, s) => true; 

            var reader = DbBinder.ToDataReader(list);
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
        using var cmd = new SqlCommand();
        var meta = new SpParameterMetadata
        {
            Name = "pDt",
            SqlDbType = SqlDbType.DateTime,
            Direction = ParameterDirection.Input
        };

        var val = new DateTime(1000, 1, 1);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbBinder.BindParameter(cmd, meta, val, strictCheck: false));
            
        Assert.Contains("1753", ex.Message);
    }

    // Helper DTO for DB04
    private class SimpleDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
