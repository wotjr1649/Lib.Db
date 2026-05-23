using System.Data;
using Lib.Db.Execution.Bulk;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class BulkSqlBuilderTests
{
    [Theory]
    [InlineData("Products", "[dbo].[Products]")]
    [InlineData("sales.Products", "[sales].[Products]")]
    public void ParseTableName_ShouldRenderSafeTwoPartName(string input, string expected)
    {
        BulkIdentifier.ParseTableName(input).ToSql().Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("server.database.schema.table")]
    [InlineData("dbo.Products;DELETE FROM dbo.Products")]
    [InlineData("dbo.Products -- comment")]
    [InlineData("dbo.Products/*comment*/")]
    [InlineData("[dbo].[Products]")]
    [InlineData("[Products]")]
    [InlineData(".Products")]
    [InlineData("Products.")]
    [InlineData("dbo..Products")]
    [InlineData("dbo .Products")]
    [InlineData("dbo. Products")]
    [InlineData("dbo.Products Archive")]
    public void ParseTableName_ShouldRejectUnsafeNames(string input)
    {
        Action act = () => BulkIdentifier.ParseTableName(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseTableName_ShouldRejectIdentifierPartsLongerThanSysname()
    {
        string tooLong = new('A', 129);

        Action act = () => BulkIdentifier.ParseTableName($"dbo.{tooLong}");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*128*");
    }

    [Fact]
    public void Quote_ShouldEscapeClosingBracketsForNormalizedIdentifiers()
    {
        BulkIdentifier.Quote("Name]With]Bracket").Should().Be("[Name]]With]]Bracket]");
    }

    [Fact]
    public void CreateStageTable_ShouldRenderColumnTypesAndNullability()
    {
        BulkShape<BulkSqlRow> shape = BulkShape.For<BulkSqlRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Column("Payload", SqlDbType.VarBinary, static row => row.Payload, size: null)
            .Column("ChangedAtUtc", SqlDbType.DateTime2, static row => row.ChangedAtUtc, scale: 7)
            .Build();

        string sql = BulkSqlBuilder.CreateStageTable("#LibDbBulk_Test", shape.Columns);

        sql.Should().Contain("CREATE TABLE #LibDbBulk_Test");
        sql.Should().Contain("[Id] int NOT NULL");
        sql.Should().Contain("[Sku] nvarchar(64) NOT NULL");
        sql.Should().Contain("[Price] decimal(18,2) NULL");
        sql.Should().Contain("[Payload] varbinary(max) NULL");
        sql.Should().Contain("[ChangedAtUtc] datetime2(7) NULL");
        sql.Should().NotContain("decimal(18,0)");
    }

    [Fact]
    public void Render_ShouldRequireExplicitDecimalPrecisionAndScale()
    {
        BulkShape<BulkSqlRow> shape = BulkShape.For<BulkSqlRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 19, scale: 4)
            .Build();

        BulkSqlTypeRenderer.Render(shape.Columns[1]).Should().Be("decimal(19,4)");
    }

    [Fact]
    public void CreateUniqueStageKeyIndex_ShouldRenderKeyColumnsWithoutIgnoreDuplicateKey()
    {
        BulkShape<BulkSqlRow> shape = BulkShape.For<BulkSqlRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Build();

        string sql = BulkSqlBuilder.CreateUniqueStageKeyIndex("#LibDbBulk_Test", shape);

        sql.Should().Be("CREATE UNIQUE INDEX [IX_LibDbBulk_Key] ON #LibDbBulk_Test ([Id]);");
        sql.Should().NotContain("IGNORE_DUP_KEY");
    }

    [Fact]
    public void CreateUniqueStageKeyIndex_ShouldRejectShapeWithoutKeys()
    {
        BulkShape<BulkSqlRow> shape = BulkShape.For<BulkSqlRow>()
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
            .Build();

        Action act = () => BulkSqlBuilder.CreateUniqueStageKeyIndex("#LibDbBulk_Test", shape);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*key*");
    }

    [Fact]
    public void BuilderSql_ShouldNotEmitMergeOrRowValues()
    {
        const string rowValue = "SKU-42";
        BulkShape<BulkSqlRow> shape = BulkShape.For<BulkSqlRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
            .Build();

        string tableSql = BulkSqlBuilder.CreateStageTable("#LibDbBulk_Test", shape.Columns);
        string indexSql = BulkSqlBuilder.CreateUniqueStageKeyIndex("#LibDbBulk_Test", shape);
        string combinedSql = tableSql + indexSql;

        combinedSql.Should().NotContain("MERGE");
        combinedSql.Should().NotContain(rowValue);
    }

    private sealed record BulkSqlRow(int Id, string Sku, decimal Price, byte[] Payload, DateTime ChangedAtUtc);
}
