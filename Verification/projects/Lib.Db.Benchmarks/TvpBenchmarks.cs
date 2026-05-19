// ============================================================================
// 파일: Benchmarks/Lib.Db.Benchmarks/TvpBenchmarks.cs
// 설명: 런타임 TVP와 generated-accessor baseline 실제 SQL Server 비교
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Lib.Db.Benchmarks.Baselines;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Benchmarks.Database;
using Lib.Db.Execution.Binding;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Benchmarks;

[MemoryDiagnoser]
public class TvpBenchmarks
{
    private const string ProcedureName = "dbo.libdb_bench_InsertOrderItems";
    private const string TvpTypeName = "dbo.libdb_bench_OrderItem";
    private string _connectionString = string.Empty;
    private BenchmarkOrderItemRow[] _rows = [];
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
            .Select(static i => new BenchmarkOrderItemRow(i, $"SKU-{i:D6}", (i % 9) + 1, (decimal)(i % 100) + 0.99m))
            .ToArray();

        _orderSeed = RowCount * 10_000;

        var options = new LibDbOptions();
        options.Tvp
            .Map<BenchmarkOrderItemRow>(TvpTypeName)
            .Column("Id", SqlDbType.Int, static x => x.Id)
            .Column("Sku", SqlDbType.NVarChar, static x => x.Sku, size: 64)
            .Column("Qty", SqlDbType.Int, static x => x.Qty)
            .Column("Price", SqlDbType.Decimal, static x => x.Price, precision: 18, scale: 2);
        DbBinder.ConfigureTvp(options);
    }

    [Benchmark(Baseline = true)]
    public Task<long> GeneratedAccessorBaseline()
        => ExecuteAsync(GeneratedAccessorBaselineReader.Create(_rows));

    [Benchmark]
    public Task<long> RuntimeObjectStreaming()
        => ExecuteRuntimeAsync(_rows);

    [Benchmark]
    public Task<long> RuntimeRegisteredFastPath()
        => ExecuteRuntimeRegisteredAsync(_rows);

    private async Task<long> ExecuteRuntimeAsync(IReadOnlyList<BenchmarkOrderItemRow> rows)
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

    private async Task<long> ExecuteRuntimeRegisteredAsync(IReadOnlyList<BenchmarkOrderItemRow> rows)
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
}

public sealed record BenchmarkOrderItemRow(
    int Id,
    [property: DbParameter(Size = 64)] string Sku,
    int Qty,
    [property: DbParameter(Precision = 18, Scale = 2)] decimal Price);
