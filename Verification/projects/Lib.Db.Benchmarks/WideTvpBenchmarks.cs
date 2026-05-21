// ============================================================================
// File: Benchmarks/Lib.Db.Benchmarks/WideTvpBenchmarks.cs
// Description: 16-column runtime TVP and generated-accessor baseline benchmark.
// Target: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Lib.Db.Benchmarks.Baselines;
using Lib.Db.Benchmarks.Database;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Execution.Binding;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Benchmarks;

[MemoryDiagnoser]
public class WideTvpBenchmarks
{
    private const string ProcedureName = "dbo.libdb_bench_InsertWideOrderItems";
    private const string TvpTypeName = "dbo.libdb_bench_WideOrderItem";
    private static readonly DateTime s_requestedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private string _connectionString = string.Empty;
    private BenchmarkWideOrderItemRow[] _rows = [];
    private int _orderSeed;

    [ParamsSource(nameof(RowCounts))]
    public int RowCount { get; set; }

    public static IEnumerable<int> RowCounts() => BenchmarkRowCounts.Values;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _connectionString = BenchmarkDatabase.GetConnectionString();
        await BenchmarkDatabase.ResetAsync(_connectionString, CancellationToken.None).ConfigureAwait(false);

        _rows = Enumerable.Range(1, RowCount)
            .Select(static i => CreateRow(i))
            .ToArray();

        _orderSeed = RowCount * 20_000;

        var options = new LibDbOptions();
        options.Tvp
            .Map<BenchmarkWideOrderItemRow>(TvpTypeName)
            .Column("Id", SqlDbType.Int, static x => x.Id)
            .Column("Sku", SqlDbType.NVarChar, static x => x.Sku, size: 64)
            .Column("Qty", SqlDbType.Int, static x => x.Qty)
            .Column("Price", SqlDbType.Decimal, static x => x.Price, precision: 18, scale: 2)
            .Column("Discount", SqlDbType.Decimal, static x => x.Discount, precision: 18, scale: 2)
            .Column("Tax", SqlDbType.Decimal, static x => x.Tax, precision: 18, scale: 2)
            .Column("LineTotal", SqlDbType.Decimal, static x => x.LineTotal, precision: 18, scale: 2)
            .Column("IsGift", SqlDbType.Bit, static x => x.IsGift)
            .Column("WarehouseId", SqlDbType.Int, static x => x.WarehouseId)
            .Column("Region", SqlDbType.NVarChar, static x => x.Region, size: 16)
            .Column("BatchId", SqlDbType.UniqueIdentifier, static x => x.BatchId)
            .Column("RequestedAt", SqlDbType.DateTime2, static x => x.RequestedAt, scale: 7)
            .Column("SequenceNumber", SqlDbType.BigInt, static x => x.SequenceNumber)
            .Column("Priority", SqlDbType.SmallInt, static x => x.Priority)
            .Column("Status", SqlDbType.TinyInt, static x => x.Status)
            .Column("Note", SqlDbType.NVarChar, static x => x.Note, size: 128);
        DbBinder.ConfigureTvp(options);
    }

    [Benchmark(Baseline = true)]
    public Task<long> GeneratedAccessorBaseline()
        => ExecuteAsync(GeneratedWideAccessorBaselineReader.Create(_rows));

    [Benchmark]
    public Task<long> RuntimeObjectStreaming()
        => ExecuteRuntimeAsync(_rows);

    [Benchmark]
    public Task<long> RuntimeRegisteredFastPath()
        => ExecuteRuntimeRegisteredAsync(_rows);

    private async Task<long> ExecuteRuntimeAsync(IReadOnlyList<BenchmarkWideOrderItemRow> rows)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using SqlCommand command = CreateCommand(connection);
        DbBinder.BindRawParameter(command, "OrderId", Interlocked.Increment(ref _orderSeed));
        DbBinder.BindRawParameter(command, "RequestedBy", "benchmark");
        DbBinder.BindRawParameter(command, "Rows", LibDb.Tvp(TvpTypeName, rows));

        object? result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    private async Task<long> ExecuteRuntimeRegisteredAsync(IReadOnlyList<BenchmarkWideOrderItemRow> rows)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using SqlCommand command = CreateCommand(connection);
        DbBinder.BindRawParameter(command, "OrderId", Interlocked.Increment(ref _orderSeed));
        DbBinder.BindRawParameter(command, "RequestedBy", "benchmark");
        DbBinder.BindRawParameter(command, "Rows", rows);

        object? result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    private async Task<long> ExecuteAsync(DbDataReader rows)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using SqlCommand command = CreateCommand(connection);
        command.Parameters.Add(new SqlParameter("@OrderId", SqlDbType.Int)
        {
            Value = Interlocked.Increment(ref _orderSeed)
        });
        command.Parameters.Add(new SqlParameter("@RequestedBy", SqlDbType.NVarChar, 64)
        {
            Value = "benchmark"
        });
        command.Parameters.Add(new SqlParameter("@Rows", SqlDbType.Structured)
        {
            TypeName = TvpTypeName,
            Value = rows
        });

        object? result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    private static SqlCommand CreateCommand(SqlConnection connection)
        => new(ProcedureName, connection)
        {
            CommandType = CommandType.StoredProcedure
        };

    private static BenchmarkWideOrderItemRow CreateRow(int i)
    {
        int qty = (i % 9) + 1;
        decimal price = (decimal)(i % 100) + 0.99m;
        decimal discount = (i % 5) * 0.10m;
        decimal tax = Math.Round((price * qty - discount) * 0.0825m, 2, MidpointRounding.AwayFromZero);
        decimal lineTotal = price * qty - discount + tax;

        return new BenchmarkWideOrderItemRow(
            i,
            $"SKU-{i:D6}",
            qty,
            price,
            discount,
            tax,
            lineTotal,
            (i & 1) == 0,
            (i % 32) + 1,
            $"R{i % 8:D2}",
            CreateBatchId(i),
            s_requestedAt.AddSeconds(i),
            10_000_000L + i,
            (short)(i % 5),
            (byte)(i % 4),
            $"note-{i:D6}");
    }

    private static Guid CreateBatchId(int i)
        => new(
            i,
            unchecked((short)(i * 17)),
            unchecked((short)(i * 31)),
            [
                (byte)i,
                (byte)(i >> 8),
                (byte)(i >> 16),
                (byte)(i >> 24),
                (byte)(i * 3),
                (byte)(i * 5),
                (byte)(i * 7),
                (byte)(i * 11)
            ]);
}

public sealed record BenchmarkWideOrderItemRow(
    int Id,
    [property: DbParameter(Size = 64)] string Sku,
    int Qty,
    [property: DbParameter(Precision = 18, Scale = 2)] decimal Price,
    [property: DbParameter(Precision = 18, Scale = 2)] decimal Discount,
    [property: DbParameter(Precision = 18, Scale = 2)] decimal Tax,
    [property: DbParameter(Precision = 18, Scale = 2)] decimal LineTotal,
    bool IsGift,
    int WarehouseId,
    [property: DbParameter(Size = 16)] string Region,
    Guid BatchId,
    [property: DbParameter(Scale = 7)] DateTime RequestedAt,
    long SequenceNumber,
    short Priority,
    byte Status,
    [property: DbParameter(Size = 128)] string Note);
