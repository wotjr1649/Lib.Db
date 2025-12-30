using Lib.Db.Contracts.Execution;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Lib.Db.Verification.Tests.Infrastructure;

public class TestSqlResumableStateStore : IResumableStateStore
{
    private readonly string _connectionString;

    public TestSqlResumableStateStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<TCursor?> GetLastCursorAsync<TCursor>(string instanceHash, string queryKey, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CursorValue 
            FROM [core].[CursorState] 
            WHERE InstanceHash = @InstanceHash AND QueryKey = @QueryKey";
        
        cmd.Parameters.AddWithValue("@InstanceHash", instanceHash);
        cmd.Parameters.AddWithValue("@QueryKey", queryKey);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value)
            return default;

        var json = (string)result;
        return JsonSerializer.Deserialize<TCursor>(json);
    }

    public async Task SaveCursorAsync<TCursor>(string instanceHash, string queryKey, TCursor cursor, CancellationToken ct = default)
    {
        Console.WriteLine($"STORE: SAVING Cursor {cursor} Hash {instanceHash} Key {queryKey}");
        var json = JsonSerializer.Serialize(cursor);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            MERGE [core].[CursorState] AS target
            USING (SELECT @InstanceHash AS InstanceHash, @QueryKey AS QueryKey) AS source
            ON (target.InstanceHash = source.InstanceHash AND target.QueryKey = source.QueryKey)
            WHEN MATCHED THEN
                UPDATE SET CursorValue = @CursorValue, UpdatedAt = SYSDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (InstanceHash, QueryKey, CursorValue, UpdatedAt)
                VALUES (@InstanceHash, @QueryKey, @CursorValue, SYSDATETIME());";

        cmd.Parameters.AddWithValue("@InstanceHash", instanceHash);
        cmd.Parameters.AddWithValue("@QueryKey", queryKey);
        cmd.Parameters.AddWithValue("@CursorValue", json);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
