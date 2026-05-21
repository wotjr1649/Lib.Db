using System.Data;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Tvp;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class TvpSchemaFingerprintTests
{
    [Fact]
    public void Compute_ShouldReturnSameFingerprintForSameSchema()
    {
        TvpTypeName typeName = TvpTypeName.Parse("dbo.T_OrderItem");
        TvpColumnMetadata[] columns =
        [
            Column("Sku", 1, SqlDbType.NVarChar, maxLength: 64, isNullable: true),
            Column("Id", 0, SqlDbType.Int)
        ];
        TvpColumnMetadata[] sameColumnsDifferentArrayOrder =
        [
            Column("Id", 0, SqlDbType.Int),
            Column("Sku", 1, SqlDbType.NVarChar, maxLength: 64, isNullable: true)
        ];

        string first = TvpSchemaFingerprint.Compute(typeName, versionToken: 7, columns);
        string second = TvpSchemaFingerprint.Compute(typeName, versionToken: 7, sameColumnsDifferentArrayOrder);

        second.Should().Be(first);
    }

    [Fact]
    public void Compute_ShouldChangeWhenNullableTypeLengthOrOrdinalChanges()
    {
        TvpTypeName typeName = TvpTypeName.Parse("dbo.T_OrderItem");
        TvpColumnMetadata[] baseline =
        [
            Column("Id", 0, SqlDbType.Int),
            Column("Sku", 1, SqlDbType.NVarChar, maxLength: 64, isNullable: true)
        ];

        string fingerprint = TvpSchemaFingerprint.Compute(typeName, versionToken: 7, baseline);

        TvpSchemaFingerprint.Compute(typeName, 7, WithSecondColumn(baseline, Column("Sku", 1, SqlDbType.NVarChar, maxLength: 64)))
            .Should()
            .NotBe(fingerprint);
        TvpSchemaFingerprint.Compute(typeName, 7, WithSecondColumn(baseline, Column("Sku", 1, SqlDbType.VarChar, maxLength: 64, isNullable: true)))
            .Should()
            .NotBe(fingerprint);
        TvpSchemaFingerprint.Compute(typeName, 7, WithSecondColumn(baseline, Column("Sku", 1, SqlDbType.NVarChar, maxLength: 128, isNullable: true)))
            .Should()
            .NotBe(fingerprint);
        TvpSchemaFingerprint.Compute(typeName, 7, WithSecondColumn(baseline, Column("Sku", 2, SqlDbType.NVarChar, maxLength: 64, isNullable: true)))
            .Should()
            .NotBe(fingerprint);
    }

    private static TvpColumnMetadata[] WithSecondColumn(
        TvpColumnMetadata[] columns,
        TvpColumnMetadata replacement)
        => [columns[0], replacement];

    private static TvpColumnMetadata Column(
        string name,
        int ordinal,
        SqlDbType sqlDbType,
        long maxLength = 0,
        byte precision = 0,
        byte scale = 0,
        bool isIdentity = false,
        bool isComputed = false,
        bool isNullable = false)
        => new(
            Name: name,
            NameHash: 0,
            MaxLength: maxLength,
            Ordinal: ordinal,
            SqlDbType: sqlDbType,
            Precision: precision,
            Scale: scale,
            IsIdentity: isIdentity,
            IsComputed: isComputed,
            IsNullable: isNullable);
}
