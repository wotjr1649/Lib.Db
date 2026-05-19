-- ============================================================================
-- File: verify-libdb-chaos-test.sql
-- Purpose: Chaos database readiness checks for LIBDB_CHAOS_TEST.
-- Run after: setup-libdb-chaos-test.sql
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i verify-libdb-chaos-test.sql -f 65001 -b
-- ============================================================================

USE [LIBDB_CHAOS_TEST];
GO

SET NOCOUNT ON;
SET XACT_ABORT OFF;

DECLARE @Failures TABLE ([Scope] NVARCHAR(120) NOT NULL, [Detail] NVARCHAR(4000) NOT NULL);

DECLARE @ExpectedTables TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTables VALUES
(N'chaos.FaultLog'), (N'chaos.DelayLedger'), (N'chaos.LockA'), (N'chaos.LockB'), (N'chaos.Outbox'),
(N'chaos.TvpFaultTarget'), (N'chaos.TvpFaultAudit'), (N'chaos.TvpRollbackTarget'),
(N'chaos.TvpDeadlockA'), (N'chaos.TvpDeadlockB'), (N'chaos.TvpLockTarget'),
(N'chaos.TvpTimeoutTarget'), (N'chaos.TvpConstraintParent'), (N'chaos.TvpConstraintChild'),
(N'chaos.TvpConversionTarget'), (N'chaos.TvpCancellationLedger'), (N'chaos.TvpPoisonQueue');

INSERT INTO @Failures
SELECT N'table-exists', CONCAT(N'Missing table: ', [Name])
FROM @ExpectedTables
WHERE OBJECT_ID(QUOTENAME(PARSENAME([Name], 2)) + N'.' + QUOTENAME(PARSENAME([Name], 1)), N'U') IS NULL;

DECLARE @ExpectedProcedures TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedProcedures VALUES
(N'chaos.usp_LogFault'), (N'chaos.usp_SimulateDelay'), (N'chaos.usp_ThrowUserError'),
(N'chaos.usp_HoldApplicationLock'), (N'chaos.usp_Deadlock_Left'), (N'chaos.usp_Deadlock_Right'),
(N'chaos.usp_LockTimeoutVictim'), (N'chaos.usp_InsertOutboxThenFail'), (N'chaos.usp_KillSession'),
(N'chaos.usp_Tvp_InsertFaultRows'), (N'chaos.usp_Tvp_InsertThenRollback'),
(N'chaos.usp_Tvp_DuplicateKeyViolation'), (N'chaos.usp_Tvp_NotNullViolation'),
(N'chaos.usp_Tvp_ForeignKeyViolation'), (N'chaos.usp_Tvp_ConversionFailure'),
(N'chaos.usp_Tvp_LockTimeoutWriter'), (N'chaos.usp_Tvp_LockTimeoutVictim'),
(N'chaos.usp_Tvp_Deadlock_Left'), (N'chaos.usp_Tvp_Deadlock_Right'),
(N'chaos.usp_Tvp_DelayedInsert'), (N'chaos.usp_Tvp_CancelProbe'),
(N'chaos.usp_Tvp_AppLock_Insert'), (N'chaos.usp_Tvp_PartialFailure_XactAbort'),
(N'chaos.usp_Tvp_SavepointRollback'), (N'chaos.usp_Tvp_RetryableError'),
(N'chaos.usp_Tvp_PoisonEnqueue'), (N'chaos.usp_Tvp_PoisonDrain'),
(N'chaos.usp_Tvp_Lob_RaiseOnLarge'), (N'chaos.usp_Tvp_ObserveLocks'),
(N'chaos.usp_Tvp_ObserveFaults'), (N'chaos.usp_Tvp_ClearFaultData'),
(N'chaos.usp_Tvp_CheckRollbackEmpty'), (N'chaos.usp_Tvp_ThrowAfterResultset'),
(N'chaos.usp_Tvp_ResetChaosMatrixData');

INSERT INTO @Failures
SELECT N'procedure-exists', CONCAT(N'Missing procedure: ', [Name])
FROM @ExpectedProcedures
WHERE OBJECT_ID(QUOTENAME(PARSENAME([Name], 2)) + N'.' + QUOTENAME(PARSENAME([Name], 1)), N'P') IS NULL;

DECLARE @ExpectedTypes TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTypes VALUES
(N'chaos.Tvp_FaultRows'), (N'chaos.Tvp_DuplicateKeyRows'), (N'chaos.Tvp_NullViolationRows'),
(N'chaos.Tvp_ForeignKeyRows'), (N'chaos.Tvp_ConversionRows'), (N'chaos.Tvp_LockRows'),
(N'chaos.Tvp_DeadlockRows'), (N'chaos.Tvp_LobRows');

INSERT INTO @Failures
SELECT N'tvp-type-exists', CONCAT(N'Missing TVP type: ', [Name])
FROM @ExpectedTypes
WHERE TYPE_ID([Name]) IS NULL;

DECLARE @LogFault TABLE ([FaultId] NUMERIC(38,0), [Spid] INT);
INSERT INTO @LogFault EXEC [chaos].[usp_LogFault] @FaultName = N'verify', @Payload = N'smoke';
IF NOT EXISTS (SELECT 1 FROM @LogFault WHERE [FaultId] IS NOT NULL AND [Spid] > 0)
    INSERT INTO @Failures VALUES (N'fault-log-smoke', N'chaos.usp_LogFault did not return fault id/spid.');

DECLARE @Delay TABLE ([DelayId] BIGINT, [DelayMilliseconds] INT);
INSERT INTO @Delay EXEC [chaos].[usp_SimulateDelay] @DelayMilliseconds = 0;
IF NOT EXISTS (SELECT 1 FROM @Delay WHERE [DelayMilliseconds] = 0)
    INSERT INTO @Failures VALUES (N'delay-smoke', N'chaos.usp_SimulateDelay did not complete 0 ms delay.');

BEGIN TRY
    EXEC [chaos].[usp_ThrowUserError] @ErrorNumber = 50101, @Message = N'Expected chaos verify error';
    INSERT INTO @Failures VALUES (N'expected-error', N'chaos.usp_ThrowUserError did not throw.');
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 50101
        INSERT INTO @Failures VALUES (N'expected-error', CONCAT(N'Unexpected chaos error number: ', ERROR_NUMBER()));
END CATCH;

BEGIN TRY
    EXEC [chaos].[usp_HoldApplicationLock] @Resource = N'libdb-verify', @HoldMilliseconds = 0;
END TRY
BEGIN CATCH
    INSERT INTO @Failures VALUES (N'applock-smoke', CONCAT(N'Application lock smoke failed: ', ERROR_MESSAGE()));
END CATCH;

BEGIN TRY
    EXEC [chaos].[usp_InsertOutboxThenFail] @MessageKey = N'verify', @Payload = N'{}';
    INSERT INTO @Failures VALUES (N'rollback-fault', N'chaos.usp_InsertOutboxThenFail did not throw.');
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 50120
        INSERT INTO @Failures VALUES (N'rollback-fault', CONCAT(N'Unexpected rollback fault error number: ', ERROR_NUMBER()));
END CATCH;

BEGIN TRY
    EXEC [chaos].[usp_KillSession] @TargetSpid = @@SPID, @Confirm = N'NO';
    INSERT INTO @Failures VALUES (N'kill-guard', N'chaos.usp_KillSession guard did not reject missing confirmation.');
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 50130
        INSERT INTO @Failures VALUES (N'kill-guard', CONCAT(N'Unexpected kill guard error number: ', ERROR_NUMBER()));
END CATCH;

IF NOT EXISTS (SELECT 1 FROM [chaos].[LockA] WHERE [Id] = 1)
    INSERT INTO @Failures VALUES (N'deadlock-readiness', N'chaos.LockA seed row is missing.');
IF NOT EXISTS (SELECT 1 FROM [chaos].[LockB] WHERE [Id] = 1)
    INSERT INTO @Failures VALUES (N'deadlock-readiness', N'chaos.LockB seed row is missing.');
IF NOT EXISTS (SELECT 1 FROM [chaos].[TvpDeadlockA] WHERE [Id] = 1)
    INSERT INTO @Failures VALUES (N'tvp-deadlock-readiness', N'chaos.TvpDeadlockA seed row is missing.');
IF NOT EXISTS (SELECT 1 FROM [chaos].[TvpDeadlockB] WHERE [Id] = 1)
    INSERT INTO @Failures VALUES (N'tvp-deadlock-readiness', N'chaos.TvpDeadlockB seed row is missing.');

-- Server-level Extended Events are intentionally outside this DB-scoped verification script.

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_query_store_options
    WHERE [desired_state_desc] = N'READ_WRITE'
      AND [actual_state_desc] = N'READ_WRITE'
)
    INSERT INTO @Failures VALUES (N'query-store', N'Query Store must be enabled for chaos workload analysis.');

IF EXISTS (SELECT 1 FROM @Failures)
BEGIN
    SELECT [Scope], [Detail] FROM @Failures ORDER BY [Scope], [Detail];
    DECLARE @Message NVARCHAR(2048) = CONCAT(N'LIBDB_CHAOS_TEST verification failed: ', (SELECT COUNT(*) FROM @Failures), N' issue(s).');
    THROW 51200, @Message, 1;
END;

SELECT N'LIBDB_CHAOS_TEST verification passed.' AS [Result],
       (SELECT COUNT(*) FROM @ExpectedTables) AS [ExpectedTables],
       (SELECT COUNT(*) FROM @ExpectedTypes) AS [ExpectedTypes],
       (SELECT COUNT(*) FROM @ExpectedProcedures) AS [ExpectedProcedures],
       N'Network/service restart/tempdb/disk pressure chaos requires external harness.' AS [ChaosNote];
GO
