// ============================================================================
// 파일: Unit/MapperCoverageTests.cs
// 설명: 특수/대체 SQL 매퍼(Dictionary, DataRow, Reflection, GeneratedResult) 커버리지 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class MapperCoverageTests
{
    [Fact]
    public void DictionarySqlMapper_ShouldMapRawSchemaOutputAndDuplicateResultNames()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = 7
        };

        mapper.MapParameters(command, null!, schema: null);
        mapper.MapParameters(command, [], schema: null);
        mapper.MapParameters(command, values, schema: null);
        mapper.MapParameters(command, values, CreateSchema(
            Param("@Id", SqlDbType.Int),
            Param("@OutValue", SqlDbType.Int, direction: ParameterDirection.Output),
            Param("@Optional", SqlDbType.NVarChar, nullable: true)));

        command.Parameters["@OutValue"].Value = DBNull.Value;
        mapper.MapOutputParameters(command, values);
        mapper.MapOutputParameters(command, null!);

        values.Should().ContainKey("OutValue").WhoseValue.Should().BeNull();

        Mock<DbDataReader> reader = new();
        reader.Setup(r => r.FieldCount).Returns(4);
        reader.Setup(r => r.GetName(0)).Returns(" ");
        reader.Setup(r => r.GetName(1)).Returns("Col_1");
        reader.Setup(r => r.GetName(2)).Returns("Col");
        reader.Setup(r => r.GetName(3)).Returns("Col");
        reader.Setup(r => r.GetValue(0)).Returns(DBNull.Value);
        reader.Setup(r => r.GetValue(1)).Returns("existing");
        reader.Setup(r => r.GetValue(2)).Returns(1);
        reader.Setup(r => r.GetValue(3)).Returns(2);

        Dictionary<string, object?> row = mapper.MapResult(reader.Object);

        row["Column0"].Should().BeNull();
        row["Col_1"].Should().Be("existing");
        row["Col"].Should().Be(1);
        row.Values.Should().Contain(2);
    }

    [Fact]
    public void DictionarySqlMapper_ShouldThrowWhenRequiredKeyIsMissing()
    {
        var mapper = new DictionarySqlMapper(strict: true);
        using var command = new SqlCommand();

        Action act = () => mapper.MapParameters(
            command,
            [],
            CreateSchema(Param("@Required", SqlDbType.Int, nullable: false)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Required*");
    }

    [Fact]
    public void DataRowSqlMapper_ShouldMapRawSchemaAndRejectUnsupportedResultMapping()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(9);
        DataRow row = table.Rows[0];

        mapper.MapParameters(command, null!, schema: null);
        mapper.MapParameters(command, row, schema: null);
        mapper.MapParameters(command, row, CreateSchema(
            Param("@Id", SqlDbType.Int),
            Param("@OutValue", SqlDbType.Int, direction: ParameterDirection.Output),
            Param("@Optional", SqlDbType.NVarChar, nullable: true)));
        mapper.MapOutputParameters(command, row);

        command.Parameters.Count.Should().BeGreaterThan(0);
        mapper.Invoking(m => m.MapResult(Mock.Of<DbDataReader>()))
            .Should()
            .Throw<NotSupportedException>();
    }

    [Fact]
    public void DataRowSqlMapper_ShouldThrowWhenRequiredColumnIsMissing()
    {
        var mapper = new DataRowSqlMapper(strict: true);
        using var command = new SqlCommand();
        DataTable table = new();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);

        Action act = () => mapper.MapParameters(
            command,
            table.Rows[0],
            CreateSchema(Param("@Required", SqlDbType.Int, nullable: false)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Required*");
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldMapRawSchemaOutputAndRejectResultMapping()
    {
        var mapper = new ReflectionParameterMapper<ReflectionCoverageDto>(strict: true);
        using var command = new SqlCommand();
        var dto = new ReflectionCoverageDto
        {
            Id = 11,
            Name = "neo"
        };

        mapper.MapParameters(command, null!, schema: null);
        mapper.MapParameters(command, dto, schema: null);
        mapper.MapParameters(command, dto, CreateSchema(
            Param("@Id", SqlDbType.Int),
            Param("@OutValue", SqlDbType.Int, direction: ParameterDirection.Output),
            Param("@Defaulted", SqlDbType.Int, nullable: false, hasDefault: true),
            Param("@Optional", SqlDbType.NVarChar, nullable: true)));

        using var outputCommand = new SqlCommand();
        outputCommand.Parameters.Add(new SqlParameter("@OutValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 42
        });
        outputCommand.Parameters.Add(new SqlParameter("@NullValue", SqlDbType.NVarChar)
        {
            Direction = ParameterDirection.InputOutput,
            Value = DBNull.Value
        });

        mapper.MapOutputParameters(outputCommand, dto);
        mapper.MapOutputParameters(outputCommand, null!);

        dto.OutValue.Should().Be(42);
        dto.NullValue.Should().BeNull();
        mapper.Invoking(m => m.MapResult(Mock.Of<DbDataReader>()))
            .Should()
            .Throw<NotSupportedException>();
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldThrowWhenRequiredPropertyIsMissing()
    {
        var mapper = new ReflectionParameterMapper<ReflectionCoverageDto>(strict: true);
        using var command = new SqlCommand();

        Action act = () => mapper.MapParameters(
            command,
            new ReflectionCoverageDto(),
            CreateSchema(Param("@RequiredMissing", SqlDbType.Int, nullable: false)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RequiredMissing*");
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldCoverDefaultMissingInputAndOutputBranches()
    {
        var mapper = new ReflectionParameterMapper<ReflectionOutputCoverageDto>(strict: true);
        var dto = new ReflectionOutputCoverageDto();
        using var inputCommand = new SqlCommand();

        mapper.MapParameters(inputCommand, dto, CreateSchema(
            Param("@MissingDefaulted", SqlDbType.Int, nullable: false, hasDefault: true),
            Param("@MissingNullable", SqlDbType.NVarChar, nullable: true),
            Param("@MissingInputOutput", SqlDbType.NVarChar, direction: ParameterDirection.InputOutput)));

        inputCommand.Parameters
            .Cast<SqlParameter>()
            .Should()
            .ContainSingle(p => p.ParameterName == "@MissingNullable");
        inputCommand.Parameters["@MissingNullable"].Value.Should().Be(DBNull.Value);

        using var outputCommand = new SqlCommand();
        outputCommand.Parameters.Add(new SqlParameter("@WritableValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.InputOutput,
            Value = 5
        });
        outputCommand.Parameters.Add(new SqlParameter("@ReadOnlyValue", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 7
        });
        outputCommand.Parameters.Add(new SqlParameter("@MissingOutput", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 8
        });
        outputCommand.Parameters.Add(new SqlParameter("@IgnoredInput", SqlDbType.Int)
        {
            Direction = ParameterDirection.Input,
            Value = 9
        });

        mapper.MapOutputParameters(outputCommand, dto);

        dto.WritableValue.Should().Be(5);
        dto.ReadOnlyValue.Should().Be(0);
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldMapDynamicTypeWithMetadataTokenFallback()
    {
        Type dynamicType = CreateDynamicParameterType();
        object dto = Activator.CreateInstance(dynamicType)!;
        dynamicType.GetProperty("Value")!.SetValue(dto, 17);

        Type mapperType = typeof(ReflectionParameterMapper<>).MakeGenericType(dynamicType);
        object mapper = Activator.CreateInstance(mapperType, [true])!;
        using var command = new SqlCommand();

        mapperType.GetMethod(nameof(ReflectionParameterMapper<object>.MapParameters))!
            .Invoke(mapper, [command, dto, null]);

        command.Parameters["@Value"].Value.Should().Be(17);
    }

    [Fact]
    public void ReflectionParameterMapper_ShouldUseNameOrderingWhenMetadataTokensAreUnavailable()
    {
        using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
        var mapper = new ReflectionParameterMapper<ReflectionMetadataTokenFallbackDto>(strict: true);
        var dto = new ReflectionMetadataTokenFallbackDto
        {
            Beta = 2,
            Alpha = 1
        };
        using var command = new SqlCommand();

        mapper.MapParameters(command, dto, schema: null);

        command.Parameters.Cast<SqlParameter>()
            .Select(static parameter => parameter.ParameterName)
            .Should()
            .Equal("@Alpha", "@Beta");
    }

    [Fact]
    public void MapperFactory_ShouldUseReflectionMapperWhenDynamicCodeIsDisabled()
    {
        using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var factory = new MapperFactory(services, new LibDbOptions());

        ISqlMapper<RuntimeFeatureFallbackDto> mapper = factory.GetMapper<RuntimeFeatureFallbackDto>();

        mapper.Should().BeOfType<ReflectionParameterMapper<RuntimeFeatureFallbackDto>>();
    }

    [Fact]
    public void MapperFactory_ShouldKeepRuntimeFeatureCacheEntriesIsolated()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var factory = new MapperFactory(services, new LibDbOptions());
        bool runtimeDynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported;

        using (RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false))
        {
            ISqlMapper<RuntimeFeatureCacheIsolationDto> fallbackMapper =
                factory.GetMapper<RuntimeFeatureCacheIsolationDto>();

            fallbackMapper.Should().BeOfType<ReflectionParameterMapper<RuntimeFeatureCacheIsolationDto>>();
        }

        RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().Be(runtimeDynamicCodeSupported);

        ISqlMapper<RuntimeFeatureCacheIsolationDto> defaultMapper =
            factory.GetMapper<RuntimeFeatureCacheIsolationDto>();

        if (!runtimeDynamicCodeSupported)
        {
            defaultMapper.Should().BeOfType<ReflectionParameterMapper<RuntimeFeatureCacheIsolationDto>>();
            return;
        }

        defaultMapper.Should().BeOfType<ExpressionTreeMapper<RuntimeFeatureCacheIsolationDto>>();
    }

    [Fact]
    public void RuntimeFeatureSwitch_ShouldRestoreNestedOverridesAndIgnoreDoubleDispose()
    {
        bool runtimeDynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported;
        bool original = RuntimeFeatureSwitch.IsDynamicCodeSupported;
        IDisposable outer = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
        RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().BeFalse();

        using (RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(true))
        {
            RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().Be(runtimeDynamicCodeSupported);
        }

        RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().BeFalse();

        outer.Dispose();
        outer.Dispose();

        RuntimeFeatureSwitch.IsDynamicCodeSupported.Should().Be(original);
    }

    [Fact]
    public void GeneratedResultMapper_ShouldUseReflectionParameterMapperWhenDynamicCodeIsDisabled()
    {
        using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);

        var mapper = new GeneratedResultMapper<GeneratedDbDataReaderRow>(new LibDbOptions());

        GetPrivateField(mapper, "_parameterMapper")
            .Should()
            .BeOfType<ReflectionParameterMapper<GeneratedDbDataReaderRow>>();
    }

    [Fact]
    public void GeneratedResultMapper_ShouldUseDbDataReaderMapAndDelegateParameterMapping()
    {
        var mapper = new GeneratedResultMapper<GeneratedDbDataReaderRow>(new LibDbOptions());
        Mock<DbDataReader> reader = new();
        reader.Setup(r => r.GetInt32(0)).Returns(21);

        GeneratedDbDataReaderRow row = mapper.MapResult(reader.Object);
        using var command = new SqlCommand();
        mapper.MapParameters(command, row, schema: null);
        mapper.MapOutputParameters(command, row);

        row.Id.Should().Be(21);
        command.Parameters.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GeneratedResultMapper_ShouldRejectDbDataReaderWhenOnlySqlDataReaderMapExists()
    {
        var mapper = new GeneratedResultMapper<SqlOnlyGeneratedRow>(new LibDbOptions());

        Action act = () => mapper.MapResult(Mock.Of<DbDataReader>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*only exposes Map(SqlDataReader)*");
    }

    [Fact]
    public async Task GeneratedResultMapper_ShouldUseSqlDataReaderMapWhenSqlDataReaderIsAvailable()
    {
        string? connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Verification");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var mapper = new GeneratedResultMapper<SqlOnlyGeneratedRow>(new LibDbOptions());

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand("SELECT CAST(34 AS int)", connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            TestContext.Current.CancellationToken);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        SqlOnlyGeneratedRow row = mapper.MapResult(reader);

        row.Id.Should().Be(34);
    }

    [Fact]
    public void GeneratedResultMapper_ShouldThrowWhenStaticMapIsMissing()
    {
        Action act = () => _ = new GeneratedResultMapper<NoStaticMapRow>(new LibDbOptions());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Map(DbDataReader)*Map(SqlDataReader)*");
    }

    [Fact]
    public void ScalarSqlMapper_ShouldMapPrimitiveNullableStreamAndNoOpParameters()
    {
        var intMapper = new ScalarSqlMapper<int>();
        var nullableMapper = new ScalarSqlMapper<int?>();
        var streamMapper = new ScalarSqlMapper<Stream>();
        using var command = new SqlCommand();

        intMapper.MapParameters(command, 1, schema: null);
        intMapper.MapOutputParameters(command, 1);

        intMapper.MapResult(ValueReader(123)).Should().Be(123);
        intMapper.MapResult(ValueReader((byte)7)).Should().Be(7);
        nullableMapper.MapResult(ValueReader(DBNull.Value)).Should().BeNull();
        using Stream stream = streamMapper.MapResult(ValueReader(new byte[] { 1, 2, 3 }));
        stream.Length.Should().Be(3);
    }

    private static object GetPrivateField(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return field.GetValue(instance)!;
    }

    private static SpSchema CreateSchema(params SpParameterMetadata[] parameters)
        => new()
        {
            Name = "dbo.usp_Coverage",
            VersionToken = 1,
            LastCheckedAt = DateTime.UtcNow,
            Parameters = parameters
        };

    private static SpParameterMetadata Param(
        string name,
        SqlDbType dbType,
        ParameterDirection direction = ParameterDirection.Input,
        bool nullable = true,
        bool hasDefault = false)
        => new(
            name,
            UdtTypeName: null,
            Size: 0,
            dbType,
            direction,
            Precision: 0,
            Scale: 0,
            IsNullable: nullable,
            HasDefaultValue: hasDefault);

    private sealed class ReflectionCoverageDto
    {
        public int Id { get; set; }

        [DbParameter(DbType = SqlDbType.NVarChar, Size = 32)]
        public string Name { get; set; } = "";

        public int? OutValue { get; set; }

        public string? NullValue { get; set; }
    }

    private sealed class ReflectionOutputCoverageDto
    {
        public int? WritableValue { get; set; }

        public int ReadOnlyValue => 0;
    }

    private sealed class ReflectionMetadataTokenFallbackDto
    {
        public int Beta { get; set; }

        public int Alpha { get; set; }
    }

    private sealed class RuntimeFeatureFallbackDto
    {
        public int Id { get; set; }
    }

    private sealed class RuntimeFeatureCacheIsolationDto
    {
        public int Id { get; set; }
    }

    public sealed class GeneratedDbDataReaderRow : IMapableResult<GeneratedDbDataReaderRow>
    {
        public int Id { get; set; }

        public static GeneratedDbDataReaderRow Map(DbDataReader reader)
            => new() { Id = reader.GetInt32(0) };

        public static GeneratedDbDataReaderRow Map(SqlDataReader reader)
            => throw new NotSupportedException();
    }

    public sealed class SqlOnlyGeneratedRow : IMapableResult<SqlOnlyGeneratedRow>
    {
        public int Id { get; set; }

        public static SqlOnlyGeneratedRow Map(SqlDataReader reader)
            => new() { Id = reader.GetInt32(0) };
    }

    public sealed class NoStaticMapRow
    {
    }

    private static DbDataReader ValueReader(object value)
    {
        Mock<DbDataReader> reader = new();
        reader.Setup(r => r.GetValue(0)).Returns(value);
        return reader.Object;
    }

    private static Type CreateDynamicParameterType()
    {
        var assemblyName = new AssemblyName("LibDbMapperCoverageDynamic" + Guid.NewGuid().ToString("N"));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule("Main");
        TypeBuilder typeBuilder = module.DefineType(
            "RuntimeParameter",
            TypeAttributes.Public | TypeAttributes.Class);
        FieldBuilder valueField = typeBuilder.DefineField(
            "_value",
            typeof(int),
            FieldAttributes.Private);
        PropertyBuilder valueProperty = typeBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            typeof(int),
            Type.EmptyTypes);
        const MethodAttributes AccessorAttributes =
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        MethodBuilder getter = typeBuilder.DefineMethod(
            "get_Value",
            AccessorAttributes,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, valueField);
        getterIl.Emit(OpCodes.Ret);

        MethodBuilder setter = typeBuilder.DefineMethod(
            "set_Value",
            AccessorAttributes,
            null,
            [typeof(int)]);
        ILGenerator setterIl = setter.GetILGenerator();
        setterIl.Emit(OpCodes.Ldarg_0);
        setterIl.Emit(OpCodes.Ldarg_1);
        setterIl.Emit(OpCodes.Stfld, valueField);
        setterIl.Emit(OpCodes.Ret);

        valueProperty.SetGetMethod(getter);
        valueProperty.SetSetMethod(setter);

        return typeBuilder.CreateTypeInfo()!.AsType();
    }
}
