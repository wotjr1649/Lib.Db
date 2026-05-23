using System.Data;
using Lib.Db.Execution.Bulk;
using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class BulkMutationTests(MultiDbFixture fixture)
{
    private const string DestinationTable = "gap.BulkMutationTarget";
    private const string SanitizedDestinationTable = "[gap].[BulkMutationTarget]";

    private static readonly BulkShape<BulkMutationRow> s_shape = BulkShape.For<BulkMutationRow>()
        .Column("ExternalId", SqlDbType.Int, static row => row.ExternalId, nullable: false)
        .Column("Name", SqlDbType.NVarChar, static row => row.Name, nullable: false, size: 100)
        .Column("Price", SqlDbType.Decimal, static row => row.Price, nullable: false, precision: 18, scale: 2)
        .Build();

    private readonly IDbSession _session = fixture.Session;
    private readonly IProcedureStage _db = fixture.Verification;

    [Fact]
    public async Task BulkInsertAsync_WhenRowsAreValid_ShouldInsertRows()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            BulkMutationRow[] rows =
            [
                new(101, "Bulk mutation alpha", 12.34m),
                new(102, "Bulk mutation beta", 56.78m)
            ];

            DbResult<long> result = await _session.BulkInsertAsync(
                "Verification",
                DestinationTable,
                rows,
                s_shape,
                ct: ct);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(rows.Length);
            (await CountRowsAsync(ct)).Should().Be(rows.Length);
        }
        finally
        {
            await ClearTargetAsync(ct);
        }
    }

    [Fact]
    public async Task BulkInsertAsync_WhenCheckConstraintFailsByDefault_ShouldNotInsertRows()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            BulkMutationRow[] rows =
            [
                new(201, "Constraint failure", -1.00m)
            ];

            DbResult<long> result = await _session.BulkInsertAsync(
                "Verification",
                DestinationTable,
                rows,
                s_shape,
                ct: ct);

            result.IsSuccess.Should().BeFalse();
            (await CountRowsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await ClearTargetAsync(ct);
        }
    }

    [Fact]
    public async Task BulkInsertAsync_WhenSqlExceptionOccurs_ShouldReturnRedactedFailureWithoutInnerException()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            BulkMutationRow[] rows =
            [
                new(301, "SQL failure row should not leak", -9.99m)
            ];

            DbResult<long> result = await _session.BulkInsertAsync(
                "Verification",
                DestinationTable,
                rows,
                s_shape,
                ct: ct);

            AssertRedactedFailure(
                result,
                "SQL failure row should not leak",
                "-9.99",
                "CK_BulkMutationTarget_Price_NonNegative");
            result.Error!.Value.Kind.Should().Be(DbErrorKind.ConstraintViolation);
            result.Error.Value.SqlErrorCode.Should().BeGreaterThan(0);
            (await CountRowsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await ClearTargetAsync(ct);
        }
    }

    [Fact]
    public async Task BulkInsertAsync_WhenUseTransactionFalse_ShouldInsertAsNonAtomicOptOut()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            BulkMutationRow[] rows =
            [
                new(351, "Non atomic opt out", 7.77m)
            ];

            DbResult<long> result = await _session.BulkInsertAsync(
                "Verification",
                DestinationTable,
                rows,
                s_shape,
                new BulkWriteOptions { UseTransaction = false },
                ct);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(rows.Length);
            (await CountRowsAsync(ct)).Should().Be(rows.Length);
        }
        finally
        {
            await ClearTargetAsync(ct);
        }
    }

    [Fact]
    public async Task BulkInsertAsync_WhenGeneralExceptionOccurs_ShouldReturnRedactedFailure()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            BulkMutationRow[] rows =
            [
                new(401, "GENERAL_EXCEPTION_ROW_VALUE", 1.23m)
            ];

            DbResult<long> result = await _session.BulkInsertAsync(
                "Verification",
                DestinationTable,
                rows,
                CreateThrowingShape("GENERAL_EXCEPTION_ROW_VALUE"),
                ct: ct);

            AssertRedactedFailure(
                result,
                "GENERAL_EXCEPTION_ROW_VALUE",
                "getter secret",
                "raw payload");
            result.Error!.Value.Kind.Should().Be(DbErrorKind.Unknown);
            (await CountRowsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await ClearTargetAsync(ct);
        }
    }

    [Fact]
    public async Task BulkInsertAsync_WhenCanceledBeforeCommit_ShouldAttemptRollbackBeforeRethrow()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);
        int rollbackAttempts = 0;
        BulkWriteExecutor.ResetTestHooks();
        BulkWriteExecutor.BeforeCommitAsync = static (_, token) => throw new OperationCanceledException(token);
        BulkWriteExecutor.RollbackAttempted = _ => Interlocked.Increment(ref rollbackAttempts);

        try
        {
            BulkMutationRow[] rows =
            [
                new(501, "Cancel before commit", 10.00m)
            ];

            Func<Task> act = () => _session.BulkInsertAsync(
                "Verification",
                DestinationTable,
                rows,
                s_shape,
                ct: ct);

            await act.Should().ThrowAsync<OperationCanceledException>();
            rollbackAttempts.Should().Be(1);
            (await CountRowsAsync(ct)).Should().Be(0);
        }
        finally
        {
            BulkWriteExecutor.ResetTestHooks();
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkInsertAsync_WhenRollbackFails_ShouldPreserveOriginalFailureAndRedactRollbackError()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);
        int rollbackAttempts = 0;
        BulkWriteExecutor.ResetTestHooks();
        BulkWriteExecutor.RollbackAttempted = _ => Interlocked.Increment(ref rollbackAttempts);
        BulkWriteExecutor.RollbackAsyncForTesting = static (_, _, _) =>
            throw new InvalidOperationException("rollback secret should not leak");

        try
        {
            BulkMutationRow[] rows =
            [
                new(601, "Rollback failure should not replace SQL failure", -1.23m)
            ];

            DbResult<long> result = await _session.BulkInsertAsync(
                "Verification",
                DestinationTable,
                rows,
                s_shape,
                ct: ct);

            AssertRedactedFailure(
                result,
                "Rollback failure should not replace SQL failure",
                "-1.23",
                "rollback secret");
            result.Error!.Value.Kind.Should().Be(DbErrorKind.ConstraintViolation);
            result.Error.Value.SqlErrorCode.Should().BeGreaterThan(0);
            rollbackAttempts.Should().Be(1);
            (await CountRowsAsync(ct)).Should().Be(0);
        }
        finally
        {
            BulkWriteExecutor.ResetTestHooks();
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    private async Task ClearTargetAsync(CancellationToken ct)
    {
        DbResult<int> result = await _db
            .Sql("DELETE FROM [gap].[BulkMutationTarget]")
            .ExecuteAsync(ct);

        result.IsSuccess.Should().BeTrue();
    }

    private async Task<int> CountRowsAsync(CancellationToken ct)
    {
        DbResult<int> result = await _db
            .Sql("SELECT COUNT(*) FROM [gap].[BulkMutationTarget]")
            .ExecuteScalarAsync<int>(ct);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static BulkShape<BulkMutationRow> CreateThrowingShape(string secret)
        => BulkShape.For<BulkMutationRow>()
            .Column("ExternalId", SqlDbType.Int, static row => row.ExternalId, nullable: false)
            .Column<string>(
                "Name",
                SqlDbType.NVarChar,
                _ => ThrowBulkName(secret),
                nullable: false,
                size: 100)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, nullable: false, precision: 18, scale: 2)
            .Build();

    private static string ThrowBulkName(string secret)
        => throw new InvalidOperationException($"getter secret raw payload {secret}");

    private static void AssertRedactedFailure(DbResult<long> result, params string[] forbiddenFragments)
    {
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();

        DbError error = result.Error!.Value;
        error.Message.Should().Be("Bulk insert failed.");
        error.ObjectName.Should().Be(SanitizedDestinationTable);
        error.InnerException.Should().BeNull();

        foreach (string fragment in forbiddenFragments)
        {
            error.Message.Should().NotContain(fragment);
            error.ObjectName.Should().NotContain(fragment);
            error.Hint.Should().NotContain(fragment);
        }
    }

    private sealed record BulkMutationRow(int ExternalId, string Name, decimal Price);
}
