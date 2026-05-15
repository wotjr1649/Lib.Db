// ============================================================================
// 파일: Unit/SqlInterpolatedStringHandlerTests.cs
// 설명: SqlInterpolatedStringHandler 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Fluent;
using System.Data;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class SqlInterpolatedStringHandlerTests
{
    [Fact]
    public void Should_Generate_Correct_Sql_And_Parameters()
    {
        // Arrange
        int userId = 123;
        string userName = "TestUser";
        bool isActive = true;

        SqlInterpolatedStringHandler handler = new(50, 3, out bool isValid);

        // Act
        handler.AppendLiteral("SELECT * FROM Users WHERE Id = ");
        handler.AppendFormatted(userId);
        handler.AppendLiteral(" AND Name = ");
        handler.AppendFormatted(userName);
        handler.AppendLiteral(" AND IsActive = ");
        handler.AppendFormatted(isActive);

        (string sql, Dictionary<string, object?> parameters) = handler.GetResult();
        handler.Dispose();

        // Assert
        sql.Should().Be("SELECT * FROM Users WHERE Id = @p0 AND Name = @p1 AND IsActive = @p2");
        parameters.Should().HaveCount(3);
        parameters["@p0"].Should().Be(userId);
        parameters["@p1"].Should().Be(userName);
        parameters["@p2"].Should().Be(isActive);
    }

    [Fact]
    public void Should_Handle_Mixed_Literals_And_Parameters()
    {
        // Arrange
        string table = "Users";
        int limit = 10;

        SqlInterpolatedStringHandler handler = new(20, 2, out bool isValid);

        // Act
        handler.AppendLiteral("SELECT TOP ");
        handler.AppendFormatted(limit);
        handler.AppendLiteral(" * FROM ");
        handler.AppendLiteral(table);

        (string sql, Dictionary<string, object?> parameters) = handler.GetResult();
        handler.Dispose();

        // Assert
        sql.Should().Be("SELECT TOP @p0 * FROM Users");
        parameters.Should().HaveCount(1);
        parameters["@p0"].Should().Be(limit);
    }

    [Fact]
    public async Task SqlRaw_Should_Execute_As_Text_Command()
    {
        // Arrange
        CapturingDbExecutor executor = new();
        DbRequestBuilder builder = new(executor, "Default");

        // Act
        await builder.SqlRaw("SELECT 1").ExecuteAsync();

        // Assert
        executor.LastCommandText.Should().Be("SELECT 1");
        executor.LastCommandType.Should().Be(CommandType.Text);
    }

    [Fact]
    public async Task FormattableSql_Should_Parameterize_Interpolated_Values()
    {
        // Arrange
        CapturingDbExecutor executor = new();
        DbRequestBuilder builder = new(executor, "Default");
        int userId = 42;
        string userName = "Alice";

        // Act
        await builder.Sql((FormattableString)$"SELECT * FROM Users WHERE Id = {userId} AND Name = {userName}")
            .ExecuteAsync();

        // Assert
        executor.LastCommandText.Should().Be("SELECT * FROM Users WHERE Id = @p0 AND Name = @p1");
        executor.LastCommandType.Should().Be(CommandType.Text);
        executor.LastParameters.Should().BeAssignableTo<IDictionary<string, object?>>();

        IDictionary<string, object?> parameters = (IDictionary<string, object?>)executor.LastParameters!;
        parameters.Should().ContainKey("@p0").WhoseValue.Should().Be(userId);
        parameters.Should().ContainKey("@p1").WhoseValue.Should().Be(userName);
    }

    private sealed class CapturingDbExecutor : IDbExecutor
    {
        public string? LastCommandText { get; private set; }
        public object? LastParameters { get; private set; }
        public string? LastInstanceHash { get; private set; }
        public CommandType? LastCommandType { get; private set; }
        public DbExecutionOptions? LastOptions { get; private set; }

        public IAsyncEnumerable<TResult> QueryAsync<TParams, TResult>(
            string commandText,
            TParams parameters,
            string instanceHash,
            CommandType commandType,
            DbExecutionOptions options,
            CancellationToken ct)
        {
            Capture(commandText, parameters, instanceHash, commandType, options);
            return EmptyAsync<TResult>();
        }

        public Task<TResult?> QuerySingleAsync<TParams, TResult>(
            string commandText,
            TParams parameters,
            string instanceHash,
            CommandType commandType,
            DbExecutionOptions options,
            CancellationToken ct)
        {
            Capture(commandText, parameters, instanceHash, commandType, options);
            return Task.FromResult<TResult?>(default);
        }

        public Task<TScalar?> ExecuteScalarAsync<TParams, TScalar>(
            string commandText,
            TParams parameters,
            string instanceHash,
            CommandType commandType,
            DbExecutionOptions options,
            CancellationToken ct)
        {
            Capture(commandText, parameters, instanceHash, commandType, options);
            return Task.FromResult<TScalar?>(default);
        }

        public Task<int> ExecuteNonQueryAsync<TParams>(
            string commandText,
            TParams parameters,
            string instanceHash,
            CommandType commandType,
            DbExecutionOptions options,
            CancellationToken ct)
        {
            Capture(commandText, parameters, instanceHash, commandType, options);
            return Task.FromResult(0);
        }

        public Task<IMultipleResultReader> QueryMultipleAsync<TParams>(
            string commandText,
            TParams parameters,
            string instanceHash,
            CommandType commandType,
            DbExecutionOptions options,
            CancellationToken ct)
        {
            Capture(commandText, parameters, instanceHash, commandType, options);
            throw new NotSupportedException();
        }

        private void Capture<TParams>(
            string commandText,
            TParams parameters,
            string instanceHash,
            CommandType commandType,
            DbExecutionOptions options)
        {
            LastCommandText = commandText;
            LastParameters = parameters;
            LastInstanceHash = instanceHash;
            LastCommandType = commandType;
            LastOptions = options;
        }

        private static async IAsyncEnumerable<TResult> EmptyAsync<TResult>()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
