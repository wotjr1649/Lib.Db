-- ============================================================================
-- File: setup-libdb-stress-test.sql
-- Purpose: Isolated stress/load database for Lib.Db v2.3.0.
-- Target DB: LIBDB_STRESS_TEST
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i setup-libdb-stress-test.sql -f 65001
-- Notes:
--   - Creates repeatable high-volume tables and stored procedures.
--   - Keeps stress state separate from functional verification data.
-- ============================================================================

USE [master];
GO

IF DB_ID(N'LIBDB_STRESS_TEST') IS NULL
BEGIN
    CREATE DATABASE [LIBDB_STRESS_TEST];
END;
GO

ALTER DATABASE [LIBDB_STRESS_TEST] SET QUERY_STORE = ON;
GO

USE [LIBDB_STRESS_TEST];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'stress') EXEC(N'CREATE SCHEMA [stress]');
GO

IF OBJECT_ID(N'[stress].[Users]', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[Users]
    (
        [UserId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_stress_Users] PRIMARY KEY,
        [TenantId] INT NOT NULL,
        [UserName] NVARCHAR(100) NOT NULL,
        [Email] NVARCHAR(255) NOT NULL,
        [Bucket] INT NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_Users_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX [IX_stress_Users_Tenant_Bucket] ON [stress].[Users] ([TenantId], [Bucket]) INCLUDE ([Email]);
END;

IF OBJECT_ID(N'[stress].[Orders]', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[Orders]
    (
        [OrderId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_stress_Orders] PRIMARY KEY,
        [TenantId] INT NOT NULL,
        [UserId] BIGINT NOT NULL,
        [Sku] NVARCHAR(64) NOT NULL,
        [Qty] INT NOT NULL,
        [Price] DECIMAL(18,2) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_Orders_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX [IX_stress_Orders_Tenant_User] ON [stress].[Orders] ([TenantId], [UserId]);
END;

IF OBJECT_ID(N'[stress].[PoolProbe]', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[PoolProbe]
    (
        [ProbeId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_stress_PoolProbe] PRIMARY KEY,
        [WorkerName] NVARCHAR(100) NOT NULL,
        [StartedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_PoolProbe_StartedAt] DEFAULT SYSUTCDATETIME(),
        [CompletedAt] DATETIME2(7) NULL
    );
END;

IF OBJECT_ID(N'[stress].[LockA]', N'U') IS NULL
    CREATE TABLE [stress].[LockA] ([Id] INT NOT NULL CONSTRAINT [PK_stress_LockA] PRIMARY KEY, [Value] INT NOT NULL);

IF OBJECT_ID(N'[stress].[LockB]', N'U') IS NULL
    CREATE TABLE [stress].[LockB] ([Id] INT NOT NULL CONSTRAINT [PK_stress_LockB] PRIMARY KEY, [Value] INT NOT NULL);
GO

IF TYPE_ID(N'stress.Tvp_OrderLine') IS NULL
    CREATE TYPE [stress].[Tvp_OrderLine] AS TABLE
    (
        [UserId] BIGINT NOT NULL,
        [Sku] NVARCHAR(64) NOT NULL,
        [Qty] INT NOT NULL,
        [Price] DECIMAL(18,2) NOT NULL
    );
GO

CREATE OR ALTER PROCEDURE [stress].[usp_SeedUsers]
    @TenantId INT,
    @RowCount INT
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH n AS
    (
        SELECT TOP (@RowCount) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [rn]
        FROM sys.all_objects a CROSS JOIN sys.all_objects b
    )
    INSERT INTO [stress].[Users] ([TenantId], [UserName], [Email], [Bucket])
    SELECT @TenantId,
           CONCAT(N'StressUser-', @TenantId, N'-', [rn]),
           CONCAT(N'stress-', @TenantId, N'-', [rn], N'@example.test'),
           [rn] % 1024
    FROM n;
    SELECT @@ROWCOUNT AS [RowsInserted];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_ReadUsersPage]
    @TenantId INT,
    @Bucket INT,
    @Offset INT,
    @Fetch INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [UserId], [TenantId], [UserName], [Email], [Bucket]
    FROM [stress].[Users]
    WHERE [TenantId] = @TenantId AND [Bucket] = @Bucket
    ORDER BY [UserId]
    OFFSET @Offset ROWS FETCH NEXT @Fetch ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_InsertOrders]
    @TenantId INT,
    @Rows [stress].[Tvp_OrderLine] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[Orders] ([TenantId], [UserId], [Sku], [Qty], [Price])
    SELECT @TenantId, [UserId], [Sku], [Qty], [Price] FROM @Rows;
    SELECT @@ROWCOUNT AS [RowsInserted];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_MixedReadWrite]
    @TenantId INT,
    @Bucket INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserId BIGINT;
    SELECT TOP (1) @UserId = [UserId]
    FROM [stress].[Users]
    WHERE [TenantId] = @TenantId AND [Bucket] = @Bucket
    ORDER BY NEWID();

    IF @UserId IS NOT NULL
    BEGIN
        INSERT INTO [stress].[Orders] ([TenantId], [UserId], [Sku], [Qty], [Price])
        VALUES (@TenantId, @UserId, N'STRESS-SKU', 1, 10.00);
    END;

    SELECT COUNT_BIG(*) AS [OrderCount]
    FROM [stress].[Orders]
    WHERE [TenantId] = @TenantId AND [UserId] = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_PoolProbe]
    @WorkerName NVARCHAR(100),
    @DelayMilliseconds INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[PoolProbe] ([WorkerName]) VALUES (@WorkerName);
    DECLARE @ProbeId BIGINT = SCOPE_IDENTITY();
    IF @DelayMilliseconds > 0
    BEGIN
        DECLARE @Delay CHAR(12) = CONCAT('00:00:', RIGHT('0' + CAST(@DelayMilliseconds / 1000 AS VARCHAR(2)), 2), '.', RIGHT('000' + CAST(@DelayMilliseconds % 1000 AS VARCHAR(3)), 3));
        WAITFOR DELAY @Delay;
    END;
    UPDATE [stress].[PoolProbe] SET [CompletedAt] = SYSUTCDATETIME() WHERE [ProbeId] = @ProbeId;
    SELECT @ProbeId AS [ProbeId], @@SPID AS [Spid];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Deadlock_Left]
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE [stress].[LockA] SET [Value] = [Value] + 1 WHERE [Id] = 1;
    WAITFOR DELAY '00:00:02';
    UPDATE [stress].[LockB] SET [Value] = [Value] + 1 WHERE [Id] = 1;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Deadlock_Right]
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE [stress].[LockB] SET [Value] = [Value] + 1 WHERE [Id] = 1;
    WAITFOR DELAY '00:00:02';
    UPDATE [stress].[LockA] SET [Value] = [Value] + 1 WHERE [Id] = 1;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_ResetStressData]
AS
BEGIN
    SET NOCOUNT ON;
    TRUNCATE TABLE [stress].[Orders];
    TRUNCATE TABLE [stress].[PoolProbe];
    DELETE FROM [stress].[Users];
    DBCC CHECKIDENT ('[stress].[Users]', RESEED, 0) WITH NO_INFOMSGS;
END;
GO

IF NOT EXISTS (SELECT 1 FROM [stress].[LockA] WHERE [Id] = 1) INSERT INTO [stress].[LockA] ([Id], [Value]) VALUES (1, 100);
IF NOT EXISTS (SELECT 1 FROM [stress].[LockB] WHERE [Id] = 1) INSERT INTO [stress].[LockB] ([Id], [Value]) VALUES (1, 200);
GO

IF OBJECT_ID(N'stress.TvpLoadRuns', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpLoadRuns]
    (
        [RunId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_stress_TvpLoadRuns] PRIMARY KEY,
        [ScenarioName] NVARCHAR(128) NOT NULL,
        [WorkerCount] INT NOT NULL,
        [StartedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpLoadRuns_StartedAt] DEFAULT SYSUTCDATETIME(),
        [FinishedAt] DATETIME2(7) NULL
    );
END;
GO

IF OBJECT_ID(N'stress.TvpLoadEvents', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpLoadEvents]
    (
        [EventId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_stress_TvpLoadEvents] PRIMARY KEY,
        [RunId] BIGINT NULL,
        [EventName] NVARCHAR(128) NOT NULL,
        [RowCount] INT NOT NULL,
        [ElapsedMilliseconds] BIGINT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpLoadEvents_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeNarrow', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeNarrow]
    (
        [Id] INT NOT NULL,
        [Code] NVARCHAR(32) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeNarrow_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeMedium', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeMedium]
    (
        [Id] INT NOT NULL,
        [Sku] NVARCHAR(64) NOT NULL,
        [Qty] INT NOT NULL,
        [Price] DECIMAL(18,2) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeMedium_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeWide', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeWide]
    (
        [Id] INT NOT NULL,
        [Sku] NVARCHAR(64) NOT NULL,
        [Qty] INT NOT NULL,
        [Price] DECIMAL(18,2) NOT NULL,
        [Discount] DECIMAL(18,2) NOT NULL,
        [Tax] DECIMAL(18,2) NOT NULL,
        [LineTotal] DECIMAL(18,2) NOT NULL,
        [IsGift] BIT NOT NULL,
        [WarehouseId] INT NOT NULL,
        [Region] NVARCHAR(16) NOT NULL,
        [BatchId] UNIQUEIDENTIFIER NOT NULL,
        [RequestedAt] DATETIME2(7) NOT NULL,
        [SequenceNumber] BIGINT NOT NULL,
        [Priority] TINYINT NOT NULL,
        [Status] SMALLINT NOT NULL,
        [Note] NVARCHAR(128) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeWide_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeSparse', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeSparse]
    (
        [Id] INT NOT NULL,
        [OptionalText] NVARCHAR(200) NULL,
        [OptionalNumber] INT NULL,
        [OptionalAmount] DECIMAL(18,4) NULL,
        [OptionalAt] DATETIME2(7) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeSparse_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeLob', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeLob]
    (
        [Id] INT NOT NULL,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [Notes] VARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeLob_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeBinary', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeBinary]
    (
        [Id] INT NOT NULL,
        [Payload] VARBINARY(MAX) NOT NULL,
        [RowHash] BINARY(32) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeBinary_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeTemporal', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeTemporal]
    (
        [Id] INT NOT NULL,
        [CreatedAtValue] DATETIME2(7) NOT NULL,
        [EffectiveDate] DATE NOT NULL,
        [EffectiveTime] TIME(7) NOT NULL,
        [OffsetAt] DATETIMEOFFSET(7) NOT NULL,
        [InsertedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeTemporal_InsertedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeComposite', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeComposite]
    (
        [TenantId] INT NOT NULL,
        [EntityId] INT NOT NULL,
        [Revision] INT NOT NULL,
        [ValueText] NVARCHAR(128) NOT NULL,
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeComposite_UpdatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_stress_TvpShapeComposite] PRIMARY KEY ([TenantId], [EntityId], [Revision])
    );
END;
GO

IF OBJECT_ID(N'stress.TvpShapeJson', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpShapeJson]
    (
        [Id] INT NOT NULL,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [JsonPath] NVARCHAR(200) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpShapeJson_CreatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [CK_stress_TvpShapeJson_IsJson] CHECK (ISJSON([Payload]) = 1)
    );
END;
GO

IF OBJECT_ID(N'stress.TvpMultiHeader', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpMultiHeader]
    (
        [HeaderId] INT NOT NULL CONSTRAINT [PK_stress_TvpMultiHeader] PRIMARY KEY,
        [TenantId] INT NOT NULL,
        [Title] NVARCHAR(128) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpMultiHeader_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'stress.TvpMultiLine', N'U') IS NULL
BEGIN
    CREATE TABLE [stress].[TvpMultiLine]
    (
        [HeaderId] INT NOT NULL,
        [LineId] INT NOT NULL,
        [Sku] NVARCHAR(64) NOT NULL,
        [Qty] INT NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_stress_TvpMultiLine_CreatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_stress_TvpMultiLine] PRIMARY KEY ([HeaderId], [LineId])
    );
END;
GO

IF TYPE_ID(N'stress.Tvp_Narrow') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Narrow] AS TABLE ([Id] INT NOT NULL, [Code] NVARCHAR(32) NOT NULL);');
IF TYPE_ID(N'stress.Tvp_Medium') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Medium] AS TABLE ([Id] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL, [Price] DECIMAL(18,2) NOT NULL);');
IF TYPE_ID(N'stress.Tvp_Wide') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Wide] AS TABLE ([Id] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL, [Price] DECIMAL(18,2) NOT NULL, [Discount] DECIMAL(18,2) NOT NULL, [Tax] DECIMAL(18,2) NOT NULL, [LineTotal] DECIMAL(18,2) NOT NULL, [IsGift] BIT NOT NULL, [WarehouseId] INT NOT NULL, [Region] NVARCHAR(16) NOT NULL, [BatchId] UNIQUEIDENTIFIER NOT NULL, [RequestedAt] DATETIME2(7) NOT NULL, [SequenceNumber] BIGINT NOT NULL, [Priority] TINYINT NOT NULL, [Status] SMALLINT NOT NULL, [Note] NVARCHAR(128) NOT NULL);');
IF TYPE_ID(N'stress.Tvp_Sparse') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Sparse] AS TABLE ([Id] INT NOT NULL, [OptionalText] NVARCHAR(200) NULL, [OptionalNumber] INT NULL, [OptionalAmount] DECIMAL(18,4) NULL, [OptionalAt] DATETIME2(7) NULL);');
IF TYPE_ID(N'stress.Tvp_Lob') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Lob] AS TABLE ([Id] INT NOT NULL, [Payload] NVARCHAR(MAX) NOT NULL, [Notes] VARCHAR(MAX) NULL);');
IF TYPE_ID(N'stress.Tvp_Binary') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Binary] AS TABLE ([Id] INT NOT NULL, [Payload] VARBINARY(MAX) NOT NULL, [RowHash] BINARY(32) NULL);');
IF TYPE_ID(N'stress.Tvp_Temporal') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Temporal] AS TABLE ([Id] INT NOT NULL, [CreatedAtValue] DATETIME2(7) NOT NULL, [EffectiveDate] DATE NOT NULL, [EffectiveTime] TIME(7) NOT NULL, [OffsetAt] DATETIMEOFFSET(7) NOT NULL);');
IF TYPE_ID(N'stress.Tvp_Composite') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Composite] AS TABLE ([TenantId] INT NOT NULL, [EntityId] INT NOT NULL, [Revision] INT NOT NULL, [ValueText] NVARCHAR(128) NOT NULL, PRIMARY KEY ([TenantId], [EntityId], [Revision]));');
IF TYPE_ID(N'stress.Tvp_Json') IS NULL
    EXEC(N'CREATE TYPE [stress].[Tvp_Json] AS TABLE ([Id] INT NOT NULL, [Payload] NVARCHAR(MAX) NOT NULL, [JsonPath] NVARCHAR(200) NULL);');
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Narrow_Insert]
    @Rows [stress].[Tvp_Narrow] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeNarrow] ([Id], [Code])
    SELECT [Id], [Code] FROM @Rows;
    SELECT COUNT_BIG(*) AS [InsertedCount] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Medium_Insert]
    @Rows [stress].[Tvp_Medium] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeMedium] ([Id], [Sku], [Qty], [Price])
    SELECT [Id], [Sku], [Qty], [Price] FROM @Rows;
    SELECT COUNT_BIG(*) AS [InsertedCount] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Wide_Insert]
    @Rows [stress].[Tvp_Wide] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeWide] ([Id], [Sku], [Qty], [Price], [Discount], [Tax], [LineTotal], [IsGift], [WarehouseId], [Region], [BatchId], [RequestedAt], [SequenceNumber], [Priority], [Status], [Note])
    SELECT [Id], [Sku], [Qty], [Price], [Discount], [Tax], [LineTotal], [IsGift], [WarehouseId], [Region], [BatchId], [RequestedAt], [SequenceNumber], [Priority], [Status], [Note] FROM @Rows;
    SELECT COUNT_BIG(*) AS [InsertedCount] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Sparse_Insert]
    @Rows [stress].[Tvp_Sparse] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeSparse] ([Id], [OptionalText], [OptionalNumber], [OptionalAmount], [OptionalAt])
    SELECT [Id], [OptionalText], [OptionalNumber], [OptionalAmount], [OptionalAt] FROM @Rows;
    SELECT COUNT_BIG(*) AS [InsertedCount] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Lob_Insert]
    @Rows [stress].[Tvp_Lob] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeLob] ([Id], [Payload], [Notes])
    SELECT [Id], [Payload], [Notes] FROM @Rows;
    SELECT COUNT_BIG(*) AS [InsertedCount] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Binary_Insert]
    @Rows [stress].[Tvp_Binary] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeBinary] ([Id], [Payload], [RowHash])
    SELECT [Id], [Payload], [RowHash] FROM @Rows;
    SELECT COUNT_BIG(*) AS [InsertedCount] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Temporal_Insert]
    @Rows [stress].[Tvp_Temporal] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeTemporal] ([Id], [CreatedAtValue], [EffectiveDate], [EffectiveTime], [OffsetAt])
    SELECT [Id], [CreatedAtValue], [EffectiveDate], [EffectiveTime], [OffsetAt] FROM @Rows;
    SELECT COUNT_BIG(*) AS [InsertedCount] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Composite_Upsert]
    @Rows [stress].[Tvp_Composite] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE target
        SET [ValueText] = source.[ValueText], [UpdatedAt] = SYSUTCDATETIME()
    FROM [stress].[TvpShapeComposite] AS target
    INNER JOIN @Rows AS source
        ON source.[TenantId] = target.[TenantId]
       AND source.[EntityId] = target.[EntityId]
       AND source.[Revision] = target.[Revision];

    INSERT INTO [stress].[TvpShapeComposite] ([TenantId], [EntityId], [Revision], [ValueText])
    SELECT source.[TenantId], source.[EntityId], source.[Revision], source.[ValueText]
    FROM @Rows AS source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM [stress].[TvpShapeComposite] AS target
        WHERE target.[TenantId] = source.[TenantId]
          AND target.[EntityId] = source.[EntityId]
          AND target.[Revision] = source.[Revision]
    );

    SELECT COUNT_BIG(*) AS [AffectedInputRows] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Json_Insert]
    @Rows [stress].[Tvp_Json] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeJson] ([Id], [Payload], [JsonPath])
    SELECT [Id], [Payload], [JsonPath] FROM @Rows WHERE ISJSON([Payload]) = 1;
    SELECT COUNT_BIG(*) AS [AcceptedRows] FROM @Rows WHERE ISJSON([Payload]) = 1;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Multi_Insert]
    @Headers [stress].[Tvp_Composite] READONLY,
    @Lines [stress].[Tvp_Medium] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpMultiHeader] ([HeaderId], [TenantId], [Title])
    SELECT source.[EntityId], source.[TenantId], source.[ValueText]
    FROM @Headers AS source
    WHERE NOT EXISTS (SELECT 1 FROM [stress].[TvpMultiHeader] AS h WHERE h.[HeaderId] = source.[EntityId]);

    INSERT INTO [stress].[TvpMultiLine] ([HeaderId], [LineId], [Sku], [Qty])
    SELECT ISNULL((SELECT TOP (1) [EntityId] FROM @Headers ORDER BY [EntityId]), 0), [Id], [Sku], [Qty]
    FROM @Lines;

    SELECT (SELECT COUNT_BIG(*) FROM @Headers) AS [HeaderRows], (SELECT COUNT_BIG(*) FROM @Lines) AS [LineRows];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Narrow_CountOnly]
    @Rows [stress].[Tvp_Narrow] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [InputRows] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Narrow_WithOutput]
    @Rows [stress].[Tvp_Narrow] READONLY,
    @InputRows BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @InputRows = COUNT_BIG(*) FROM @Rows;
    SELECT @InputRows AS [InputRows];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Narrow_MultiResult]
    @Rows [stress].[Tvp_Narrow] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [InputRows] FROM @Rows;
    SELECT TOP (10) [Id], [Code] FROM @Rows ORDER BY [Id];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Narrow_ScalarAndTvp]
    @TenantId INT,
    @ScenarioName NVARCHAR(128),
    @Rows [stress].[Tvp_Narrow] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpLoadEvents] ([EventName], [RowCount])
    VALUES (CONCAT(@ScenarioName, N':tenant:', @TenantId), (SELECT COUNT(*) FROM @Rows));
    SELECT COUNT_BIG(*) AS [InputRows] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Narrow_ZeroRows]
    @Rows [stress].[Tvp_Narrow] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM @Rows)
        THROW 51110, N'Expected zero-row TVP.', 1;
    SELECT CAST(0 AS BIGINT) AS [InputRows];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Narrow_OptionalFilter]
    @CodePrefix NVARCHAR(32) = NULL,
    @Rows [stress].[Tvp_Narrow] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [MatchedRows]
    FROM @Rows
    WHERE @CodePrefix IS NULL OR [Code] LIKE @CodePrefix + N'%';
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Medium_MixedReadWrite]
    @TenantId INT,
    @Rows [stress].[Tvp_Medium] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpShapeMedium] ([Id], [Sku], [Qty], [Price])
    SELECT [Id], [Sku], [Qty], [Price] FROM @Rows;
    SELECT @TenantId AS [TenantId], COUNT_BIG(*) AS [RowsWritten] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Medium_Page]
    @Rows [stress].[Tvp_Medium] READONLY,
    @Offset INT,
    @Fetch INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Sku], [Qty], [Price]
    FROM @Rows
    ORDER BY [Id]
    OFFSET @Offset ROWS FETCH NEXT @Fetch ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Wide_JoinUsers]
    @TenantId INT,
    @Rows [stress].[Tvp_Wide] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [JoinedRows]
    FROM @Rows AS rows
    LEFT JOIN [stress].[Users] AS users
        ON users.[TenantId] = @TenantId
       AND users.[Bucket] = rows.[WarehouseId] % 16;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Lob_Checksum]
    @Rows [stress].[Tvp_Lob] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [InputRows], SUM(DATALENGTH([Payload])) AS [PayloadBytes] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Binary_Checksum]
    @Rows [stress].[Tvp_Binary] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT_BIG(*) AS [InputRows], SUM(DATALENGTH([Payload])) AS [PayloadBytes] FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Temporal_Window]
    @Rows [stress].[Tvp_Temporal] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], ROW_NUMBER() OVER (ORDER BY [CreatedAtValue], [Id]) AS [RowNumber]
    FROM @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Composite_MergeLikeUpdate]
    @Rows [stress].[Tvp_Composite] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    EXEC [stress].[usp_Tvp_Composite_Upsert] @Rows = @Rows;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Json_OpenJson]
    @Rows [stress].[Tvp_Json] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rows.[Id], JSON_VALUE(rows.[Payload], COALESCE(rows.[JsonPath], N'$.name')) AS [JsonValue]
    FROM @Rows AS rows
    WHERE ISJSON(rows.[Payload]) = 1;
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Load_Run_Start]
    @ScenarioName NVARCHAR(128),
    @WorkerCount INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [stress].[TvpLoadRuns] ([ScenarioName], [WorkerCount]) VALUES (@ScenarioName, @WorkerCount);
    SELECT CONVERT(BIGINT, SCOPE_IDENTITY()) AS [RunId];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_Load_Run_Finish]
    @RunId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [stress].[TvpLoadRuns] SET [FinishedAt] = SYSUTCDATETIME() WHERE [RunId] = @RunId;
    SELECT @@ROWCOUNT AS [RowsUpdated];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_QueryStore_Probe]
    @Rows [stress].[Tvp_Narrow] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Code], COUNT_BIG(*) AS [RowsPerCode]
    FROM @Rows
    GROUP BY [Code]
    ORDER BY [Code];
END;
GO

CREATE OR ALTER PROCEDURE [stress].[usp_Tvp_ResetMatrixData]
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [stress].[TvpMultiLine];
    DELETE FROM [stress].[TvpMultiHeader];
    DELETE FROM [stress].[TvpShapeJson];
    DELETE FROM [stress].[TvpShapeComposite];
    DELETE FROM [stress].[TvpShapeTemporal];
    DELETE FROM [stress].[TvpShapeBinary];
    DELETE FROM [stress].[TvpShapeLob];
    DELETE FROM [stress].[TvpShapeSparse];
    DELETE FROM [stress].[TvpShapeWide];
    DELETE FROM [stress].[TvpShapeMedium];
    DELETE FROM [stress].[TvpShapeNarrow];
    DELETE FROM [stress].[TvpLoadEvents];
    DELETE FROM [stress].[TvpLoadRuns];
    SELECT 1 AS [ResetCompleted];
END;
GO
