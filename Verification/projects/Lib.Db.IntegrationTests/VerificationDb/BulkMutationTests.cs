using System.Data;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Execution.Bulk;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class BulkMutationTests(MultiDbFixture fixture)
{
    private const string DestinationTable = "gap.BulkMutationTarget";
    private const string SanitizedDestinationTable = "[gap].[BulkMutationTarget]";

    private static readonly BulkShape<BulkMutationRow> s_shape = BulkShape.For<BulkMutationRow>()
        .Key("ExternalId", SqlDbType.Int, static row => row.ExternalId, nullable: false)
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
                "Bulk insert failed.",
                SanitizedDestinationTable,
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
                "Bulk insert failed.",
                SanitizedDestinationTable,
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
                "Bulk insert failed.",
                SanitizedDestinationTable,
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

    [Fact]
    public async Task BulkUpdateAsync_WithStaticShape_ShouldUpdateOnlyMatchingRows()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<long> result = await _session.BulkUpdateAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "Seed one updated", 19.99m)],
                s_shape,
                ct: ct);

            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            result.Value.Should().Be(1);
            await AssertRowAsync(1001, "Seed one updated", 19.99m, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_WithStaticShape_ShouldDeleteOnlyMatchingKeys()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<long> result = await _session.BulkDeleteAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "ignored", 0m)],
                s_shape,
                ct: ct);

            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            result.Value.Should().Be(1);
            (await CountRowsAsync(ct)).Should().Be(1);
            await AssertMissingAsync(1001, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkUpdateAsync_WithDuplicateSourceKeys_ShouldFailBeforeChangingTarget()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<long> result = await _session.BulkUpdateAsync(
                "Verification",
                DestinationTable,
                [
                    new BulkMutationRow(1001, "duplicate source first", 31.00m),
                    new BulkMutationRow(1001, "duplicate source second", 32.00m)
                ],
                s_shape,
                ct: ct);

            AssertRedactedFailure(
                result,
                "Bulk update failed.",
                SanitizedDestinationTable,
                "duplicate source first",
                "duplicate source second",
                "31.00",
                "32.00");
            await AssertRowAsync(1001, "Seed one", 11.25m, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkUpdateAsync_WhenActionSqlFails_ShouldRollbackTargetChanges()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<long> result = await _session.BulkUpdateAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "constraint failure should not leak", -12.34m)],
                s_shape,
                ct: ct);

            AssertRedactedFailure(
                result,
                "Bulk update failed.",
                SanitizedDestinationTable,
                "constraint failure should not leak",
                "-12.34",
                "CK_BulkMutationTarget_Price_NonNegative");
            await AssertRowAsync(1001, "Seed one", 11.25m, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenCanceledBeforeCommit_ShouldAttemptRollbackBeforeRethrow()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);
        int rollbackAttempts = 0;
        BulkWriteExecutor.ResetTestHooks();

        try
        {
            await SeedRowsAsync(ct);
            BulkWriteExecutor.BeforeCommitAsync = static (_, token) => throw new OperationCanceledException(token);
            BulkWriteExecutor.RollbackAttempted = _ => Interlocked.Increment(ref rollbackAttempts);

            Func<Task> act = () => _session.BulkDeleteAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "ignored", 0m)],
                s_shape,
                ct: ct);

            await act.Should().ThrowAsync<OperationCanceledException>();
            rollbackAttempts.Should().Be(1);
            await AssertRowAsync(1001, "Seed one", 11.25m, CancellationToken.None);
            await AssertRowAsync(1002, "Seed two", 22.50m, CancellationToken.None);
        }
        finally
        {
            BulkWriteExecutor.ResetTestHooks();
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkUpdateAsync_WhenUseTransactionFalse_ShouldRejectBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkUpdateAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                options,
                token),
            new BulkWriteOptions { UseTransaction = false },
            "Bulk update failed.");
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenUseTransactionFalse_ShouldRejectBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkDeleteAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                options,
                token),
            new BulkWriteOptions { UseTransaction = false },
            "Bulk delete failed.");
    }

    [Fact]
    public async Task BulkUpdateAsync_WhenFireTriggersTrue_ShouldRejectBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkUpdateAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                options,
                token),
            new BulkWriteOptions { FireTriggers = true },
            "Bulk update failed.");
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenCheckConstraintsFalse_ShouldRejectBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkDeleteAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                options,
                token),
            new BulkWriteOptions { CheckConstraints = false },
            "Bulk delete failed.");
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenKeepIdentityTrue_ShouldRejectBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkDeleteAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                options,
                token),
            new BulkWriteOptions { KeepIdentity = true },
            "Bulk delete failed.");
    }

    [Fact]
    public async Task BulkDeleteAsync_ShouldStageKeyColumnsOnly()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<long> result = await _session.BulkDeleteAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "DELETE_WRITABLE_SHOULD_NOT_BE_READ", 0m)],
                CreateDeleteShapeWithThrowingWritableColumn("DELETE_WRITABLE_SHOULD_NOT_BE_READ"),
                ct: ct);

            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            result.Value.Should().Be(1);
            await AssertMissingAsync(1001, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkUpsertAsync_WithStaticShape_ShouldUpdateMatchedAndInsertMissing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<BulkUpsertResult> result = await _session.BulkUpsertAsync(
                "Verification",
                DestinationTable,
                [
                    new BulkMutationRow(1001, "Seed one upserted", 19.99m),
                    new BulkMutationRow(1003, "Seed three inserted", 33.75m)
                ],
                s_shape,
                ct: ct);

            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            result.Value.Updated.Should().Be(1);
            result.Value.Inserted.Should().Be(1);
            result.Value.TotalAffected.Should().Be(2);
            (await CountRowsAsync(ct)).Should().Be(3);
            await AssertRowAsync(1001, "Seed one upserted", 19.99m, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
            await AssertRowAsync(1003, "Seed three inserted", 33.75m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkMergeAsync_WithDefaultActions_ShouldUpdateMatchedInsertMissingAndReturnSeparatedCounts()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<BulkMergeResult> result = await _session.BulkMergeAsync(
                "Verification",
                DestinationTable,
                [
                    new BulkMutationRow(1001, "Seed one merged", 29.99m),
                    new BulkMutationRow(1003, "Seed three merged", 43.75m)
                ],
                s_shape,
                ct: ct);

            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            result.Value.Updated.Should().Be(1);
            result.Value.Inserted.Should().Be(1);
            result.Value.Deleted.Should().Be(0);
            result.Value.TotalAffected.Should().Be(2);
            (await CountRowsAsync(ct)).Should().Be(3);
            await AssertRowAsync(1001, "Seed one merged", 29.99m, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
            await AssertRowAsync(1003, "Seed three merged", 43.75m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkMergeAsync_WithDeleteMatched_ShouldDeleteOnlyStagedKeys()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<BulkMergeResult> result = await _session.BulkMergeAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "ignored", 0m)],
                s_shape,
                new BulkMergeOptions { Actions = BulkMergeActions.DeleteMatched },
                ct);

            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            result.Value.Updated.Should().Be(0);
            result.Value.Inserted.Should().Be(0);
            result.Value.Deleted.Should().Be(1);
            result.Value.TotalAffected.Should().Be(1);
            (await CountRowsAsync(ct)).Should().Be(1);
            await AssertMissingAsync(1001, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(BulkMergeActions.UpdateMatched | BulkMergeActions.DeleteMatched)]
    [InlineData(BulkMergeActions.InsertMissing | BulkMergeActions.DeleteMatched)]
    public async Task BulkMergeAsync_WithInvalidDeleteMatchedCombination_ShouldFailBeforeChangingTarget(
        BulkMergeActions actions)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);

        try
        {
            await SeedRowsAsync(ct);

            DbResult<BulkMergeResult> result = await _session.BulkMergeAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "Seed one should not change", 99.99m)],
                s_shape,
                new BulkMergeOptions { Actions = actions },
                ct);

            result.IsSuccess.Should().BeFalse();
            (await CountRowsAsync(ct)).Should().Be(2);
            await AssertRowAsync(1001, "Seed one", 11.25m, ct);
            await AssertRowAsync(1002, "Seed two", 22.50m, ct);
        }
        finally
        {
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkUpsertAsync_WhenInsertMissingFails_ShouldRollbackPriorUpdate()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);
        BulkWriteExecutor.ResetTestHooks();
        BulkWriteExecutor.BeforeInsertMissingAsync = static (_, _) =>
            throw new InvalidOperationException("insert missing secret raw payload");

        try
        {
            await SeedRowsAsync(ct);

            DbResult<BulkUpsertResult> result = await _session.BulkUpsertAsync(
                "Verification",
                DestinationTable,
                [
                    new BulkMutationRow(1001, "Seed one should rollback", 19.99m),
                    new BulkMutationRow(1003, "Seed three should not insert", 33.75m)
                ],
                s_shape,
                ct: ct);

            AssertRedactedFailure(
                result,
                "Bulk upsert failed.",
                SanitizedDestinationTable,
                "insert missing secret",
                "raw payload",
                "Seed one should rollback",
                "Seed three should not insert",
                "33.75");
            await AssertRowAsync(1001, "Seed one", 11.25m, CancellationToken.None);
            await AssertRowAsync(1002, "Seed two", 22.50m, CancellationToken.None);
            await AssertMissingAsync(1003, CancellationToken.None);
        }
        finally
        {
            BulkWriteExecutor.ResetTestHooks();
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkMergeAsync_WhenCanceledAfterActionBeforeCommit_ShouldAttemptRollbackBeforeRethrow()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearTargetAsync(ct);
        int rollbackAttempts = 0;
        BulkWriteExecutor.ResetTestHooks();

        try
        {
            await SeedRowsAsync(ct);
            BulkWriteExecutor.BeforeCommitAsync = static (_, token) => throw new OperationCanceledException(token);
            BulkWriteExecutor.RollbackAttempted = _ => Interlocked.Increment(ref rollbackAttempts);

            Func<Task> act = () => _session.BulkMergeAsync(
                "Verification",
                DestinationTable,
                [
                    new BulkMutationRow(1001, "Seed one should rollback", 19.99m),
                    new BulkMutationRow(1003, "Seed three should not insert", 33.75m)
                ],
                s_shape,
                ct: ct);

            await act.Should().ThrowAsync<OperationCanceledException>();
            rollbackAttempts.Should().Be(1);
            await AssertRowAsync(1001, "Seed one", 11.25m, CancellationToken.None);
            await AssertRowAsync(1002, "Seed two", 22.50m, CancellationToken.None);
            await AssertMissingAsync(1003, CancellationToken.None);
        }
        finally
        {
            BulkWriteExecutor.ResetTestHooks();
            await ClearTargetAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkUpsertAsync_WhenUseTransactionFalse_ShouldRejectBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkUpsertAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                options,
                token),
            new BulkWriteOptions { UseTransaction = false },
            "Bulk upsert failed.");
    }

    [Fact]
    public async Task BulkMergeAsync_WhenUseTransactionFalse_ShouldRejectBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkMergeAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                (BulkMergeOptions)options,
                token),
            new BulkMergeOptions { UseTransaction = false },
            "Bulk merge failed.");
    }

    [Fact]
    public async Task BulkUpsertAsync_WhenKeepIdentityTrue_ShouldRejectBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkUpsertAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                options,
                token),
            new BulkWriteOptions { KeepIdentity = true },
            "Bulk upsert failed.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BulkMergeAsync_WhenFireTriggersTrueOrCheckConstraintsFalse_ShouldRejectBeforeOpeningConnection(
        bool fireTriggers)
    {
        BulkMergeOptions options = fireTriggers
            ? new BulkMergeOptions { FireTriggers = true }
            : new BulkMergeOptions { CheckConstraints = false };

        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkMergeAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                (BulkMergeOptions)options,
                token),
            options,
            "Bulk merge failed.");
    }

    [Fact]
    public async Task BulkMergeAsync_WithUnknownActionBits_ShouldFailBeforeOpeningConnection()
    {
        await AssertRejectsBeforeOpeningConnectionAsync(
            static (executor, options, token) => executor.BulkMergeAsync(
                "Verification",
                DestinationTable,
                [new BulkMutationRow(1001, "pre-open validation", 1.00m)],
                s_shape,
                (BulkMergeOptions)options,
                token),
            new BulkMergeOptions { Actions = (BulkMergeActions)16 },
            "Bulk merge failed.");
    }

    private async Task ClearTargetAsync(CancellationToken ct)
    {
        DbResult<int> result = await _db
            .Sql("""
                DELETE FROM [gap].[BulkMutationAudit];
                DELETE FROM [gap].[BulkMutationTarget];
                """)
            .ExecuteAsync(ct);

        result.IsSuccess.Should().BeTrue();
    }

    private async Task SeedRowsAsync(CancellationToken ct)
    {
        BulkMutationRow[] rows =
        [
            new(1001, "Seed one", 11.25m),
            new(1002, "Seed two", 22.50m)
        ];

        DbResult<long> result = await _session.BulkInsertAsync(
            "Verification",
            DestinationTable,
            rows,
            s_shape,
            ct: ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().Be(rows.Length);
    }

    private async Task<int> CountRowsAsync(CancellationToken ct)
    {
        DbResult<int> result = await _db
            .Sql("SELECT COUNT(*) FROM [gap].[BulkMutationTarget]")
            .ExecuteScalarAsync<int>(ct);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task AssertRowAsync(int externalId, string expectedName, decimal expectedPrice, CancellationToken ct)
    {
        BulkMutationSnapshot? row = await GetRowAsync(externalId, ct);

        row.Should().NotBeNull();
        row!.Name.Should().Be(expectedName);
        row.Price.Should().Be(expectedPrice);
    }

    private async Task AssertMissingAsync(int externalId, CancellationToken ct)
    {
        BulkMutationSnapshot? row = await GetRowAsync(externalId, ct);

        row.Should().BeNull();
    }

    private async Task<BulkMutationSnapshot?> GetRowAsync(int externalId, CancellationToken ct)
    {
        DbResult<Dictionary<string, object?>?> result = await _db
            .Sql((FormattableString)$"""
                SELECT [ExternalId], [Name], [Price]
                FROM [gap].[BulkMutationTarget]
                WHERE [ExternalId] = {externalId}
                """)
            .QuerySingleAsync<Dictionary<string, object?>>(ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        if (result.Value is null)
            return null;

        return new BulkMutationSnapshot(
            (int)result.Value["ExternalId"]!,
            (string)result.Value["Name"]!,
            (decimal)result.Value["Price"]!);
    }

    private static async Task AssertRejectsBeforeOpeningConnectionAsync<TResult>(
        Func<BulkWriteExecutor, BulkWriteOptions, CancellationToken, Task<DbResult<TResult>>> act,
        BulkWriteOptions options,
        string expectedMessage)
    {
        CountingConnectionFactory connectionFactory = new();
        BulkWriteExecutor executor = new(connectionFactory);

        DbResult<TResult> result = await act(executor, options, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Message.Should().Be(expectedMessage);
        result.Error.Value.ObjectName.Should().Be(SanitizedDestinationTable);
        result.Error.Value.InnerException.Should().BeNull();
        connectionFactory.OpenAttempts.Should().Be(0);
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

    private static BulkShape<BulkMutationRow> CreateDeleteShapeWithThrowingWritableColumn(string secret)
        => BulkShape.For<BulkMutationRow>()
            .Key("ExternalId", SqlDbType.Int, static row => row.ExternalId, nullable: false)
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

    private static void AssertRedactedFailure<TResult>(
        DbResult<TResult> result,
        string expectedMessage,
        string? expectedObjectName,
        params string[] forbiddenFragments)
    {
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();

        DbError error = result.Error!.Value;
        error.Message.Should().Be(expectedMessage);
        error.ObjectName.Should().Be(expectedObjectName);
        error.InnerException.Should().BeNull();

        foreach (string fragment in forbiddenFragments)
        {
            error.Message.Should().NotContain(fragment);
            error.ObjectName.Should().NotContain(fragment);
            error.Hint.Should().NotContain(fragment);
        }
    }

    private sealed class CountingConnectionFactory : IDbConnectionFactory
    {
        private int _openAttempts;

        public int OpenAttempts => _openAttempts;

        public Task<SqlConnection> CreateConnectionAsync(string instanceHash, CancellationToken ct)
        {
            Interlocked.Increment(ref _openAttempts);
            throw new InvalidOperationException("A staged mutation pre-open validation test attempted to open a connection.");
        }

        public void RegisterAdHocInstance(string instanceName, string connectionString)
        {
        }

        public void UnregisterAdHocInstance(string instanceName)
        {
        }
    }

    private sealed record BulkMutationRow(int ExternalId, string Name, decimal Price);
    private sealed record BulkMutationSnapshot(int ExternalId, string Name, decimal Price);
}
