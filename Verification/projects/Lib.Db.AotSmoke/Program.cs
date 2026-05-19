using System.Data;
using System.Globalization;
using Lib.Db;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Tvp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

const string ConnectionEnvironmentVariable = "LIBDB_AOT_SMOKE_CONNECTION";

string? connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"{ConnectionEnvironmentVariable} is not set.");
    return 2;
}

await EnsureSchemaAsync(connectionString, CancellationToken.None);

ServiceCollection services = new();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddLibDb(options =>
{
    options.ConnectionStrings["Verification"] = connectionString;
    options.ConnectionStringNames = ["Verification"];
    options.EnableGeneratedTvpBinder = false;
    options.Tvp
        .Map<AotSmokeRow>("dbo.libdb_aot_OrderItem")
        .Column("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
        .Column("Qty", SqlDbType.Int, static row => row.Qty);
});

await using ServiceProvider provider = services.BuildServiceProvider();
IDbSession session = provider.GetRequiredService<IDbSession>();

AotSmokeRow[] rows =
[
    new(1, "AOT-001", 2),
    new(2, "AOT-002", 3)
];

TvpShape<AotSmokeRow> directShape = TvpShape.For<AotSmokeRow>()
    .Column("Id", SqlDbType.Int, static row => row.Id)
    .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
    .Column("Qty", SqlDbType.Int, static row => row.Qty)
    .Build();

await ExecuteDirectStaticShapeAsync(connectionString, directShape, rows, CancellationToken.None);

DbResult<int> result = await session
    .Use("Verification")
    .Procedure("dbo.libdb_aot_InsertOrderItems")
    .With(new Dictionary<string, object?>
    {
        ["OrderId"] = 230,
        ["RequestedBy"] = "aot-smoke",
        ["Rows"] = LibDb.Tvp("dbo.libdb_aot_OrderItem", rows, directShape)
    })
    .ExecuteScalarAsync<int>();

if (!result.IsSuccess || result.Value != 5)
{
    Console.Error.WriteLine(result.Error?.Message ?? string.Create(CultureInfo.InvariantCulture, $"Unexpected scalar: {result.Value}"));
    return 1;
}

Console.WriteLine("AOT smoke completed.");
return 0;

static async Task EnsureSchemaAsync(string connectionString, CancellationToken ct)
{
    await using SqlConnection connection = new(connectionString);
    await connection.OpenAsync(ct);

    const string sql = """
        IF TYPE_ID(N'dbo.libdb_aot_OrderItem') IS NULL
            EXEC(N'CREATE TYPE dbo.libdb_aot_OrderItem AS TABLE
            (
                Id int NOT NULL,
                Sku nvarchar(64) NOT NULL,
                Qty int NOT NULL
            )');

        EXEC(N'
        CREATE OR ALTER PROCEDURE dbo.libdb_aot_InsertOrderItems
            @OrderId int,
            @RequestedBy nvarchar(64),
            @Rows dbo.libdb_aot_OrderItem READONLY
        AS
        BEGIN
            SET NOCOUNT ON;
            SELECT SUM(Qty) FROM @Rows;
        END');
        """;

    await using SqlCommand command = new(sql, connection);
    await command.ExecuteNonQueryAsync(ct);
}

static async Task ExecuteDirectStaticShapeAsync(
    string connectionString,
    TvpShape<AotSmokeRow> shape,
    AotSmokeRow[] rows,
    CancellationToken ct)
{
    await using SqlConnection connection = new(connectionString);
    await connection.OpenAsync(ct);

    await using SqlCommand command = new("dbo.libdb_aot_InsertOrderItems", connection)
    {
        CommandType = CommandType.StoredProcedure
    };

    DbBinder.BindRawParameter(command, "OrderId", 230);
    DbBinder.BindRawParameter(command, "RequestedBy", "aot-direct");
    DbBinder.BindRawParameter(command, "Rows", LibDb.Tvp("dbo.libdb_aot_OrderItem", rows, shape));

    object? scalar = await command.ExecuteScalarAsync(ct);
    int sum = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    if (sum != 5)
        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Unexpected direct scalar: {sum}"));
}

internal sealed record AotSmokeRow(int Id, string Sku, int Qty);
