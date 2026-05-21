// ============================================================================
// 파일: Binding/MappersTests.cs
// 설명: MapperFactory 매핑 단위 테스트 (Null, Enum, Decimal, Guid, 호환 타입)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using Lib.Db.Core;
using Lib.Db.Execution.Binding;
using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.Binding;

public sealed class MappersTests
{
    private readonly MapperFactory _factory;
    private readonly Mock<IServiceProvider> _spMock;
    private readonly LibDbOptions _options;

    public MappersTests()
    {
        _spMock = new Mock<IServiceProvider>();
        _options = new LibDbOptions();
        _factory = new MapperFactory(_spMock.Object, _options);
    }

    #region MP-01: Null Handling

    public sealed class NullableDto
    {
        public int? NullableInt { get; set; }
        public string? NullableString { get; set; }
    }

    [Fact]
    public void MP01_Map_Null_To_Nullable_ShouldWork()
    {
        Mock<DbDataReader> readerMock = new();
        readerMock.Setup(r => r.FieldCount).Returns(2);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(NullableDto.NullableInt));
        readerMock.Setup(r => r.GetName(1)).Returns(nameof(NullableDto.NullableString));

        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
        readerMock.Setup(r => r.GetFieldType(1)).Returns(typeof(string));

        readerMock.Setup(r => r.IsDBNull(0)).Returns(true);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(true);

        readerMock.Setup(r => r.GetValue(0)).Returns(DBNull.Value);
        readerMock.Setup(r => r.GetValue(1)).Returns(DBNull.Value);

        Lib.Db.Contracts.Mapping.ISqlMapper<NullableDto> mapper = _factory.GetMapper<NullableDto>();

        NullableDto result = mapper.MapResult(readerMock.Object);

        Assert.NotNull(result);
        Assert.Null(result.NullableInt);
        Assert.Null(result.NullableString);
    }

    #endregion

    #region MP-02: Enum Handling

    public enum TestEnum { A = 1, B = 2 }

    public sealed class EnumDto
    {
        public TestEnum EnumVal { get; set; }
        public TestEnum? NullableEnum { get; set; }
    }

    [Fact]
    public void MP02_Map_Enum_Underlying_ShouldWork()
    {
        Mock<DbDataReader> readerMock = new();
        readerMock.Setup(r => r.FieldCount).Returns(2);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(EnumDto.EnumVal));
        readerMock.Setup(r => r.GetName(1)).Returns(nameof(EnumDto.NullableEnum));

        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
        readerMock.Setup(r => r.GetFieldType(1)).Returns(typeof(int));

        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(false);

        readerMock.Setup(r => r.GetValue(0)).Returns(1);
        readerMock.Setup(r => r.GetValue(1)).Returns(2);

        Lib.Db.Contracts.Mapping.ISqlMapper<EnumDto> mapper = _factory.GetMapper<EnumDto>();

        EnumDto result = mapper.MapResult(readerMock.Object);

        Assert.Equal(TestEnum.A, result.EnumVal);
        Assert.Equal(TestEnum.B, result.NullableEnum);
    }

    #endregion

    #region MP-03: Decimal / Precision

    public sealed class DecimalDto
    {
        public decimal Money { get; set; }
    }

    [Fact]
    public void MP03_Map_Decimal_Precision_ShouldWork()
    {
        Mock<DbDataReader> readerMock = new();
        readerMock.Setup(r => r.FieldCount).Returns(1);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(DecimalDto.Money));
        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(decimal));
        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.GetDecimal(0)).Returns(123.45m);

        Lib.Db.Contracts.Mapping.ISqlMapper<DecimalDto> mapper = _factory.GetMapper<DecimalDto>();

        DecimalDto result = mapper.MapResult(readerMock.Object);

        Assert.Equal(123.45m, result.Money);
    }

    #endregion

    #region MP-04: Guid from String

    public sealed class GuidDto
    {
        public Guid Id { get; set; }
    }

    [Fact]
    public void MP04_Map_Guid_FromString_ShouldWork()
    {
        string guidStr = "d9f6782c-3965-4f48-9366-51543b57e753";
        Guid expected = Guid.Parse(guidStr);

        Mock<DbDataReader> readerMock = new();
        readerMock.Setup(r => r.FieldCount).Returns(1);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(GuidDto.Id));

        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(string));
        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.GetValue(0)).Returns(guidStr);
        readerMock.Setup(r => r.GetString(0)).Returns(guidStr);

        Lib.Db.Contracts.Mapping.ISqlMapper<GuidDto> mapper = _factory.GetMapper<GuidDto>();

        GuidDto result = mapper.MapResult(readerMock.Object);
        Assert.Equal(expected, result.Id);
    }

    #endregion

    #region MP-05: Compatible Types

    public sealed class CompatibleDto
    {
        public double DoubleVal { get; set; }
        public int IntVal { get; set; }
    }

    [Fact]
    public void MP05_Map_Compatible_Types_ShouldWork()
    {
        Mock<DbDataReader> readerMock = new();
        readerMock.Setup(r => r.FieldCount).Returns(2);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(CompatibleDto.DoubleVal));
        readerMock.Setup(r => r.GetName(1)).Returns(nameof(CompatibleDto.IntVal));

        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(float));
        readerMock.Setup(r => r.GetFieldType(1)).Returns(typeof(byte));

        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(false);

        readerMock.Setup(r => r.GetValue(0)).Returns(1.23f);
        readerMock.Setup(r => r.GetValue(1)).Returns((byte)255);

        Lib.Db.Contracts.Mapping.ISqlMapper<CompatibleDto> mapper = _factory.GetMapper<CompatibleDto>();

        CompatibleDto result = mapper.MapResult(readerMock.Object);

        Assert.Equal(1.2300000190734863, result.DoubleVal);
        Assert.Equal(255, result.IntVal);
    }

    #endregion

    #region MP-06: SQL Name Convention

    public sealed record SuspendRow(int CellNo, string SlotName);

    [Fact]
    public void MP06_Map_SnakeCaseColumns_ToPascalCasePositionalRecord_ShouldWork()
    {
        Mock<DbDataReader> readerMock = new();
        readerMock.Setup(r => r.FieldCount).Returns(2);
        readerMock.Setup(r => r.GetName(0)).Returns("CELL_NO");
        readerMock.Setup(r => r.GetName(1)).Returns("SLOT_NAME");
        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
        readerMock.Setup(r => r.GetFieldType(1)).Returns(typeof(string));
        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(false);
        readerMock.Setup(r => r.GetInt32(0)).Returns(7);
        readerMock.Setup(r => r.GetString(1)).Returns("A01");

        Lib.Db.Contracts.Mapping.ISqlMapper<SuspendRow> mapper = _factory.GetMapper<SuspendRow>();

        SuspendRow result = mapper.MapResult(readerMock.Object);

        Assert.Equal(7, result.CellNo);
        Assert.Equal("A01", result.SlotName);
    }

    [Fact]
    public void MP07_GeneratedResultMapper_ShouldAcceptDbDataReaderWrapper()
    {
        Mock<DbDataReader> readerMock = new();
        readerMock.Setup(r => r.FieldCount).Returns(4);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(DbResultUser.UserId));
        readerMock.Setup(r => r.GetName(1)).Returns(nameof(DbResultUser.UserName));
        readerMock.Setup(r => r.GetName(2)).Returns(nameof(DbResultUser.Email));
        readerMock.Setup(r => r.GetName(3)).Returns(nameof(DbResultUser.Age));
        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(2)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(3)).Returns(true);
        readerMock.Setup(r => r.GetInt32(0)).Returns(42);
        readerMock.Setup(r => r.GetString(1)).Returns("user-42");
        readerMock.Setup(r => r.GetString(2)).Returns("user42@example.test");

        Lib.Db.Contracts.Mapping.ISqlMapper<DbResultUser> mapper = _factory.GetMapper<DbResultUser>();

        DbResultUser result = mapper.MapResult(readerMock.Object);

        Assert.Equal(42, result.UserId);
        Assert.Equal("user-42", result.UserName);
        Assert.Equal("user42@example.test", result.Email);
        Assert.Null(result.Age);
    }

    [Fact]
    public void MP08_Map_DuplicateNormalizedColumns_ShouldUseFirstMappedColumn()
    {
        Mock<DbDataReader> readerMock = new();
        readerMock.Setup(r => r.FieldCount).Returns(2);
        readerMock.Setup(r => r.GetName(0)).Returns("CELL_NO");
        readerMock.Setup(r => r.GetName(1)).Returns("CellNo");
        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
        readerMock.Setup(r => r.GetFieldType(1)).Returns(typeof(int));
        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(false);
        readerMock.Setup(r => r.GetInt32(0)).Returns(7);
        readerMock.Setup(r => r.GetInt32(1)).Returns(99);

        Lib.Db.Contracts.Mapping.ISqlMapper<DuplicateColumnRow> mapper = _factory.GetMapper<DuplicateColumnRow>();

        DuplicateColumnRow result = mapper.MapResult(readerMock.Object);

        Assert.Equal(7, result.CellNo);
    }

    public sealed class DuplicateColumnRow
    {
        public int CellNo { get; set; }
    }

    #endregion
}
