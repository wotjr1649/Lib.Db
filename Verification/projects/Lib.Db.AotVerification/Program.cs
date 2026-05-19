using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Lib.Db;
using Lib.Db.Caching;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Tvp;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;

try
{
    RunStep("NativeAotRuntimeMode", VerifyNativeAotRuntimeMode);
    RunStep("DependencyInjectionHybridCache", VerifyDependencyInjectionHybridCache);
    RunStep("ExplicitStaticTvpShape", VerifyExplicitStaticTvpShape);
    RunStep("RegisteredStaticTvpShape", VerifyRegisteredStaticTvpShape);
    RunStep("GeneratedMapperAndReflectionParameterMapper", VerifyGeneratedMapperAndReflectionParameterMapper);

    Console.WriteLine("Lib.Db AOT verification passed.");
    return 0;
}
catch (Exception ex)
{
    WriteException(ex);
    return 1;
}
finally
{
    DbBinder.ConfigureTvp(new LibDbOptions());
    DbBinder.ClearTvpCaches();
}

static void RunStep(string name, Action action)
{
    try
    {
        action();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"AOT verification step '{name}' failed.", ex);
    }
}

static void WriteException(Exception exception)
{
    for (Exception? current = exception; current is not null; current = current.InnerException)
    {
        Console.Error.WriteLine($"{current.GetType().Name}: {current.Message}");
    }
}

static void VerifyNativeAotRuntimeMode()
{
    if (RuntimeFeature.IsDynamicCodeSupported)
        throw new InvalidOperationException("Expected Native AOT runtime mode with dynamic code disabled.");
}

static void VerifyDependencyInjectionHybridCache()
{
    var services = new ServiceCollection();
    services.AddLibDb(static _ => { });

    using ServiceProvider provider = services.BuildServiceProvider();
    HybridCache cache = provider.GetRequiredService<HybridCache>();

    if (cache is not LibDbAotHybridCache)
        throw new InvalidOperationException($"Expected {nameof(LibDbAotHybridCache)} from DI, actual '{cache.GetType().FullName}'.");

    cache.SetAsync("aot-schema", "before", tags: ["schema:dbo"]).GetAwaiter().GetResult();
    cache.RemoveByTagAsync("schema:dbo").GetAwaiter().GetResult();
    cache.SetAsync("aot-schema", "after", tags: ["schema:dbo"]).GetAwaiter().GetResult();

    int factoryCalls = 0;
    string value = cache.GetOrCreateAsync(
        "aot-schema",
        state: 0,
        (_, _) =>
        {
            factoryCalls++;
            return new ValueTask<string>("miss");
        },
        tags: ["schema:dbo"]).GetAwaiter().GetResult();

    AssertEqual("after", value, "HybridCache tag invalidation");
    AssertEqual(0, factoryCalls, "HybridCache factory calls after post-invalidation set");
}

static void VerifyExplicitStaticTvpShape()
{
    LibDbTvpValue value = LibDb.Tvp("dbo.T_OrderItem", CreateRows(), CreateShape());

    using var command = new SqlCommand();
    DbBinder.BindRawParameter(command, "Rows", value);

    VerifyStructuredRows(command, "@Rows");
}

static void VerifyRegisteredStaticTvpShape()
{
    LibDbOptions options = new();
    options.Tvp
        .Map<AotOrderItem>("dbo.T_OrderItem")
        .Column("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
        .Column("Qty", SqlDbType.Int, static row => row.Qty);

    DbBinder.ConfigureTvp(options);
    DbBinder.ClearTvpCaches();

    using var command = new SqlCommand();
    DbBinder.BindRawParameter(command, "Rows", CreateRows());

    VerifyStructuredRows(command, "@Rows");
}

static void VerifyGeneratedMapperAndReflectionParameterMapper()
{
    DataTable table = new();
    table.Columns.Add("Id", typeof(int));
    table.Columns.Add("Name", typeof(string));
    table.Rows.Add(42, "aot-row");

    using DbDataReader reader = table.CreateDataReader();
    if (!reader.Read())
        throw new InvalidOperationException("Expected one generated-mapper row.");

    var generatedMapper = new GeneratedResultMapper<AotGeneratedRow>(new LibDbOptions());
    AotGeneratedRow row = generatedMapper.MapResult(reader);

    AssertEqual(42, row.Id, "generated row Id");
    AssertEqual("aot-row", row.Name, "generated row Name");

    var parameterMapper = new ReflectionParameterMapper<AotParameterDto>(strict: true);
    var dto = new AotParameterDto
    {
        Id = 7,
        Name = "input"
    };

    using var command = new SqlCommand();
    parameterMapper.MapParameters(command, dto, CreateSchema(
        Param("@Id", SqlDbType.Int, nullable: false),
        Param("@Name", SqlDbType.NVarChar, nullable: false),
        Param("@OutValue", SqlDbType.Int, direction: ParameterDirection.Output)));

    SqlParameter idParameter = GetParameter(command, "@Id");
    AssertEqual(SqlDbType.Int, idParameter.SqlDbType, "@Id SqlDbType");
    AssertEqual(ParameterDirection.Input, idParameter.Direction, "@Id Direction");
    AssertParameterValue(idParameter, 7, "@Id Value");

    SqlParameter nameParameter = GetParameter(command, "@Name");
    AssertEqual(SqlDbType.NVarChar, nameParameter.SqlDbType, "@Name SqlDbType");
    AssertEqual(ParameterDirection.Input, nameParameter.Direction, "@Name Direction");
    AssertParameterValue(nameParameter, "input", "@Name Value");

    SqlParameter outValueParameter = GetParameter(command, "@OutValue");
    AssertEqual(SqlDbType.Int, outValueParameter.SqlDbType, "@OutValue SqlDbType");
    AssertEqual(ParameterDirection.Output, outValueParameter.Direction, "@OutValue Direction");

    outValueParameter.Value = 99;
    parameterMapper.MapOutputParameters(command, dto);

    AssertParameterValue(outValueParameter, 99, "@OutValue Value");
    AssertEqual(99, dto.OutValue, "output parameter");
}

static AotOrderItem[] CreateRows()
    => [new(1, "A100", 2)];

static TvpShape<AotOrderItem> CreateShape()
    => TvpShape.For<AotOrderItem>()
        .Column("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
        .Column("Qty", SqlDbType.Int, static row => row.Qty)
        .Build();

static void VerifyStructuredRows(SqlCommand command, string parameterName)
{
    SqlParameter parameter = command.Parameters[parameterName];

    if (parameter.SqlDbType != SqlDbType.Structured)
        throw new InvalidOperationException($"Expected structured parameter for {parameterName}.");

    if (!string.Equals(parameter.TypeName, "dbo.T_OrderItem", StringComparison.Ordinal))
        throw new InvalidOperationException($"Unexpected TVP type name: {parameter.TypeName}");

    if (parameter.Value is not IEnumerable<SqlDataRecord> records)
        throw new InvalidOperationException("Expected static TVP shape to bind as SqlDataRecord sequence.");

    using IEnumerator<SqlDataRecord> enumerator = records.GetEnumerator();
    if (!enumerator.MoveNext())
        throw new InvalidOperationException("Expected one TVP row.");

    SqlDataRecord record = enumerator.Current;
    AssertEqual("Id", record.GetName(0), "column 0 name");
    AssertEqual("Sku", record.GetName(1), "column 1 name");
    AssertEqual("Qty", record.GetName(2), "column 2 name");
    AssertEqual(1, record.GetInt32(0), "Id");
    AssertEqual("A100", record.GetString(1), "Sku");
    AssertEqual(2, record.GetInt32(2), "Qty");

    if (enumerator.MoveNext())
        throw new InvalidOperationException("Expected exactly one TVP row.");
}

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
}

static SqlParameter GetParameter(SqlCommand command, string name)
{
    if (!command.Parameters.Contains(name))
        throw new InvalidOperationException($"Expected {name} parameter.");

    return command.Parameters[name];
}

static void AssertParameterValue<T>(SqlParameter parameter, T expected, string name)
{
    if (parameter.Value is not T actual)
        throw new InvalidOperationException($"{name}: expected '{typeof(T).Name}', actual '{parameter.Value?.GetType().Name ?? "null"}'.");

    AssertEqual(expected, actual, name);
}

static SpSchema CreateSchema(params SpParameterMetadata[] parameters)
    => new()
    {
        Name = "dbo.usp_AotVerification",
        VersionToken = 1,
        LastCheckedAt = DateTime.UtcNow,
        Parameters = parameters
    };

static SpParameterMetadata Param(
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

internal readonly record struct AotOrderItem(int Id, string Sku, int Qty);

internal sealed class AotGeneratedRow : IMapableResult<AotGeneratedRow>
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    public static AotGeneratedRow Map(DbDataReader reader)
        => new()
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1)
        };

    public static AotGeneratedRow Map(SqlDataReader reader)
        => throw new NotSupportedException("The AOT verification uses the DbDataReader overload.");
}

internal sealed class AotParameterDto
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int OutValue { get; set; }
}
