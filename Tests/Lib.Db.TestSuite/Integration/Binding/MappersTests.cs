using System.Data;
using System.Data.Common;
using Lib.Db.Core; // For LibDbOptions
using Lib.Db.Execution.Binding;
using Moq;
using Xunit;

namespace Lib.Db.Verification.Tests.Binding;

public class MappersTests
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

    // =========================================================================
    // MP-01: Null Handling
    // =========================================================================
    public class NullableDto
    {
        public int? NullableInt { get; set; }
        public string? NullableString { get; set; }
    }

    [Fact]
    public void MP01_Map_Null_To_Nullable_ShouldWork()
    {
        // Arrange
        var readerMock = new Mock<DbDataReader>();
        readerMock.Setup(r => r.FieldCount).Returns(2);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(NullableDto.NullableInt));
        readerMock.Setup(r => r.GetName(1)).Returns(nameof(NullableDto.NullableString));
        
        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
        readerMock.Setup(r => r.GetFieldType(1)).Returns(typeof(string));

        readerMock.Setup(r => r.IsDBNull(0)).Returns(true);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(true);

        readerMock.Setup(r => r.GetValue(0)).Returns(DBNull.Value);
        readerMock.Setup(r => r.GetValue(1)).Returns(DBNull.Value);

        var mapper = _factory.GetMapper<NullableDto>();

        // Act
        var result = mapper.MapResult(readerMock.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.NullableInt);
        Assert.Null(result.NullableString);
    }

    // =========================================================================
    // MP-02: Enum Handling
    // =========================================================================
    public enum TestEnum { A = 1, B = 2 }

    public class EnumDto
    {
        public TestEnum EnumVal { get; set; }
        public TestEnum? NullableEnum { get; set; }
    }

    [Fact]
    public void MP02_Map_Enum_Underlying_ShouldWork()
    {
        // Arrange
        var readerMock = new Mock<DbDataReader>();
        readerMock.Setup(r => r.FieldCount).Returns(2);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(EnumDto.EnumVal));
        readerMock.Setup(r => r.GetName(1)).Returns(nameof(EnumDto.NullableEnum));

        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
        readerMock.Setup(r => r.GetFieldType(1)).Returns(typeof(int));

        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(false);

        readerMock.Setup(r => r.GetValue(0)).Returns(1); // A
        readerMock.Setup(r => r.GetValue(1)).Returns(2); // B

        var mapper = _factory.GetMapper<EnumDto>();

        // Act
        var result = mapper.MapResult(readerMock.Object);

        // Assert
        Assert.Equal(TestEnum.A, result.EnumVal);
        Assert.Equal(TestEnum.B, result.NullableEnum);
    }

    // =========================================================================
    // MP-03: Decimal / Precision
    // =========================================================================
    public class DecimalDto
    {
        public decimal Money { get; set; }
    }

    [Fact]
    public void MP03_Map_Decimal_Precision_ShouldWork()
    {
        // Arrange
        var readerMock = new Mock<DbDataReader>();
        readerMock.Setup(r => r.FieldCount).Returns(1);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(DecimalDto.Money));
        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(decimal));
        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.GetDecimal(0)).Returns(123.45m); // Typed Getter use

        var mapper = _factory.GetMapper<DecimalDto>();

        // Act
        var result = mapper.MapResult(readerMock.Object);

        // Assert
        Assert.Equal(123.45m, result.Money);
    }

    // =========================================================================
    // MP-04: Guid from String (UUID case)
    // =========================================================================
    public class GuidDto
    {
        public Guid Id { get; set; }
    }

    [Fact]
    public void MP04_Map_Guid_FromString_ShouldWork()
    {
        // Arrange
        var guidStr = "d9f6782c-3965-4f48-9366-51543b57e753";
        var expected = Guid.Parse(guidStr);

        var readerMock = new Mock<DbDataReader>();
        readerMock.Setup(r => r.FieldCount).Returns(1);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(GuidDto.Id));
        
        // DB returns String (Legacy DB or non-MSSQL scenario emulation)
        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(string));
        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.GetValue(0)).Returns(guidStr);
        readerMock.Setup(r => r.GetString(0)).Returns(guidStr); // Required for ExpressionTreeMapper optimization

        var mapper = _factory.GetMapper<GuidDto>();

        // Act
        // This relies on ExpressionTreeMapper using GetValue -> Convert
        // Expression.Convert(string, Guid) is NOT supported natively by CLR.
        // If this throws, we need to fix Mappers.cs to support string->guid parsing.
        try
        {
            var result = mapper.MapResult(readerMock.Object);
            Assert.Equal(expected, result.Id);
        }
        catch (InvalidOperationException) 
        {
            // Expected failure if logic is missing. Marking logic as 'To Implement' if blocked.
            // But user requested PASS. I will assert failing behavior if I can't fix code yet?
            // User: "구현하고 PASS를 확보한다". So I MUST FIX IT if it fails.
            throw; 
        }
    }

    // =========================================================================
    // MP-05: Compatible Types (Float -> Double, Byte -> Int)
    // =========================================================================
    public class CompatibleDto
    {
        public double DoubleVal { get; set; }
        public int IntVal { get; set; }
    }

    [Fact]
    public void MP05_Map_Compatible_Types_ShouldWork()
    {
        // Arrange
        var readerMock = new Mock<DbDataReader>();
        readerMock.Setup(r => r.FieldCount).Returns(2);
        readerMock.Setup(r => r.GetName(0)).Returns(nameof(CompatibleDto.DoubleVal));
        readerMock.Setup(r => r.GetName(1)).Returns(nameof(CompatibleDto.IntVal));

        // DB gives float (Real) for double property, byte (TinyInt) for int property
        readerMock.Setup(r => r.GetFieldType(0)).Returns(typeof(float));
        readerMock.Setup(r => r.GetFieldType(1)).Returns(typeof(byte));

        readerMock.Setup(r => r.IsDBNull(0)).Returns(false);
        readerMock.Setup(r => r.IsDBNull(1)).Returns(false);

        readerMock.Setup(r => r.GetValue(0)).Returns(1.23f);
        readerMock.Setup(r => r.GetValue(1)).Returns((byte)255);

        var mapper = _factory.GetMapper<CompatibleDto>();

        // Act
        var result = mapper.MapResult(readerMock.Object);

        // Assert
        Assert.Equal(1.2300000190734863, result.DoubleVal); // float -> double precision
        Assert.Equal(255, result.IntVal);
    }
}
