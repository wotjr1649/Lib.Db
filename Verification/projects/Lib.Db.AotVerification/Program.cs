using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Lib.Db;
using Lib.Db.Caching;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Bulk;
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
    RunStep("AotSafeBulkShape", VerifyAotSafeBulkShape);
    RunStep("AotSafeBulkPublicApiReachability", VerifyAotSafeBulkPublicApiReachability);

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
        Console.WriteLine($"AOT verification step '{name}' completed.");
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

static void VerifyAotSafeBulkShape()
{
    BulkShape<AotBulkRow> shape = CreateBulkShape();

    using BulkShapeDataReader<AotBulkRow> reader = new(
        [new AotBulkRow(1, "AOT-SKU", 3, AotBulkStatus.Active)],
        shape);

    if (!reader.Read())
        throw new InvalidOperationException("AOT bulk reader did not read the smoke row.");

    if (!Equals(reader.GetValue(1), "AOT-SKU"))
        throw new InvalidOperationException("AOT bulk reader returned an unexpected value.");

    if (!Equals(reader.GetValue(3), 1))
        throw new InvalidOperationException("AOT bulk reader did not normalize enum values through shape metadata.");
}

static void VerifyAotSafeBulkPublicApiReachability()
{
    BulkShape<AotBulkRow> shape = CreateBulkShape();
    BulkWriteOptions writeOptions = new();
    BulkMergeOptions mergeOptions = new();
    mergeOptions.Validate();

    Func<IDbSession, CancellationToken, Task<DbResult<long>>> insert =
        (session, token) => session.BulkInsertAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, writeOptions, token);
    Func<IDbSession, CancellationToken, Task<DbResult<long>>> update =
        (session, token) => session.BulkUpdateAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, writeOptions, token);
    Func<IDbSession, CancellationToken, Task<DbResult<long>>> delete =
        (session, token) => session.BulkDeleteAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, writeOptions, token);
    Func<IDbSession, CancellationToken, Task<DbResult<BulkUpsertResult>>> upsert =
        (session, token) => session.BulkUpsertAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, writeOptions, token);
    Func<IDbSession, CancellationToken, Task<DbResult<BulkMergeResult>>> merge =
        (session, token) => session.BulkMergeAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, mergeOptions, token);

    GC.KeepAlive(insert);
    GC.KeepAlive(update);
    GC.KeepAlive(delete);
    GC.KeepAlive(upsert);
    GC.KeepAlive(merge);

    VerifyAotSafeBulkConcreteExecutorReachability(shape, writeOptions, mergeOptions);
}

static void VerifyAotSafeBulkConcreteExecutorReachability(
    BulkShape<AotBulkRow> shape,
    BulkWriteOptions writeOptions,
    BulkMergeOptions mergeOptions)
{
    AotNoDbConnectionFactory connectionFactory = new();
    BulkWriteExecutor executor = new(connectionFactory);

    DbResult<long> insert = executor.BulkInsertAsync(
        "Default",
        "dbo.AotBulk",
        Array.Empty<AotBulkRow>(),
        shape,
        writeOptions,
        CancellationToken.None).GetAwaiter().GetResult();
    AssertSuccessfulCount(insert, 0, "bulk insert no-DB reachability");

    DbResult<long> update = executor.BulkUpdateAsync(
        "Default",
        "dbo.AotBulk",
        Array.Empty<AotBulkRow>(),
        shape,
        writeOptions,
        CancellationToken.None).GetAwaiter().GetResult();
    AssertSuccessfulCount(update, 0, "bulk update no-DB reachability");

    DbResult<long> delete = executor.BulkDeleteAsync(
        "Default",
        "dbo.AotBulk",
        Array.Empty<AotBulkRow>(),
        shape,
        writeOptions,
        CancellationToken.None).GetAwaiter().GetResult();
    AssertSuccessfulCount(delete, 0, "bulk delete no-DB reachability");

    DbResult<BulkUpsertResult> upsert = executor.BulkUpsertAsync(
        "Default",
        "dbo.AotBulk",
        Array.Empty<AotBulkRow>(),
        shape,
        writeOptions,
        CancellationToken.None).GetAwaiter().GetResult();
    AssertSuccessful(upsert, "bulk upsert no-DB reachability");
    AssertEqual(0, upsert.Value.TotalAffected, "bulk upsert no-DB reachability");

    DbResult<BulkMergeResult> merge = executor.BulkMergeAsync(
        "Default",
        "dbo.AotBulk",
        Array.Empty<AotBulkRow>(),
        shape,
        mergeOptions,
        CancellationToken.None).GetAwaiter().GetResult();
    AssertSuccessful(merge, "bulk merge no-DB reachability");
    AssertEqual(0, merge.Value.TotalAffected, "bulk merge no-DB reachability");

    DbResult<BulkMergeResult> invalidMerge = executor.BulkMergeAsync(
        "Default",
        "dbo.AotBulk",
        [new AotBulkRow(1, "AOT-SKU", 3, AotBulkStatus.Active)],
        shape,
        new BulkMergeOptions { Actions = (BulkMergeActions)16 },
        CancellationToken.None).GetAwaiter().GetResult();
    if (invalidMerge.IsSuccess)
        throw new InvalidOperationException("Expected invalid merge actions to fail before opening a connection.");

    AssertEqual(0, connectionFactory.OpenAttempts, "bulk no-DB connection open attempts");
}

static AotOrderItem[] CreateRows()
    => [new(1, "A100", 2)];

static TvpShape<AotOrderItem> CreateShape()
    => TvpShape.For<AotOrderItem>()
        .Column("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
        .Column("Qty", SqlDbType.Int, static row => row.Qty)
        .Build();

static BulkShape<AotBulkRow> CreateBulkShape()
    => BulkShape.For<AotBulkRow>()
        .Key("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
        .Column("Qty", SqlDbType.Int, static row => row.Qty)
        .Column("Status", SqlDbType.Int, static row => row.Status)
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

static void AssertSuccessfulCount(DbResult<long> result, long expected, string name)
{
    AssertSuccessful(result, name);
    AssertEqual(expected, result.Value, name);
}

static void AssertSuccessful<T>(DbResult<T> result, string name)
{
    if (!result.IsSuccess)
        throw new InvalidOperationException($"{name}: expected success.");
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

internal enum AotBulkStatus
{
    Inactive = 0,
    Active = 1
}

internal readonly record struct AotBulkRow(int Id, string Sku, int Qty, AotBulkStatus Status);

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

internal sealed class AotNoDbConnectionFactory : IDbConnectionFactory
{
    private int _openAttempts;

    public int OpenAttempts => _openAttempts;

    public Task<SqlConnection> CreateConnectionAsync(string instanceHash, CancellationToken ct)
    {
        Interlocked.Increment(ref _openAttempts);
        throw new InvalidOperationException("AOT no-DB bulk probe attempted to open a SQL connection.");
    }

    public void RegisterAdHocInstance(string instanceName, string connectionString)
    {
    }

    public void UnregisterAdHocInstance(string instanceName)
    {
    }
}
