using System.Data;
using Lib.Db.Execution.Bulk;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class BulkShapeTests
{
    [Fact]
    public void BulkShape_ShouldBuildColumnsAndKeysWithoutReflection()
    {
        BulkShape<BulkShapeRow> shape = BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100, nullable: false)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Build();

        shape.Columns.Should().HaveCount(3);
        shape.KeyColumns.Should().ContainSingle(column => column.DestinationName == "Id");
        shape.WritableColumns.Should().Contain(column => column.DestinationName == "Name");
        shape.WritableColumns.Should().Contain(column => column.DestinationName == "Price");

        BulkColumn<BulkShapeRow> nameColumn = shape.Columns[1];
        nameColumn.Ordinal.Should().Be(1);
        nameColumn.DestinationName.Should().Be("Name");
        nameColumn.SqlDbType.Should().Be(SqlDbType.NVarChar);
        nameColumn.Nullable.Should().BeFalse();
        nameColumn.IsKey.Should().BeFalse();
        nameColumn.Size.Should().Be(100);
        nameColumn.GetValue(new BulkShapeRow(1, "sku", 12.34m)).Should().Be("sku");

        BulkColumn<BulkShapeRow> priceColumn = shape.Columns[2];
        priceColumn.Precision.Should().Be(18);
        priceColumn.Scale.Should().Be(2);
    }

    [Fact]
    public void BulkShape_ShouldBeImmutableAfterBuild()
    {
        BulkShapeBuilder<BulkShapeRow> builder = BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id);

        BulkShape<BulkShapeRow> shape = builder.Build();
        builder.Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 50);

        shape.Columns.Should().ContainSingle(column => column.DestinationName == "Id");
    }

    [Fact]
    public void Build_ShouldRejectEmptyShape()
    {
        Action act = () => BulkShape.For<BulkShapeRow>().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one column*");
    }

    [Fact]
    public void Build_ShouldRejectDuplicateDestinationColumns()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("id", SqlDbType.Int, static row => row.Id)
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate*Id*");
    }

    [Fact]
    public void ValidateForMutation_ShouldRequireAtLeastOneKey()
    {
        BulkShape<BulkShapeRow> shape = BulkShape.For<BulkShapeRow>()
            .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100)
            .Build();

        Action act = () => shape.ValidateForMutation();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mutation*key*");
    }

    [Fact]
    public void Build_ShouldRejectNullableKeyColumns()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id, nullable: true)
            .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100, nullable: false)
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*key*Id*non-null*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Sku Name")]
    [InlineData("Sku;DROP")]
    [InlineData("Sku--Comment")]
    [InlineData("[Sku")]
    [InlineData("Sku]")]
    public void Build_ShouldRejectUnsafeDestinationColumnNames(string destinationName)
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column(destinationName, SqlDbType.NVarChar, static row => row.Name, size: 100, nullable: false)
            .Build();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(18, null)]
    public void Build_ShouldRejectDecimalWithoutPrecisionAndScale(int? precision, int? scale)
    {
        byte? declaredPrecision = precision is null ? null : (byte)precision.Value;
        byte? declaredScale = scale is null ? null : (byte)scale.Value;

        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: declaredPrecision, scale: declaredScale)
            .Build();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Decimal*precision*scale*");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(39, 0)]
    [InlineData(18, 19)]
    public void Build_ShouldRejectInvalidDecimalPrecisionAndScale(byte precision, byte scale)
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: precision, scale: scale)
            .Build();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(SqlDbType.NVarChar, 0)]
    [InlineData(SqlDbType.NVarChar, 4001)]
    [InlineData(SqlDbType.VarChar, 0)]
    [InlineData(SqlDbType.VarChar, 8001)]
    public void Build_ShouldRejectInvalidStringSize(SqlDbType sqlDbType, int size)
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Name", sqlDbType, static row => row.Name, size: size, nullable: false)
            .Build();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8001)]
    public void Build_ShouldRejectInvalidBinarySize(int size)
    {
        Action act = () => BulkShape.For<BulkShapeBinaryRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Payload", SqlDbType.VarBinary, static row => row.Payload, size: size, nullable: false)
            .Build();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(SqlDbType.Time, 8)]
    [InlineData(SqlDbType.DateTime2, 8)]
    [InlineData(SqlDbType.DateTimeOffset, 8)]
    public void Build_ShouldRejectInvalidTemporalScale(SqlDbType sqlDbType, byte scale)
    {
        Action act = sqlDbType switch
        {
            SqlDbType.Time => () => BulkShape.For<BulkShapeTemporalRow>()
                .Key("Id", SqlDbType.Int, static row => row.Id)
                .Column("StartsAt", SqlDbType.Time, static row => row.StartsAt, scale: scale)
                .Build(),
            SqlDbType.DateTime2 => () => BulkShape.For<BulkShapeTemporalRow>()
                .Key("Id", SqlDbType.Int, static row => row.Id)
                .Column("ChangedAtUtc", SqlDbType.DateTime2, static row => row.ChangedAtUtc, scale: scale)
                .Build(),
            SqlDbType.DateTimeOffset => () => BulkShape.For<BulkShapeTemporalRow>()
                .Key("Id", SqlDbType.Int, static row => row.Id)
                .Column("ChangedAtOffset", SqlDbType.DateTimeOffset, static row => row.ChangedAtOffset, scale: scale)
                .Build(),
            _ => throw new InvalidOperationException()
        };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Build_ShouldRejectMaxLengthKeyColumns()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Sku", SqlDbType.NVarChar, static row => row.Name, size: null)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*key*max*");
    }

    [Fact]
    public void Build_ShouldRejectStageKeyDeclaredLengthOverIndexLimit()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Sku", SqlDbType.NVarChar, static row => row.Name, size: 451)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*index key*900*");
    }

    [Fact]
    public void Build_ShouldRejectMoreThanThirtyTwoKeyColumns()
    {
        BulkShapeBuilder<BulkShapeRow> builder = BulkShape.For<BulkShapeRow>();
        for (int i = 0; i < 33; i++)
            builder.Key($"K{i}", SqlDbType.Int, static row => row.Id);

        builder.Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2);

        Action act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32-column*");
    }

    [Theory]
    [InlineData(SqlDbType.DateTime2)]
    [InlineData(SqlDbType.NVarChar)]
    [InlineData(SqlDbType.VarBinary)]
    public void Column_ShouldRejectDateOnlyExceptDate(SqlDbType sqlDbType)
    {
        Action act = () => BulkShape.For<BulkShapeDateOnlyRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("OnlyDate", sqlDbType, static row => row.OnlyDate);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*DateOnly*{sqlDbType}*");
    }

    [Theory]
    [InlineData(SqlDbType.Date)]
    [InlineData(SqlDbType.DateTime2)]
    public void Column_ShouldRejectTimeTypesExceptTime(SqlDbType sqlDbType)
    {
        Action act = () => BulkShape.For<BulkShapeTimeOnlyRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("OnlyTime", sqlDbType, static row => row.OnlyTime);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*TimeOnly*{sqlDbType}*");
    }

    [Fact]
    public void Column_ShouldRejectGuidExceptUniqueIdentifier()
    {
        Action act = () => BulkShape.For<BulkShapeGuidRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("ExternalId", SqlDbType.NVarChar, static row => row.ExternalId);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Guid*NVarChar*");
    }

    [Fact]
    public void Column_ShouldRejectStringExceptTextTypes()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Name", SqlDbType.Int, static row => row.Name);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*String*Int*");
    }

    [Fact]
    public void Column_ShouldRejectByteArrayExceptVarBinary()
    {
        Action act = () => BulkShape.For<BulkShapeBinaryRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Payload", SqlDbType.NVarChar, static row => row.Payload, size: 100);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Byte[]*NVarChar*");
    }

    [Theory]
    [InlineData(SqlDbType.Decimal)]
    [InlineData(SqlDbType.Money)]
    [InlineData(SqlDbType.SmallMoney)]
    public void Column_ShouldAcceptDecimalMoneyFamily(SqlDbType sqlDbType)
    {
        BulkShape<BulkShapeRow> shape = BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column(
                "Price",
                sqlDbType,
                static row => row.Price,
                precision: sqlDbType == SqlDbType.Decimal ? (byte)18 : null,
                scale: sqlDbType == SqlDbType.Decimal ? (byte)2 : null)
            .Build();

        shape.Columns[1].SqlDbType.Should().Be(sqlDbType);
    }

    [Fact]
    public void Column_ShouldRejectIntegerTypeMismatch()
    {
        Action act = () => BulkShape.For<BulkShapeLongRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Int64*Int*");
    }

    [Fact]
    public void Column_ShouldRejectEnumUnderlyingTypeMismatch()
    {
        Action act = () => BulkShape.For<BulkShapeEnumRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Status", SqlDbType.BigInt, static row => row.Status);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*enum*Int32*BigInt*");
    }

    [Fact]
    public void Column_ShouldUseMetadataSelectedConverters()
    {
        BulkShape<BulkShapeConverterRow> shape = BulkShape.For<BulkShapeConverterRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("OnlyDate", SqlDbType.Date, static row => row.OnlyDate)
            .Column("OnlyTime", SqlDbType.Time, static row => row.OnlyTime)
            .Column("Status", SqlDbType.Int, static row => row.Status)
            .Build();

        BulkShapeConverterRow row = new(1, new DateOnly(2026, 5, 23), new TimeOnly(14, 30), BulkShapeStatus.Active);

        shape.Columns[1].GetValue(row).Should().Be(new DateTime(2026, 5, 23));
        shape.Columns[2].GetValue(row).Should().Be(new TimeSpan(14, 30, 0));
        shape.Columns[3].GetValue(row).Should().Be(1);
    }

    [Fact]
    public void Build_ShouldRejectDestinationColumnNamesLongerThanSysname()
    {
        string tooLong = new('A', 129);

        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column(tooLong, SqlDbType.NVarChar, static row => row.Name, size: 100, nullable: false)
            .Build();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*128*");
    }

    [Fact]
    public void Column_ShouldRejectUnsupportedSqlDbTypeBeforeBuild()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Payload", SqlDbType.Structured, static row => row.Name);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Structured*not supported*");
    }

    [Fact]
    public void BulkWriteOptions_ShouldExposeSafeDefaults()
    {
        BulkWriteOptions options = new();

        options.BatchSize.Should().Be(5_000);
        options.TimeoutSeconds.Should().Be(600);
        options.EnableStreaming.Should().BeTrue();
        options.UseTransaction.Should().BeTrue();
        options.CheckConstraints.Should().BeTrue();
    }

    [Fact]
    public void BulkWriteOptions_ShouldNotRejectTransactionOptOutInScalarValidation()
    {
        BulkWriteOptions options = new() { UseTransaction = false };

        Action act = () => options.Validate();

        act.Should().NotThrow();
        options.UseTransaction.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BulkWriteOptions_ShouldRejectInvalidBatchSize(int batchSize)
    {
        BulkWriteOptions options = new() { BatchSize = batchSize };

        Action act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*batch size*greater than zero*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BulkWriteOptions_ShouldRejectInvalidTimeout(int timeoutSeconds)
    {
        BulkWriteOptions options = new() { TimeoutSeconds = timeoutSeconds };

        Action act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*timeout*greater than zero*");
    }

    [Fact]
    public void BulkMergeActions_ShouldDefaultToUpdateMatchedAndInsertMissing()
    {
        BulkMergeOptions options = new();

        options.Actions.Should().Be(BulkMergeActions.UpdateMatched | BulkMergeActions.InsertMissing);
    }

    [Fact]
    public void BulkMergeOptions_ShouldRejectNoActions()
    {
        BulkWriteOptions options = new BulkMergeOptions
        {
            Actions = BulkMergeActions.None
        };

        Action act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*actions*empty*");
    }

    [Fact]
    public void BulkMergeOptions_ShouldRejectDeleteNotMatchedBySourceInV240()
    {
        BulkWriteOptions options = new BulkMergeOptions
        {
            Actions = BulkMergeActions.DeleteNotMatchedBySource
        };

        Action act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DeleteNotMatchedBySource*not supported*v2.4.0*");
    }

    [Theory]
    [InlineData(BulkMergeActions.UpdateMatched | BulkMergeActions.DeleteMatched)]
    [InlineData(BulkMergeActions.InsertMissing | BulkMergeActions.DeleteMatched)]
    [InlineData(BulkMergeActions.UpdateMatched | BulkMergeActions.InsertMissing | BulkMergeActions.DeleteMatched)]
    public void BulkMergeOptions_ShouldRejectDeleteMatchedWithOtherActions(BulkMergeActions actions)
    {
        BulkWriteOptions options = new BulkMergeOptions { Actions = actions };

        Action act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DeleteMatched*exclusive*");
    }

    [Theory]
    [InlineData((BulkMergeActions)16)]
    [InlineData((BulkMergeActions)31)]
    public void BulkMergeOptions_ShouldRejectUnknownActionBits(BulkMergeActions actions)
    {
        BulkWriteOptions options = new BulkMergeOptions { Actions = actions };

        Action act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown*merge action*");
    }

    [Fact]
    public void BulkUpsertResult_ShouldExposeTotalAffected()
    {
        BulkUpsertResult result = new(Inserted: 3, Updated: 4);

        result.TotalAffected.Should().Be(7);
    }

    [Fact]
    public void BulkMergeResult_ShouldExposeTotalAffected()
    {
        BulkMergeResult result = new(Inserted: 3, Updated: 4, Deleted: 5);

        result.TotalAffected.Should().Be(12);
    }

    private sealed record BulkShapeRow(int Id, string Name, decimal Price);
    private sealed record BulkShapeBinaryRow(int Id, byte[] Payload);
    private sealed record BulkShapeTemporalRow(int Id, TimeSpan StartsAt, DateTime ChangedAtUtc, DateTimeOffset ChangedAtOffset);
    private sealed record BulkShapeDateOnlyRow(int Id, DateOnly OnlyDate);
    private sealed record BulkShapeTimeOnlyRow(int Id, TimeOnly OnlyTime);
    private sealed record BulkShapeGuidRow(int Id, Guid ExternalId);
    private sealed record BulkShapeLongRow(long Id);
    private sealed record BulkShapeEnumRow(int Id, BulkShapeStatus Status);
    private sealed record BulkShapeConverterRow(int Id, DateOnly OnlyDate, TimeOnly OnlyTime, BulkShapeStatus Status);
    private enum BulkShapeStatus { Pending = 0, Active = 1 }
}
