-- ============================================================================
-- File: setup-libdb-chaos-test.sql
-- Purpose: Isolated chaos/fault-injection database for Lib.Db v2.3.0.
-- Target DB: LIBDB_CHAOS_TEST
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i setup-libdb-chaos-test.sql -f 65001
-- Notes:
--   - Procedures are intentionally sharp-edged but guarded where needed.
--   - Do not point production applications at this database.
-- ============================================================================

USE [master];
GO

IF DB_ID(N'LIBDB_CHAOS_TEST') IS NULL
BEGIN
    CREATE DATABASE [LIBDB_CHAOS_TEST];
END;
GO

ALTER DATABASE [LIBDB_CHAOS_TEST] SET QUERY_STORE = ON;
GO

USE [LIBDB_CHAOS_TEST];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'chaos') EXEC(N'CREATE SCHEMA [chaos]');
GO

IF OBJECT_ID(N'[chaos].[FaultLog]', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[FaultLog]
    (
        [FaultId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_chaos_FaultLog] PRIMARY KEY,
        [FaultName] NVARCHAR(100) NOT NULL,
        [Spid] INT NOT NULL,
        [Payload] NVARCHAR(4000) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_chaos_FaultLog_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[chaos].[DelayLedger]', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[DelayLedger]
    (
        [DelayId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_chaos_DelayLedger] PRIMARY KEY,
        [DelayMilliseconds] INT NOT NULL,
        [StartedAt] DATETIME2(7) NOT NULL,
        [CompletedAt] DATETIME2(7) NULL
    );
END;

IF OBJECT_ID(N'[chaos].[LockA]', N'U') IS NULL
    CREATE TABLE [chaos].[LockA] ([Id] INT NOT NULL CONSTRAINT [PK_chaos_LockA] PRIMARY KEY, [Value] INT NOT NULL);

IF OBJECT_ID(N'[chaos].[LockB]', N'U') IS NULL
    CREATE TABLE [chaos].[LockB] ([Id] INT NOT NULL CONSTRAINT [PK_chaos_LockB] PRIMARY KEY, [Value] INT NOT NULL);

IF OBJECT_ID(N'[chaos].[Outbox]', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[Outbox]
    (
        [MessageId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_chaos_Outbox] PRIMARY KEY,
        [MessageKey] NVARCHAR(100) NOT NULL,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [ProcessedAt] DATETIME2(7) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_chaos_Outbox_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_LogFault]
    @FaultName NVARCHAR(100),
    @Payload NVARCHAR(4000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[FaultLog] ([FaultName], [Spid], [Payload])
    VALUES (@FaultName, @@SPID, @Payload);
    SELECT SCOPE_IDENTITY() AS [FaultId], @@SPID AS [Spid];
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_SimulateDelay]
    @DelayMilliseconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SafeDelayMilliseconds INT = CASE WHEN @DelayMilliseconds BETWEEN 0 AND 59000 THEN @DelayMilliseconds ELSE 1000 END;
    INSERT INTO [chaos].[DelayLedger] ([DelayMilliseconds], [StartedAt]) VALUES (@SafeDelayMilliseconds, SYSUTCDATETIME());
    DECLARE @DelayId BIGINT = SCOPE_IDENTITY();
    DECLARE @Delay CHAR(12) = CONCAT('00:00:', RIGHT('0' + CAST(@SafeDelayMilliseconds / 1000 AS VARCHAR(2)), 2), '.', RIGHT('000' + CAST(@SafeDelayMilliseconds % 1000 AS VARCHAR(3)), 3));
    WAITFOR DELAY @Delay;
    UPDATE [chaos].[DelayLedger] SET [CompletedAt] = SYSUTCDATETIME() WHERE [DelayId] = @DelayId;
    SELECT @DelayId AS [DelayId], @SafeDelayMilliseconds AS [DelayMilliseconds];
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_ThrowUserError]
    @ErrorNumber INT = 50100,
    @Message NVARCHAR(2048) = N'Chaos user error'
AS
BEGIN
    SET NOCOUNT ON;
    IF @ErrorNumber < 50000 SET @ErrorNumber = 50100;
    EXEC [chaos].[usp_LogFault] @FaultName = N'user-error', @Payload = @Message;
    THROW @ErrorNumber, @Message, 1;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_HoldApplicationLock]
    @Resource NVARCHAR(255),
    @HoldMilliseconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Result INT;
    BEGIN TRANSACTION;
    EXEC @Result = sp_getapplock
        @Resource = @Resource,
        @LockMode = 'Exclusive',
        @LockOwner = 'Transaction',
        @LockTimeout = 1000;
    IF @Result < 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50110, N'Unable to acquire application lock.', 1;
    END;
    EXEC [chaos].[usp_SimulateDelay] @DelayMilliseconds = @HoldMilliseconds;
    COMMIT TRANSACTION;
    SELECT @Result AS [AppLockResult];
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Deadlock_Left]
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE [chaos].[LockA] SET [Value] = [Value] + 1 WHERE [Id] = 1;
    WAITFOR DELAY '00:00:02';
    UPDATE [chaos].[LockB] SET [Value] = [Value] + 1 WHERE [Id] = 1;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Deadlock_Right]
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE [chaos].[LockB] SET [Value] = [Value] + 1 WHERE [Id] = 1;
    WAITFOR DELAY '00:00:02';
    UPDATE [chaos].[LockA] SET [Value] = [Value] + 1 WHERE [Id] = 1;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_LockTimeoutVictim]
    @LockTimeoutMilliseconds INT = 500
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SafeLockTimeoutMilliseconds INT = CASE WHEN @LockTimeoutMilliseconds >= -1 THEN @LockTimeoutMilliseconds ELSE 0 END;
    DECLARE @LockTimeoutSql NVARCHAR(MAX) = CONCAT(
        N'SET LOCK_TIMEOUT ', CONVERT(NVARCHAR(20), @SafeLockTimeoutMilliseconds), N';
UPDATE [chaos].[LockA] SET [Value] = [Value] + 1 WHERE [Id] = 1;
SELECT @@ROWCOUNT AS [RowsAffected];');
    EXEC sys.sp_executesql @LockTimeoutSql;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_InsertOutboxThenFail]
    @MessageKey NVARCHAR(100),
    @Payload NVARCHAR(MAX)
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT INTO [chaos].[Outbox] ([MessageKey], [Payload]) VALUES (@MessageKey, @Payload);
        THROW 50120, N'Chaos rollback after outbox insert.', 1;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_KillSession]
    @TargetSpid INT,
    @Confirm NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Confirm <> N'KILL_LIBDB_SESSION'
        THROW 50130, N'KILL guard confirmation is required.', 1;
    IF @TargetSpid IS NULL OR @TargetSpid = @@SPID OR @TargetSpid <= 50
        THROW 50131, N'Refusing to kill this session, a system session, or an invalid session.', 1;
    DECLARE @sql NVARCHAR(100) = CONCAT(N'KILL ', CONVERT(NVARCHAR(20), @TargetSpid));
    EXEC [chaos].[usp_LogFault] @FaultName = N'kill-session', @Payload = @sql;
    EXEC (@sql);
END;
GO

-- Server-level Extended Events are intentionally not created by this DB-scoped setup.
-- Keep this script limited to LIBDB_CHAOS_TEST unless a separate server-level setup is explicitly approved.
GO

IF NOT EXISTS (SELECT 1 FROM [chaos].[LockA] WHERE [Id] = 1) INSERT INTO [chaos].[LockA] ([Id], [Value]) VALUES (1, 100);
IF NOT EXISTS (SELECT 1 FROM [chaos].[LockB] WHERE [Id] = 1) INSERT INTO [chaos].[LockB] ([Id], [Value]) VALUES (1, 200);
GO

IF OBJECT_ID(N'chaos.TvpFaultTarget', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[TvpFaultTarget]
    (
        [Id] INT NOT NULL CONSTRAINT [PK_chaos_TvpFaultTarget] PRIMARY KEY,
        [Payload] NVARCHAR(400) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_chaos_TvpFaultTarget_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'chaos.TvpFaultAudit', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[TvpFaultAudit]
    (
        [AuditId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_chaos_TvpFaultAudit] PRIMARY KEY,
        [FaultName] NVARCHAR(128) NOT NULL,
        [RowsObserved] INT NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_chaos_TvpFaultAudit_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'chaos.TvpRollbackTarget', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[TvpRollbackTarget]
    (
        [Id] INT NOT NULL,
        [Payload] NVARCHAR(400) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_chaos_TvpRollbackTarget_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'chaos.TvpDeadlockA', N'U') IS NULL
    CREATE TABLE [chaos].[TvpDeadlockA] ([Id] INT NOT NULL CONSTRAINT [PK_chaos_TvpDeadlockA] PRIMARY KEY, [Value] INT NOT NULL);
GO

IF OBJECT_ID(N'chaos.TvpDeadlockB', N'U') IS NULL
    CREATE TABLE [chaos].[TvpDeadlockB] ([Id] INT NOT NULL CONSTRAINT [PK_chaos_TvpDeadlockB] PRIMARY KEY, [Value] INT NOT NULL);
GO

IF OBJECT_ID(N'chaos.TvpLockTarget', N'U') IS NULL
    CREATE TABLE [chaos].[TvpLockTarget] ([Id] INT NOT NULL CONSTRAINT [PK_chaos_TvpLockTarget] PRIMARY KEY, [Value] INT NOT NULL);
GO

IF OBJECT_ID(N'chaos.TvpTimeoutTarget', N'U') IS NULL
    CREATE TABLE [chaos].[TvpTimeoutTarget] ([Id] INT NOT NULL CONSTRAINT [PK_chaos_TvpTimeoutTarget] PRIMARY KEY, [Value] INT NOT NULL);
GO

IF OBJECT_ID(N'chaos.TvpConstraintParent', N'U') IS NULL
    CREATE TABLE [chaos].[TvpConstraintParent] ([ParentId] INT NOT NULL CONSTRAINT [PK_chaos_TvpConstraintParent] PRIMARY KEY);
GO

IF OBJECT_ID(N'chaos.TvpConstraintChild', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[TvpConstraintChild]
    (
        [ChildId] INT NOT NULL CONSTRAINT [PK_chaos_TvpConstraintChild] PRIMARY KEY,
        [ParentId] INT NOT NULL,
        CONSTRAINT [FK_chaos_TvpConstraintChild_Parent] FOREIGN KEY ([ParentId]) REFERENCES [chaos].[TvpConstraintParent] ([ParentId])
    );
END;
GO

IF OBJECT_ID(N'chaos.TvpConversionTarget', N'U') IS NULL
    CREATE TABLE [chaos].[TvpConversionTarget] ([Id] INT NOT NULL CONSTRAINT [PK_chaos_TvpConversionTarget] PRIMARY KEY, [NumericValue] INT NOT NULL);
GO

IF OBJECT_ID(N'chaos.TvpCancellationLedger', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[TvpCancellationLedger]
    (
        [LedgerId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_chaos_TvpCancellationLedger] PRIMARY KEY,
        [ScenarioName] NVARCHAR(128) NOT NULL,
        [RowsObserved] INT NOT NULL,
        [DelayMilliseconds] INT NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_chaos_TvpCancellationLedger_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'chaos.TvpPoisonQueue', N'U') IS NULL
BEGIN
    CREATE TABLE [chaos].[TvpPoisonQueue]
    (
        [QueueId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_chaos_TvpPoisonQueue] PRIMARY KEY,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [Status] NVARCHAR(32) NOT NULL CONSTRAINT [DF_chaos_TvpPoisonQueue_Status] DEFAULT N'Pending',
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_chaos_TvpPoisonQueue_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF TYPE_ID(N'chaos.Tvp_FaultRows') IS NULL
    EXEC(N'CREATE TYPE [chaos].[Tvp_FaultRows] AS TABLE ([Id] INT NOT NULL, [Payload] NVARCHAR(400) NOT NULL);');
IF TYPE_ID(N'chaos.Tvp_DuplicateKeyRows') IS NULL
    EXEC(N'CREATE TYPE [chaos].[Tvp_DuplicateKeyRows] AS TABLE ([Id] INT NOT NULL, [Payload] NVARCHAR(400) NOT NULL);');
IF TYPE_ID(N'chaos.Tvp_NullViolationRows') IS NULL
    EXEC(N'CREATE TYPE [chaos].[Tvp_NullViolationRows] AS TABLE ([Id] INT NOT NULL, [RequiredText] NVARCHAR(100) NULL);');
IF TYPE_ID(N'chaos.Tvp_ForeignKeyRows') IS NULL
    EXEC(N'CREATE TYPE [chaos].[Tvp_ForeignKeyRows] AS TABLE ([ParentId] INT NOT NULL, [ChildId] INT NOT NULL);');
IF TYPE_ID(N'chaos.Tvp_ConversionRows') IS NULL
    EXEC(N'CREATE TYPE [chaos].[Tvp_ConversionRows] AS TABLE ([Id] INT NOT NULL, [NumericText] NVARCHAR(50) NOT NULL);');
IF TYPE_ID(N'chaos.Tvp_LockRows') IS NULL
    EXEC(N'CREATE TYPE [chaos].[Tvp_LockRows] AS TABLE ([Id] INT NOT NULL, [Delta] INT NOT NULL);');
IF TYPE_ID(N'chaos.Tvp_DeadlockRows') IS NULL
    EXEC(N'CREATE TYPE [chaos].[Tvp_DeadlockRows] AS TABLE ([Id] INT NOT NULL, [Delta] INT NOT NULL);');
IF TYPE_ID(N'chaos.Tvp_LobRows') IS NULL
    EXEC(N'CREATE TYPE [chaos].[Tvp_LobRows] AS TABLE ([Id] INT NOT NULL, [Payload] NVARCHAR(MAX) NOT NULL);');
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_InsertFaultRows]
    @Rows [chaos].[Tvp_FaultRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[TvpFaultTarget] ([Id], [Payload])
    SELECT [Id], [Payload]
    FROM @Rows AS rows
    WHERE NOT EXISTS (SELECT 1 FROM [chaos].[TvpFaultTarget] AS target WHERE target.[Id] = rows.[Id]);
    INSERT INTO [chaos].[TvpFaultAudit] ([FaultName], [RowsObserved]) VALUES (N'insert-fault-rows', (SELECT COUNT(*) FROM @Rows));
    SELECT COUNT_BIG(*) AS [InputRows] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_InsertThenRollback]
    @Rows [chaos].[Tvp_FaultRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT INTO [chaos].[TvpRollbackTarget] ([Id], [Payload])
        SELECT [Id], [Payload] FROM @Rows;
        THROW 51210, N'TVP rollback chaos fault.', 1;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_DuplicateKeyViolation]
    @Rows [chaos].[Tvp_DuplicateKeyRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[TvpFaultTarget] ([Id], [Payload])
    SELECT [Id], [Payload] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_NotNullViolation]
    @Rows [chaos].[Tvp_NullViolationRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[TvpFaultTarget] ([Id], [Payload])
    SELECT [Id], [RequiredText] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_ForeignKeyViolation]
    @Rows [chaos].[Tvp_ForeignKeyRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[TvpConstraintChild] ([ChildId], [ParentId])
    SELECT [ChildId], [ParentId] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_ConversionFailure]
    @Rows [chaos].[Tvp_ConversionRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[TvpConversionTarget] ([Id], [NumericValue])
    SELECT [Id], CONVERT(INT, [NumericText]) FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_LockTimeoutWriter]
    @Rows [chaos].[Tvp_LockRows] READONLY,
    @HoldMilliseconds INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE target
        SET [Value] = [Value] + rows.[Delta]
    FROM [chaos].[TvpLockTarget] AS target
    INNER JOIN @Rows AS rows ON rows.[Id] = target.[Id];
    DECLARE @SafeHoldMilliseconds INT = CASE WHEN @HoldMilliseconds BETWEEN 0 AND 59000 THEN @HoldMilliseconds ELSE 1000 END;
    DECLARE @Delay CHAR(12) = CONCAT('00:00:', RIGHT('0' + CAST(@SafeHoldMilliseconds / 1000 AS VARCHAR(2)), 2), '.', RIGHT('000' + CAST(@SafeHoldMilliseconds % 1000 AS VARCHAR(3)), 3));
    WAITFOR DELAY @Delay;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_LockTimeoutVictim]
    @Rows [chaos].[Tvp_LockRows] READONLY,
    @LockTimeoutMilliseconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SafeLockTimeoutMilliseconds INT = CASE WHEN @LockTimeoutMilliseconds >= -1 THEN @LockTimeoutMilliseconds ELSE 0 END;
    DECLARE @LockTimeoutSql NVARCHAR(MAX) = CONCAT(
        N'SET LOCK_TIMEOUT ', CONVERT(NVARCHAR(20), @SafeLockTimeoutMilliseconds), N';
UPDATE target
    SET [Value] = [Value] + rows.[Delta]
FROM [chaos].[TvpLockTarget] AS target
INNER JOIN @Rows AS rows ON rows.[Id] = target.[Id];');
    EXEC sys.sp_executesql
        @LockTimeoutSql,
        N'@Rows [chaos].[Tvp_LockRows] READONLY',
        @Rows = @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_Deadlock_Left]
    @Rows [chaos].[Tvp_DeadlockRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE a SET [Value] = [Value] + rows.[Delta] FROM [chaos].[TvpDeadlockA] AS a INNER JOIN @Rows AS rows ON rows.[Id] = a.[Id];
    WAITFOR DELAY '00:00:02';
    UPDATE b SET [Value] = [Value] + rows.[Delta] FROM [chaos].[TvpDeadlockB] AS b INNER JOIN @Rows AS rows ON rows.[Id] = b.[Id];
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_Deadlock_Right]
    @Rows [chaos].[Tvp_DeadlockRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE b SET [Value] = [Value] + rows.[Delta] FROM [chaos].[TvpDeadlockB] AS b INNER JOIN @Rows AS rows ON rows.[Id] = b.[Id];
    WAITFOR DELAY '00:00:02';
    UPDATE a SET [Value] = [Value] + rows.[Delta] FROM [chaos].[TvpDeadlockA] AS a INNER JOIN @Rows AS rows ON rows.[Id] = a.[Id];
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_DelayedInsert]
    @Rows [chaos].[Tvp_FaultRows] READONLY,
    @DelayMilliseconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SafeDelayMilliseconds INT = CASE WHEN @DelayMilliseconds BETWEEN 0 AND 59000 THEN @DelayMilliseconds ELSE 1000 END;
    DECLARE @Delay CHAR(12) = CONCAT('00:00:', RIGHT('0' + CAST(@SafeDelayMilliseconds / 1000 AS VARCHAR(2)), 2), '.', RIGHT('000' + CAST(@SafeDelayMilliseconds % 1000 AS VARCHAR(3)), 3));
    WAITFOR DELAY @Delay;
    EXEC [chaos].[usp_Tvp_InsertFaultRows] @Rows = @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_CancelProbe]
    @Rows [chaos].[Tvp_FaultRows] READONLY,
    @DelayMilliseconds INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[TvpCancellationLedger] ([ScenarioName], [RowsObserved], [DelayMilliseconds])
    VALUES (N'cancel-probe', (SELECT COUNT(*) FROM @Rows), @DelayMilliseconds);
    DECLARE @SafeDelayMilliseconds INT = CASE WHEN @DelayMilliseconds BETWEEN 0 AND 59000 THEN @DelayMilliseconds ELSE 1000 END;
    DECLARE @Delay CHAR(12) = CONCAT('00:00:', RIGHT('0' + CAST(@SafeDelayMilliseconds / 1000 AS VARCHAR(2)), 2), '.', RIGHT('000' + CAST(@SafeDelayMilliseconds % 1000 AS VARCHAR(3)), 3));
    WAITFOR DELAY @Delay;
    SELECT COUNT_BIG(*) AS [InputRows] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_AppLock_Insert]
    @Resource NVARCHAR(255),
    @Rows [chaos].[Tvp_FaultRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Result INT;
    BEGIN TRANSACTION;
    EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 1000;
    IF @Result < 0
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51220, N'TVP app lock insert failed to acquire lock.', 1;
    END;
    INSERT INTO [chaos].[TvpFaultAudit] ([FaultName], [RowsObserved]) VALUES (N'applock-insert', (SELECT COUNT(*) FROM @Rows));
    COMMIT TRANSACTION;
    SELECT @Result AS [AppLockResult];
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_PartialFailure_XactAbort]
    @Rows [chaos].[Tvp_ConversionRows] READONLY
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    INSERT INTO [chaos].[TvpConversionTarget] ([Id], [NumericValue])
    SELECT [Id], CONVERT(INT, [NumericText]) FROM @Rows;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_SavepointRollback]
    @Rows [chaos].[Tvp_FaultRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    SAVE TRANSACTION [BeforeTvpRows];
    INSERT INTO [chaos].[TvpRollbackTarget] ([Id], [Payload]) SELECT [Id], [Payload] FROM @Rows;
    ROLLBACK TRANSACTION [BeforeTvpRows];
    COMMIT TRANSACTION;
    SELECT COUNT_BIG(*) AS [RowsRolledBack] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_RetryableError]
    @Rows [chaos].[Tvp_FaultRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[TvpFaultAudit] ([FaultName], [RowsObserved]) VALUES (N'retryable-error', (SELECT COUNT(*) FROM @Rows));
    THROW 51230, N'Simulated retryable TVP error.', 1;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_PoisonEnqueue]
    @Rows [chaos].[Tvp_LobRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [chaos].[TvpPoisonQueue] ([Payload])
    SELECT [Payload] FROM @Rows;
    SELECT COUNT_BIG(*) AS [QueuedRows] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_PoisonDrain]
    @Take INT
AS
BEGIN
    SET NOCOUNT ON;
    WITH queue AS
    (
        SELECT TOP (@Take) *
        FROM [chaos].[TvpPoisonQueue]
        WHERE [Status] = N'Pending'
        ORDER BY [QueueId]
    )
    UPDATE queue
        SET [Status] = N'Processed';
    SELECT @@ROWCOUNT AS [ProcessedRows];
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_Lob_RaiseOnLarge]
    @Rows [chaos].[Tvp_LobRows] READONLY,
    @MaxBytes INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM @Rows WHERE DATALENGTH([Payload]) > @MaxBytes)
        THROW 51240, N'TVP LOB payload exceeded chaos threshold.', 1;
    SELECT COUNT_BIG(*) AS [AcceptedRows] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_ObserveLocks]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [CurrentDatabaseLocks]
    FROM sys.dm_tran_locks
    WHERE [resource_database_id] = DB_ID();
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_ObserveFaults]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (50) [AuditId], [FaultName], [RowsObserved], [CreatedAt]
    FROM [chaos].[TvpFaultAudit]
    ORDER BY [AuditId] DESC;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_ClearFaultData]
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [chaos].[TvpFaultAudit];
    DELETE FROM [chaos].[TvpFaultTarget];
    SELECT 1 AS [Cleared];
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_CheckRollbackEmpty]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [RollbackRows] FROM [chaos].[TvpRollbackTarget];
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_ThrowAfterResultset]
    @Rows [chaos].[Tvp_FaultRows] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [InputRows] FROM @Rows;
    THROW 51250, N'TVP resultset emitted before failure.', 1;
END;
GO

CREATE OR ALTER PROCEDURE [chaos].[usp_Tvp_ResetChaosMatrixData]
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [chaos].[TvpPoisonQueue];
    DELETE FROM [chaos].[TvpCancellationLedger];
    DELETE FROM [chaos].[TvpConversionTarget];
    DELETE FROM [chaos].[TvpConstraintChild];
    DELETE FROM [chaos].[TvpRollbackTarget];
    DELETE FROM [chaos].[TvpFaultAudit];
    DELETE FROM [chaos].[TvpFaultTarget];
    UPDATE [chaos].[TvpDeadlockA] SET [Value] = 100 WHERE [Id] = 1;
    UPDATE [chaos].[TvpDeadlockB] SET [Value] = 200 WHERE [Id] = 1;
    UPDATE [chaos].[TvpLockTarget] SET [Value] = 100 WHERE [Id] = 1;
    UPDATE [chaos].[TvpTimeoutTarget] SET [Value] = 100 WHERE [Id] = 1;
    SELECT 1 AS [ResetCompleted];
END;
GO

IF NOT EXISTS (SELECT 1 FROM [chaos].[TvpDeadlockA] WHERE [Id] = 1) INSERT INTO [chaos].[TvpDeadlockA] ([Id], [Value]) VALUES (1, 100);
IF NOT EXISTS (SELECT 1 FROM [chaos].[TvpDeadlockB] WHERE [Id] = 1) INSERT INTO [chaos].[TvpDeadlockB] ([Id], [Value]) VALUES (1, 200);
IF NOT EXISTS (SELECT 1 FROM [chaos].[TvpLockTarget] WHERE [Id] = 1) INSERT INTO [chaos].[TvpLockTarget] ([Id], [Value]) VALUES (1, 100);
IF NOT EXISTS (SELECT 1 FROM [chaos].[TvpTimeoutTarget] WHERE [Id] = 1) INSERT INTO [chaos].[TvpTimeoutTarget] ([Id], [Value]) VALUES (1, 100);
IF NOT EXISTS (SELECT 1 FROM [chaos].[TvpConstraintParent] WHERE [ParentId] = 1) INSERT INTO [chaos].[TvpConstraintParent] ([ParentId]) VALUES (1);
GO
