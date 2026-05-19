// ============================================================================
// 파일: Unit/RuntimeTvpBindingTests.cs
// 설명: 런타임 TVP 바인딩 API 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Execution.Tvp;
using Lib.Db.Execution.Binding;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using System.Data;
using System.Data.Common;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class RuntimeTvpBindingTests
{
    [Fact]
    public void LibDbTvp_ShouldCreateStructuredValueWithValidTypeName()
    {
        var rows = new[] { new OrderItemRow(1, "A100", 2) };

        LibDbTvpValue value = LibDb.Tvp("dbo.T_OrderItem", rows);

        value.TypeName.FullName.Should().Be("dbo.T_OrderItem");
        value.Rows.Should().BeSameAs(rows);
        value.RowType.Should().Be(typeof(OrderItemRow));
        value.Policy.Should().Be(TvpBindingPolicy.Strict);
    }

    [Fact]
    public void LibDbTvp_ShouldRejectUnsafeTypeNameBeforeBinding()
    {
        var rows = new[] { new OrderItemRow(1, "A100", 2) };

        Action act = () => LibDb.Tvp("dbo.T_OrderItem;DROP TABLE X", rows);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*TVP type name*");
    }

    [Fact]
    public void TvpMappingRegistry_ShouldResolveRegisteredRowType()
    {
        var registry = new TvpMappingRegistry();

        registry.Map<OrderItemRow>("dbo.T_OrderItem");

        registry.TryResolve(
                typeof(OrderItemRow),
                out TvpTypeName typeName,
                out TvpBindingPolicy policy)
            .Should()
            .BeTrue();

        typeName.FullName.Should().Be("dbo.T_OrderItem");
        policy.Should().Be(TvpBindingPolicy.Strict);
    }

    [Fact]
    public void TvpMappingRegistry_ShouldNotGuessUnregisteredRowType()
    {
        var registry = new TvpMappingRegistry();

        registry.TryResolve(
                typeof(OrderItemRow),
                out _,
                out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void RuntimeTvpDataReader_ShouldReadObjectRowsByColumnName()
    {
        var rows = new[] { new OrderItemRow(1, "A100", 2) };
        var columns = new[]
        {
            TvpColumnShape.Required("Id", typeof(int)),
            TvpColumnShape.Required("Sku", typeof(string)),
            TvpColumnShape.Required("Qty", typeof(int))
        };

        using var reader = RuntimeTvpDataReader.Create(rows, columns, TvpBindingPolicy.Strict);

        reader.Read().Should().BeTrue();
        reader.GetValue(0).Should().Be(1);
        reader.GetValue(1).Should().Be("A100");
        reader.GetValue(2).Should().Be(2);
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void RuntimeTvpDataReader_ShouldExposeSqlClientSchemaMetadata()
    {
        var rows = new[] { new { Sku = "A100", Price = 12.34m } };
        var columns = new[]
        {
            TvpColumnShape.Required("Sku", typeof(string), size: 64),
            TvpColumnShape.Required("Price", typeof(decimal), precision: 18, scale: 2)
        };

        using var reader = RuntimeTvpDataReader.Create(rows, columns, TvpBindingPolicy.Strict);

        DataTable schema = reader.GetSchemaTable();
        schema.Columns.Contains(SchemaTableColumn.IsKey).Should().BeTrue();
        schema.Columns.Contains(SchemaTableColumn.ColumnSize).Should().BeTrue();
        schema.Columns.Contains(SchemaTableColumn.NumericPrecision).Should().BeTrue();
        schema.Columns.Contains(SchemaTableColumn.NumericScale).Should().BeTrue();

        schema.Rows[0][SchemaTableColumn.ColumnSize].Should().Be(64);
        schema.Rows[0][SchemaTableColumn.IsKey].Should().Be(false);
        schema.Rows[1][SchemaTableColumn.NumericPrecision].Should().Be((short)18);
        schema.Rows[1][SchemaTableColumn.NumericScale].Should().Be((short)2);
    }

    [Fact]
    public void RuntimeTvpDataReader_ShouldReadDictionaryRowsByColumnName()
    {
        var rows = new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = 1,
                ["Sku"] = "A100",
                ["Qty"] = 2
            }
        };

        var columns = new[]
        {
            TvpColumnShape.Required("Id", typeof(int)),
            TvpColumnShape.Required("Sku", typeof(string)),
            TvpColumnShape.Required("Qty", typeof(int))
        };

        using var reader = RuntimeTvpDataReader.Create(rows, columns, TvpBindingPolicy.Strict);

        reader.Read().Should().BeTrue();
        reader.GetValue(0).Should().Be(1);
        reader.GetValue(1).Should().Be("A100");
        reader.GetValue(2).Should().Be(2);
    }

    [Fact]
    public void RuntimeTvpDataReader_ShouldRejectMissingRequiredColumnInStrictMode()
    {
        var rows = new[] { new { Id = 1, Sku = "A100" } };
        var columns = new[]
        {
            TvpColumnShape.Required("Id", typeof(int)),
            TvpColumnShape.Required("Sku", typeof(string)),
            TvpColumnShape.Required("Qty", typeof(int))
        };

        Action act = () => RuntimeTvpDataReader.Create(rows, columns, TvpBindingPolicy.Strict);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Qty*");
    }

    [Fact]
    public void BindRawParameter_ShouldSetStructuredParameterForExplicitTvpAndKeepScalars()
    {
        var rows = new[] { new OrderItemRow(1, "A100", 2) };

        using var command = new SqlCommand();
        DbBinder.BindRawParameter(command, "OrderId", 123);
        DbBinder.BindRawParameter(command, "Rows", LibDb.Tvp("dbo.T_OrderItem", rows));
        DbBinder.BindRawParameter(command, "RequestedBy", "system");

        command.Parameters["@OrderId"].Value.Should().Be(123);
        command.Parameters["@RequestedBy"].Value.Should().Be("system");

        SqlParameter rowsParameter = command.Parameters["@Rows"];
        rowsParameter.SqlDbType.Should().Be(SqlDbType.Structured);
        rowsParameter.TypeName.Should().Be("dbo.T_OrderItem");
        rowsParameter.Value.Should().BeAssignableTo<DbDataReader>();
    }

    [Fact]
    public void BindRawParameter_ShouldAutoBindRegisteredEnumerableWithRuntimeTvpOptions()
    {
        var rows = new[] { new OrderItemRow(1, "A100", 2) };
        var options = new LibDbOptions();
        options.Tvp.Map<OrderItemRow>("dbo.T_OrderItem", TvpBindingPolicy.Adaptive);

        try
        {
            DbBinder.ConfigureTvp(options);

            using var command = new SqlCommand();
            DbBinder.BindRawParameter(command, "Rows", rows);

            SqlParameter rowsParameter = command.Parameters["@Rows"];
            rowsParameter.SqlDbType.Should().Be(SqlDbType.Structured);
            rowsParameter.TypeName.Should().Be("dbo.T_OrderItem");
            rowsParameter.Value.Should().BeAssignableTo<DbDataReader>();
        }
        finally
        {
            DbBinder.ConfigureTvp(new LibDbOptions());
        }
    }

    [Fact]
    public void BindRawParameter_ShouldReuseRuntimeTvpShapeForRegisteredFastPath()
    {
        var rows = new[] { new FastPathOrderItemRow(1, "A100", 2, 12.34m) };
        var options = new LibDbOptions();
        options.Tvp.Map<FastPathOrderItemRow>("dbo.T_OrderItem");

        try
        {
            DbBinder.ConfigureTvp(options);
            DbBinder.ClearTvpCaches();

            using var firstCommand = new SqlCommand();
            DbBinder.BindRawParameter(firstCommand, "Rows", rows);
            var firstReader = (RuntimeTvpDataReader)firstCommand.Parameters["@Rows"].Value;

            using var secondCommand = new SqlCommand();
            DbBinder.BindRawParameter(secondCommand, "Rows", rows);
            var secondReader = (RuntimeTvpDataReader)secondCommand.Parameters["@Rows"].Value;

            DataTable firstSchema = firstReader.GetSchemaTable();
            DataTable secondSchema = secondReader.GetSchemaTable();

            secondSchema.Should().BeSameAs(firstSchema);
            firstSchema.Rows[1][SchemaTableColumn.ColumnSize].Should().Be(64);
            firstSchema.Rows[3][SchemaTableColumn.NumericPrecision].Should().Be((short)18);
            firstSchema.Rows[3][SchemaTableColumn.NumericScale].Should().Be((short)2);
        }
        finally
        {
            DbBinder.ConfigureTvp(new LibDbOptions());
            DbBinder.ClearTvpCaches();
        }
    }

    [Fact]
    public void BindRawParameter_ShouldUseRegisteredStaticShapeForAotFastPath()
    {
        var rows = new[] { new StaticShapeOrderItemRow(1, "A100") };
        var options = new LibDbOptions();
        options.Tvp
            .Map<StaticShapeOrderItemRow>("dbo.T_OrderItem")
            .Column("Id", SqlDbType.Int, static x => x.SourceId)
            .Column("Sku", SqlDbType.NVarChar, static x => x.Code, size: 64);

        try
        {
            DbBinder.ConfigureTvp(options);
            DbBinder.ClearTvpCaches();

            using var command = new SqlCommand();
            DbBinder.BindRawParameter(command, "Rows", rows);

            var records = command.Parameters["@Rows"].Value
                .Should()
                .BeAssignableTo<IEnumerable<SqlDataRecord>>()
                .Subject;

            using IEnumerator<SqlDataRecord> enumerator = records.GetEnumerator();
            enumerator.MoveNext().Should().BeTrue();
            SqlDataRecord record = enumerator.Current;
            record.GetName(0).Should().Be("Id");
            record.GetName(1).Should().Be("Sku");
            record.GetValue(0).Should().Be(1);
            record.GetValue(1).Should().Be("A100");
        }
        finally
        {
            DbBinder.ConfigureTvp(new LibDbOptions());
            DbBinder.ClearTvpCaches();
        }
    }

    [Fact]
    public void BindRawParameter_ShouldPreferRegisteredStaticShapeOverTvpRowAttributeFallback()
    {
        var rows = new[] { new AttributeAndStaticShapeOrderItemRow(1, "A100", 999) };
        var options = new LibDbOptions();
        options.Tvp
            .Map<AttributeAndStaticShapeOrderItemRow>("dbo.T_OrderItem")
            .Column("Id", SqlDbType.Int, static x => x.SourceId)
            .Column("Sku", SqlDbType.NVarChar, static x => x.Code, size: 64);

        try
        {
            DbBinder.ConfigureTvp(options);
            DbBinder.ClearTvpCaches();

            using var command = new SqlCommand();
            DbBinder.BindRawParameter(command, "Rows", rows);

            SqlParameter rowsParameter = command.Parameters["@Rows"];
            rowsParameter.TypeName.Should().Be("dbo.T_OrderItem");
            var records = rowsParameter.Value
                .Should()
                .BeAssignableTo<IEnumerable<SqlDataRecord>>()
                .Subject;

            using IEnumerator<SqlDataRecord> enumerator = records.GetEnumerator();
            enumerator.MoveNext().Should().BeTrue();
            SqlDataRecord record = enumerator.Current;
            record.FieldCount.Should().Be(2);
            record.GetName(0).Should().Be("Id");
            record.GetName(1).Should().Be("Sku");
            record.GetValue(0).Should().Be(1);
            record.GetValue(1).Should().Be("A100");
        }
        finally
        {
            DbBinder.ConfigureTvp(new LibDbOptions());
            DbBinder.ClearTvpCaches();
        }
    }

    [Fact]
    public void LibDbTvp_ShouldAcceptReusableStaticShapeForExplicitWrapper()
    {
        var rows = new[] { new StaticShapeOrderItemRow(1, "A100") };
        TvpShape<StaticShapeOrderItemRow> shape = TvpShape
            .For<StaticShapeOrderItemRow>()
            .Column("Id", SqlDbType.Int, static x => x.SourceId)
            .Column("Sku", SqlDbType.NVarChar, static x => x.Code, size: 64)
            .Build();

        using var command = new SqlCommand();
        DbBinder.BindRawParameter(command, "Rows", LibDb.Tvp("dbo.T_OrderItem", rows, shape));

        var records = command.Parameters["@Rows"].Value
            .Should()
            .BeAssignableTo<IEnumerable<SqlDataRecord>>()
            .Subject;

        using IEnumerator<SqlDataRecord> enumerator = records.GetEnumerator();
        enumerator.MoveNext().Should().BeTrue();
        SqlDataRecord record = enumerator.Current;
        record.GetName(0).Should().Be("Id");
        record.GetName(1).Should().Be("Sku");
        record.GetValue(0).Should().Be(1);
        record.GetValue(1).Should().Be("A100");
    }

    [Fact]
    public void BindParameter_ShouldSetStructuredParameterForExplicitTvpWhenSpMetadataIsStructured()
    {
        var rows = new[] { new OrderItemRow(1, "A100", 2) };
        SpParameterMetadata metadata = StructuredRowsMetadata("dbo.T_OrderItem");

        using var command = new SqlCommand();
        DbBinder.BindParameter(command, metadata, LibDb.Tvp("dbo.T_OrderItem", rows), strictCheck: true);

        SqlParameter rowsParameter = command.Parameters["@Rows"];
        rowsParameter.SqlDbType.Should().Be(SqlDbType.Structured);
        rowsParameter.TypeName.Should().Be("dbo.T_OrderItem");
        rowsParameter.Value.Should().BeAssignableTo<DbDataReader>();
    }

    [Fact]
    public void BindParameter_ShouldUseRegisteredStaticShapeForStructuredMetadata()
    {
        var rows = new[] { new StaticShapeOrderItemRow(1, "A100") };
        var options = new LibDbOptions();
        options.Tvp
            .Map<StaticShapeOrderItemRow>("dbo.T_OrderItem")
            .Column("Id", SqlDbType.Int, static x => x.SourceId)
            .Column("Sku", SqlDbType.NVarChar, static x => x.Code, size: 64);

        try
        {
            DbBinder.ConfigureTvp(options);
            SpParameterMetadata metadata = StructuredRowsMetadata("dbo.T_OrderItem");

            using var command = new SqlCommand();
            DbBinder.BindParameter(command, metadata, rows, strictCheck: true);

            SqlParameter rowsParameter = command.Parameters["@Rows"];
            rowsParameter.SqlDbType.Should().Be(SqlDbType.Structured);
            rowsParameter.TypeName.Should().Be("dbo.T_OrderItem");

            var records = rowsParameter.Value
                .Should()
                .BeAssignableTo<IEnumerable<SqlDataRecord>>()
                .Subject;

            using IEnumerator<SqlDataRecord> enumerator = records.GetEnumerator();
            enumerator.MoveNext().Should().BeTrue();
            SqlDataRecord record = enumerator.Current;
            record.GetName(0).Should().Be("Id");
            record.GetValue(0).Should().Be(1);
            record.GetName(1).Should().Be("Sku");
            record.GetValue(1).Should().Be("A100");
        }
        finally
        {
            DbBinder.ConfigureTvp(new LibDbOptions());
            DbBinder.ClearTvpCaches();
        }
    }

    [Fact]
    public void BindParameter_ShouldRejectExplicitTvpWhenMetadataTypeNameDiffers()
    {
        var rows = new[] { new OrderItemRow(1, "A100", 2) };
        SpParameterMetadata metadata = StructuredRowsMetadata("dbo.T_OrderItem");

        using var command = new SqlCommand();
        Action act = () => DbBinder.BindParameter(
            command,
            metadata,
            LibDb.Tvp("dbo.T_DifferentOrderItem", rows),
            strictCheck: true);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*TVP type name*");
    }

    [Fact]
    public void TvpFactoryRegistry_ShouldRejectUnsafeRegisteredTypeName()
    {
        Action act = () => TvpFactoryRegistry.Register(
            typeof(List<OrderItemRow>),
            static _ => new DataTable().CreateDataReader(),
            "dbo.T_OrderItem;DROP");

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*TVP type name*");
    }

    [Fact]
    public void BindRawParameter_ShouldRejectUnsafeTvpRowAttributeTypeName()
    {
        var rows = new[] { new UnsafeAttributeOrderItemRow(1) };

        using var command = new SqlCommand();
        Action act = () => DbBinder.BindRawParameter(command, "Rows", rows);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*TVP type name*");
    }

    [Fact]
    public void BindParameter_ShouldRejectUnsafeStructuredMetadataTypeName()
    {
        using var command = new SqlCommand();
        using DataTable table = new();

        Action act = () => DbBinder.BindParameter(
            command,
            StructuredRowsMetadata("dbo.T_OrderItem;DROP"),
            table,
            strictCheck: true);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*TVP type name*");
    }

    private static SpParameterMetadata StructuredRowsMetadata(string typeName)
        => new(
            Name: "@Rows",
            UdtTypeName: typeName,
            Size: 0,
            SqlDbType: SqlDbType.Structured,
            Direction: ParameterDirection.Input,
            Precision: 0,
            Scale: 0,
            IsNullable: false,
            HasDefaultValue: false);

    private sealed record OrderItemRow(int Id, string Sku, int Qty);

    private sealed record FastPathOrderItemRow(
        int Id,
        [property: DbParameterAttribute(Size = 64)] string Sku,
        int Qty,
        [property: DbParameterAttribute(Precision = 18, Scale = 2)] decimal Price);

    private sealed record StaticShapeOrderItemRow(int SourceId, string Code);

    [TvpRow(TypeName = "dbo.T_ReflectionFallbackOrderItem")]
    private sealed record AttributeAndStaticShapeOrderItemRow(int SourceId, string Code, int Qty);

    [TvpRow(TypeName = "dbo.T_Unsafe;DROP")]
    private sealed record UnsafeAttributeOrderItemRow(int Id);
}
