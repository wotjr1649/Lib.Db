using System.Data;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Tvp;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class TvpRowAccessorCacheTests
{
    [Fact]
    public void GetOrAdd_ShouldCacheShapeByRowTypeTypeNameFingerprintAndPolicy()
    {
        TvpSchemaDescriptor descriptor = Descriptor(
            Column("Id", 0, SqlDbType.Int),
            Column("Sku", 1, SqlDbType.NVarChar, maxLength: 64, isNullable: true));

        RuntimeTvpRowShape first = TvpRowAccessorCache.GetOrAdd(typeof(CacheRow), descriptor, TvpBindingPolicy.Adaptive);
        RuntimeTvpRowShape second = TvpRowAccessorCache.GetOrAdd(typeof(CacheRow), descriptor, TvpBindingPolicy.Adaptive);

        second.Should().BeSameAs(first);
        first.Columns.Select(column => column.Name).Should().Equal("Id", "Sku");
    }

    [Fact]
    public void GetOrAdd_ShouldAllowMissingNullableColumnsInAdaptivePolicy()
    {
        TvpSchemaDescriptor descriptor = Descriptor(
            Column("Id", 0, SqlDbType.Int),
            Column("OptionalNote", 1, SqlDbType.NVarChar, maxLength: 128, isNullable: true));
        RuntimeTvpRowShape shape = TvpRowAccessorCache.GetOrAdd(typeof(MissingOptionalRow), descriptor, TvpBindingPolicy.Adaptive);
        var rows = new[] { new MissingOptionalRow(42) };

        using RuntimeTvpDataReader reader = RuntimeTvpDataReader.Create(rows, shape);

        reader.Read().Should().BeTrue();
        reader.GetValue(0).Should().Be(42);
        reader.GetValue(1).Should().Be(DBNull.Value);
    }

    [Fact]
    public void GetOrAdd_ShouldMapUnicodeMaxLengthBytesToCharacterSize()
    {
        TvpSchemaDescriptor descriptor = Descriptor(
            Column("Sku", 0, SqlDbType.NVarChar, maxLength: 128),
            Column("Code", 1, SqlDbType.VarChar, maxLength: 128));

        RuntimeTvpRowShape shape = TvpRowAccessorCache.GetOrAdd(typeof(SizeRow), descriptor, TvpBindingPolicy.Strict);

        shape.Columns[0].Size.Should().Be(64);
        shape.Columns[1].Size.Should().Be(128);
    }

    [Fact]
    public void GetOrAdd_ShouldRejectMissingRequiredColumnsInStrictPolicy()
    {
        TvpSchemaDescriptor descriptor = Descriptor(
            Column("Id", 0, SqlDbType.Int),
            Column("RequiredQty", 1, SqlDbType.Int));

        Action act = () => TvpRowAccessorCache.GetOrAdd(typeof(MissingOptionalRow), descriptor, TvpBindingPolicy.Strict);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*RequiredQty*");
    }

    [Fact]
    public void Clear_ShouldRemoveSpecificTvpTypeEntries()
    {
        TvpSchemaDescriptor descriptor = Descriptor(Column("Id", 0, SqlDbType.Int));
        RuntimeTvpRowShape first = TvpRowAccessorCache.GetOrAdd(typeof(CacheRow), descriptor, TvpBindingPolicy.Strict);

        TvpRowAccessorCache.Clear(descriptor.TypeName);
        RuntimeTvpRowShape second = TvpRowAccessorCache.GetOrAdd(typeof(CacheRow), descriptor, TvpBindingPolicy.Strict);

        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void Clear_ShouldKeepOtherTvpTypeEntries()
    {
        TvpSchemaDescriptor target = Descriptor("dbo.T_CacheRow", Column("Id", 0, SqlDbType.Int));
        TvpSchemaDescriptor other = Descriptor("dbo.T_OtherCacheRow", Column("Id", 0, SqlDbType.Int));

        RuntimeTvpRowShape targetFirst = TvpRowAccessorCache.GetOrAdd(typeof(CacheRow), target, TvpBindingPolicy.Strict);
        RuntimeTvpRowShape otherFirst = TvpRowAccessorCache.GetOrAdd(typeof(CacheRow), other, TvpBindingPolicy.Strict);

        TvpRowAccessorCache.Clear(target.TypeName);

        RuntimeTvpRowShape targetSecond = TvpRowAccessorCache.GetOrAdd(typeof(CacheRow), target, TvpBindingPolicy.Strict);
        RuntimeTvpRowShape otherSecond = TvpRowAccessorCache.GetOrAdd(typeof(CacheRow), other, TvpBindingPolicy.Strict);

        targetSecond.Should().NotBeSameAs(targetFirst);
        otherSecond.Should().BeSameAs(otherFirst);
    }

    private static TvpSchemaDescriptor Descriptor(params TvpColumnMetadata[] columns)
        => Descriptor("dbo.T_CacheRow", columns);

    private static TvpSchemaDescriptor Descriptor(string typeNameValue, params TvpColumnMetadata[] columns)
    {
        TvpTypeName typeName = TvpTypeName.Parse(typeNameValue);
        string fingerprint = TvpSchemaFingerprint.Compute(typeName, versionToken: 3, columns);

        return new TvpSchemaDescriptor(typeName, VersionToken: 3, columns, fingerprint);
    }

    private static TvpColumnMetadata Column(
        string name,
        int ordinal,
        SqlDbType sqlDbType,
        long maxLength = 0,
        bool isNullable = false)
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
            IsNullable: isNullable);

    private sealed record CacheRow(int Id, string Sku);

    private sealed record MissingOptionalRow(int Id);

    private sealed record SizeRow(string Sku, string Code);
}
