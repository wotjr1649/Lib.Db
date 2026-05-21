using System.Data;
using System.Data.Common;
using Lib.Db;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Tvp;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class TvpSchemaDescriptorBindingTests
{
    [Fact]
    public void BindRawParameter_ShouldBindDescriptorTvpWithMixedScalarParameters()
    {
        TvpTypeName typeName = TvpTypeName.Parse("dbo.T_DescriptorOrder");
        TvpColumnMetadata[] columns =
        [
            Column("Id", 0, SqlDbType.Int),
            Column("Sku", 1, SqlDbType.NVarChar, maxLength: 64)
        ];
        TvpSchemaDescriptor descriptor = new(
            typeName,
            VersionToken: 11,
            columns,
            TvpSchemaFingerprint.Compute(typeName, 11, columns));
        var rows = new[] { new DescriptorOrderRow("A100", 7) };

        using var command = new SqlCommand();
        DbBinder.BindRawParameter(command, "OrderId", 123);
        DbBinder.BindRawParameter(command, "Rows", LibDb.Tvp(descriptor, rows));
        DbBinder.BindRawParameter(command, "RequestedBy", "system");

        command.Parameters["@OrderId"].Value.Should().Be(123);
        command.Parameters["@RequestedBy"].Value.Should().Be("system");

        SqlParameter rowsParameter = command.Parameters["@Rows"];
        rowsParameter.SqlDbType.Should().Be(SqlDbType.Structured);
        rowsParameter.TypeName.Should().Be("dbo.T_DescriptorOrder");
        DbDataReader reader = rowsParameter.Value.Should().BeAssignableTo<DbDataReader>().Subject;

        reader.Read().Should().BeTrue();
        reader.GetName(0).Should().Be("Id");
        reader.GetName(1).Should().Be("Sku");
        reader.GetValue(0).Should().Be(7);
        reader.GetValue(1).Should().Be("A100");
    }

    [Fact]
    public void BindParameter_ShouldNotReprocessExplicitStaticShapeTvpAsLegacyCollection()
    {
        TvpShape<DescriptorOrderRow> shape = TvpShape.For<DescriptorOrderRow>()
            .Column("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
            .Build();
        var rows = new[] { new DescriptorOrderRow("A100", 7) };
        SpParameterMetadata meta = new(
            Name: "@Rows",
            UdtTypeName: "dbo.T_DescriptorOrder",
            Size: 0,
            SqlDbType: SqlDbType.Structured,
            Direction: ParameterDirection.Input,
            Precision: 0,
            Scale: 0,
            IsNullable: false,
            HasDefaultValue: false);

        using var command = new SqlCommand();
        DbBinder.BindParameter(command, meta, LibDb.Tvp("dbo.T_DescriptorOrder", rows, shape), strictCheck: true);

        SqlParameter parameter = command.Parameters["@Rows"];
        parameter.SqlDbType.Should().Be(SqlDbType.Structured);
        parameter.TypeName.Should().Be("dbo.T_DescriptorOrder");
        parameter.Value.Should().BeAssignableTo<IEnumerable<SqlDataRecord>>();
    }

    private static TvpColumnMetadata Column(
        string name,
        int ordinal,
        SqlDbType sqlDbType,
        long maxLength = 0)
        => new(
            Name: name,
            NameHash: 0,
            MaxLength: maxLength,
            Ordinal: ordinal,
            SqlDbType: sqlDbType,
            Precision: 0,
            Scale: 0,
            IsIdentity: false,
            IsComputed: false,
            IsNullable: false);

    private sealed record DescriptorOrderRow(string Sku, int Id);
}
