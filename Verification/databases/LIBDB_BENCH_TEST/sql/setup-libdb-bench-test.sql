-- ============================================================================
-- File: setup-libdb-bench-test.sql
-- Purpose: Dedicated BenchmarkDotNet SQL Server database for Lib.Db verification.
-- Target DB: LIBDB_BENCH_TEST
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i setup-libdb-bench-test.sql -f 65001
-- Notes:
--   - This script resets benchmark objects inside LIBDB_BENCH_TEST.
--   - The DB name contains BENCH to match the benchmark reset guard.
-- ============================================================================

USE [master];
GO

IF DB_ID(N'LIBDB_BENCH_TEST') IS NULL
BEGIN
    CREATE DATABASE [LIBDB_BENCH_TEST];
END;
GO

ALTER DATABASE [LIBDB_BENCH_TEST] SET QUERY_STORE = ON;
GO

USE [LIBDB_BENCH_TEST];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[libdb_bench_InsertWideOrderItems]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[libdb_bench_InsertWideOrderItems];

IF OBJECT_ID(N'[dbo].[libdb_bench_InsertMultiOrderGraph]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[libdb_bench_InsertMultiOrderGraph];

IF OBJECT_ID(N'[dbo].[libdb_bench_InsertOrderItems]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[libdb_bench_InsertOrderItems];

IF OBJECT_ID(N'[dbo].[libdb_bench_WideOrderItems]', N'U') IS NOT NULL
    DROP TABLE [dbo].[libdb_bench_WideOrderItems];

IF OBJECT_ID(N'[dbo].[libdb_bench_OrderItems]', N'U') IS NOT NULL
    DROP TABLE [dbo].[libdb_bench_OrderItems];

IF TYPE_ID(N'dbo.libdb_bench_WideOrderItem') IS NOT NULL
    DROP TYPE [dbo].[libdb_bench_WideOrderItem];

IF TYPE_ID(N'dbo.libdb_bench_OrderItem') IS NOT NULL
    DROP TYPE [dbo].[libdb_bench_OrderItem];
GO

CREATE TYPE [dbo].[libdb_bench_OrderItem] AS TABLE
(
    [Id] INT NOT NULL,
    [Sku] NVARCHAR(64) NOT NULL,
    [Qty] INT NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL
);
GO

CREATE TABLE [dbo].[libdb_bench_OrderItems]
(
    [OrderId] INT NOT NULL,
    [RequestedBy] NVARCHAR(64) NOT NULL,
    [Id] INT NOT NULL,
    [Sku] NVARCHAR(64) NOT NULL,
    [Qty] INT NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_OrderItems_CreatedAt] DEFAULT SYSUTCDATETIME()
);
GO

CREATE TYPE [dbo].[libdb_bench_WideOrderItem] AS TABLE
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
    [Priority] SMALLINT NOT NULL,
    [Status] TINYINT NOT NULL,
    [Note] NVARCHAR(128) NOT NULL
);
GO

CREATE TABLE [dbo].[libdb_bench_WideOrderItems]
(
    [OrderId] INT NOT NULL,
    [RequestedBy] NVARCHAR(64) NOT NULL,
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
    [Priority] SMALLINT NOT NULL,
    [Status] TINYINT NOT NULL,
    [Note] NVARCHAR(128) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_WideOrderItems_CreatedAt] DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID(N'[dbo].[libdb_bench_Runs]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[libdb_bench_Runs]
    (
        [RunId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_libdb_bench_Runs] PRIMARY KEY,
        [BenchmarkName] NVARCHAR(200) NOT NULL,
        [RuntimeVersion] NVARCHAR(100) NULL,
        [StartedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_Runs_StartedAt] DEFAULT SYSUTCDATETIME(),
        [Notes] NVARCHAR(1000) NULL
    );
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertOrderItems]
    @OrderId INT,
    @RequestedBy NVARCHAR(64),
    @Rows [dbo].[libdb_bench_OrderItem] READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[libdb_bench_OrderItems] ([OrderId], [RequestedBy], [Id], [Sku], [Qty], [Price])
    SELECT @OrderId, @RequestedBy, [Id], [Sku], [Qty], [Price]
    FROM @Rows;

    SELECT COUNT_BIG(*) AS [InsertedCount]
    FROM [dbo].[libdb_bench_OrderItems]
    WHERE [OrderId] = @OrderId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertWideOrderItems]
    @OrderId INT,
    @RequestedBy NVARCHAR(64),
    @Rows [dbo].[libdb_bench_WideOrderItem] READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[libdb_bench_WideOrderItems]
    (
        [OrderId], [RequestedBy], [Id], [Sku], [Qty], [Price], [Discount], [Tax],
        [LineTotal], [IsGift], [WarehouseId], [Region], [BatchId], [RequestedAt],
        [SequenceNumber], [Priority], [Status], [Note]
    )
    SELECT
        @OrderId, @RequestedBy, [Id], [Sku], [Qty], [Price], [Discount], [Tax],
        [LineTotal], [IsGift], [WarehouseId], [Region], [BatchId], [RequestedAt],
        [SequenceNumber], [Priority], [Status], [Note]
    FROM @Rows;

    SELECT COUNT_BIG(*) AS [InsertedCount]
    FROM [dbo].[libdb_bench_WideOrderItems]
    WHERE [OrderId] = @OrderId;
END;
GO

IF OBJECT_ID(N'dbo.libdb_bench_MediumItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_MediumItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL, [Price] DECIMAL(18,2) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_MediumItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_UltraWideItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_UltraWideItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL, [Price] DECIMAL(18,2) NOT NULL, [C01] NVARCHAR(64) NOT NULL, [C02] NVARCHAR(64) NOT NULL, [C03] NVARCHAR(64) NOT NULL, [C04] NVARCHAR(64) NOT NULL, [N01] INT NOT NULL, [N02] INT NOT NULL, [D01] DECIMAL(18,4) NOT NULL, [D02] DECIMAL(18,4) NOT NULL, [G01] UNIQUEIDENTIFIER NOT NULL, [T01] DATETIME2(7) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_UltraWideItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_NullableItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_NullableItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [Sku] NVARCHAR(64) NULL, [Qty] INT NULL, [Price] DECIMAL(18,2) NULL, [Note] NVARCHAR(200) NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_NullableItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_SparseItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_SparseItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [OptionalText] NVARCHAR(200) NULL, [OptionalNumber] INT NULL, [OptionalAmount] DECIMAL(18,4) NULL, [OptionalAt] DATETIME2(7) NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_SparseItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_LobItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_LobItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [Payload] NVARCHAR(MAX) NOT NULL, [Notes] VARCHAR(MAX) NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_LobItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_BinaryItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_BinaryItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [Payload] VARBINARY(MAX) NOT NULL, [RowHash] BINARY(32) NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_BinaryItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_DecimalItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_DecimalItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [Amount19_4] DECIMAL(19,4) NOT NULL, [Amount38_10] DECIMAL(38,10) NOT NULL, [Ratio] FLOAT NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_DecimalItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_TemporalItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_TemporalItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [CreatedAtValue] DATETIME2(7) NOT NULL, [EffectiveDate] DATE NOT NULL, [EffectiveTime] TIME(7) NOT NULL, [OffsetAt] DATETIMEOFFSET(7) NOT NULL, [InsertedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_TemporalItems_InsertedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_GuidItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_GuidItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [RowGuid] UNIQUEIDENTIFIER NOT NULL, [Code] NVARCHAR(64) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_GuidItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_JsonItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_JsonItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [Id] INT NOT NULL, [Payload] NVARCHAR(MAX) NOT NULL, [JsonPath] NVARCHAR(200) NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_JsonItems_CreatedAt] DEFAULT SYSUTCDATETIME(), CONSTRAINT [CK_libdb_bench_JsonItems_IsJson] CHECK (ISJSON([Payload]) = 1));
GO
IF OBJECT_ID(N'dbo.libdb_bench_CompositeItems', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_CompositeItems] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [TenantId] INT NOT NULL, [EntityId] INT NOT NULL, [Revision] INT NOT NULL, [ValueText] NVARCHAR(128) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_CompositeItems_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_MultiHeaders', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_MultiHeaders] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [HeaderId] INT NOT NULL, [TenantId] INT NOT NULL, [Title] NVARCHAR(128) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_MultiHeaders_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_MultiLines', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_MultiLines] ([OrderId] INT NOT NULL, [RequestedBy] NVARCHAR(64) NOT NULL, [HeaderId] INT NOT NULL, [LineId] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_MultiLines_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_MethodRuns', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_MethodRuns] ([RunId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_libdb_bench_MethodRuns] PRIMARY KEY, [MethodName] NVARCHAR(128) NOT NULL, [ShapeName] NVARCHAR(128) NOT NULL, [RowCount] INT NOT NULL, [StartedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_MethodRuns_StartedAt] DEFAULT SYSUTCDATETIME(), [FinishedAt] DATETIME2(7) NULL);
GO
IF OBJECT_ID(N'dbo.libdb_bench_MethodRunMetrics', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_MethodRunMetrics] ([MetricId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_libdb_bench_MethodRunMetrics] PRIMARY KEY, [RunId] BIGINT NOT NULL, [MetricName] NVARCHAR(128) NOT NULL, [MetricValue] DECIMAL(38,10) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_MethodRunMetrics_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_BulkCopyStage', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_BulkCopyStage] ([StageId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_libdb_bench_BulkCopyStage] PRIMARY KEY, [BatchId] UNIQUEIDENTIFIER NOT NULL, [Id] INT NOT NULL, [Payload] NVARCHAR(400) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_BulkCopyStage_CreatedAt] DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID(N'dbo.libdb_bench_DataReaderStage', N'U') IS NULL
    CREATE TABLE [dbo].[libdb_bench_DataReaderStage] ([StageId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_libdb_bench_DataReaderStage] PRIMARY KEY, [BatchId] UNIQUEIDENTIFIER NOT NULL, [Id] INT NOT NULL, [Payload] NVARCHAR(400) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_DataReaderStage_CreatedAt] DEFAULT SYSUTCDATETIME());
GO

IF TYPE_ID(N'dbo.libdb_bench_MediumOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_MediumOrderItem] AS TABLE ([Id] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL, [Price] DECIMAL(18,2) NOT NULL);');
IF TYPE_ID(N'dbo.libdb_bench_UltraWideOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_UltraWideOrderItem] AS TABLE ([Id] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL, [Price] DECIMAL(18,2) NOT NULL, [C01] NVARCHAR(64) NOT NULL, [C02] NVARCHAR(64) NOT NULL, [C03] NVARCHAR(64) NOT NULL, [C04] NVARCHAR(64) NOT NULL, [N01] INT NOT NULL, [N02] INT NOT NULL, [D01] DECIMAL(18,4) NOT NULL, [D02] DECIMAL(18,4) NOT NULL, [G01] UNIQUEIDENTIFIER NOT NULL, [T01] DATETIME2(7) NOT NULL);');
IF TYPE_ID(N'dbo.libdb_bench_NullableOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_NullableOrderItem] AS TABLE ([Id] INT NOT NULL, [Sku] NVARCHAR(64) NULL, [Qty] INT NULL, [Price] DECIMAL(18,2) NULL, [Note] NVARCHAR(200) NULL);');
IF TYPE_ID(N'dbo.libdb_bench_SparseOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_SparseOrderItem] AS TABLE ([Id] INT NOT NULL, [OptionalText] NVARCHAR(200) NULL, [OptionalNumber] INT NULL, [OptionalAmount] DECIMAL(18,4) NULL, [OptionalAt] DATETIME2(7) NULL);');
IF TYPE_ID(N'dbo.libdb_bench_LobOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_LobOrderItem] AS TABLE ([Id] INT NOT NULL, [Payload] NVARCHAR(MAX) NOT NULL, [Notes] VARCHAR(MAX) NULL);');
IF TYPE_ID(N'dbo.libdb_bench_BinaryOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_BinaryOrderItem] AS TABLE ([Id] INT NOT NULL, [Payload] VARBINARY(MAX) NOT NULL, [RowHash] BINARY(32) NULL);');
IF TYPE_ID(N'dbo.libdb_bench_DecimalOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_DecimalOrderItem] AS TABLE ([Id] INT NOT NULL, [Amount19_4] DECIMAL(19,4) NOT NULL, [Amount38_10] DECIMAL(38,10) NOT NULL, [Ratio] FLOAT NOT NULL);');
IF TYPE_ID(N'dbo.libdb_bench_TemporalOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_TemporalOrderItem] AS TABLE ([Id] INT NOT NULL, [CreatedAtValue] DATETIME2(7) NOT NULL, [EffectiveDate] DATE NOT NULL, [EffectiveTime] TIME(7) NOT NULL, [OffsetAt] DATETIMEOFFSET(7) NOT NULL);');
IF TYPE_ID(N'dbo.libdb_bench_GuidOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_GuidOrderItem] AS TABLE ([Id] INT NOT NULL, [RowGuid] UNIQUEIDENTIFIER NOT NULL, [Code] NVARCHAR(64) NOT NULL);');
IF TYPE_ID(N'dbo.libdb_bench_JsonOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_JsonOrderItem] AS TABLE ([Id] INT NOT NULL, [Payload] NVARCHAR(MAX) NOT NULL, [JsonPath] NVARCHAR(200) NULL);');
IF TYPE_ID(N'dbo.libdb_bench_CompositeOrderItem') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_CompositeOrderItem] AS TABLE ([TenantId] INT NOT NULL, [EntityId] INT NOT NULL, [Revision] INT NOT NULL, [ValueText] NVARCHAR(128) NOT NULL);');
IF TYPE_ID(N'dbo.libdb_bench_MultiOrderHeader') IS NULL
    EXEC(N'CREATE TYPE [dbo].[libdb_bench_MultiOrderHeader] AS TABLE ([HeaderId] INT NOT NULL, [TenantId] INT NOT NULL, [Title] NVARCHAR(128) NOT NULL);');
GO

CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertMediumOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_MediumOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_MediumItems] ([OrderId], [RequestedBy], [Id], [Sku], [Qty], [Price]) SELECT @OrderId, @RequestedBy, [Id], [Sku], [Qty], [Price] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_MediumItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertUltraWideOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_UltraWideOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_UltraWideItems] ([OrderId], [RequestedBy], [Id], [Sku], [Qty], [Price], [C01], [C02], [C03], [C04], [N01], [N02], [D01], [D02], [G01], [T01]) SELECT @OrderId, @RequestedBy, [Id], [Sku], [Qty], [Price], [C01], [C02], [C03], [C04], [N01], [N02], [D01], [D02], [G01], [T01] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_UltraWideItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertNullableOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_NullableOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_NullableItems] ([OrderId], [RequestedBy], [Id], [Sku], [Qty], [Price], [Note]) SELECT @OrderId, @RequestedBy, [Id], [Sku], [Qty], [Price], [Note] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_NullableItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertSparseOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_SparseOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_SparseItems] ([OrderId], [RequestedBy], [Id], [OptionalText], [OptionalNumber], [OptionalAmount], [OptionalAt]) SELECT @OrderId, @RequestedBy, [Id], [OptionalText], [OptionalNumber], [OptionalAmount], [OptionalAt] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_SparseItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertLobOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_LobOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_LobItems] ([OrderId], [RequestedBy], [Id], [Payload], [Notes]) SELECT @OrderId, @RequestedBy, [Id], [Payload], [Notes] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_LobItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertBinaryOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_BinaryOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_BinaryItems] ([OrderId], [RequestedBy], [Id], [Payload], [RowHash]) SELECT @OrderId, @RequestedBy, [Id], [Payload], [RowHash] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_BinaryItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertDecimalOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_DecimalOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_DecimalItems] ([OrderId], [RequestedBy], [Id], [Amount19_4], [Amount38_10], [Ratio]) SELECT @OrderId, @RequestedBy, [Id], [Amount19_4], [Amount38_10], [Ratio] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_DecimalItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertTemporalOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_TemporalOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_TemporalItems] ([OrderId], [RequestedBy], [Id], [CreatedAtValue], [EffectiveDate], [EffectiveTime], [OffsetAt]) SELECT @OrderId, @RequestedBy, [Id], [CreatedAtValue], [EffectiveDate], [EffectiveTime], [OffsetAt] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_TemporalItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertGuidOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_GuidOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_GuidItems] ([OrderId], [RequestedBy], [Id], [RowGuid], [Code]) SELECT @OrderId, @RequestedBy, [Id], [RowGuid], [Code] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_GuidItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertJsonOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_JsonOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_JsonItems] ([OrderId], [RequestedBy], [Id], [Payload], [JsonPath]) SELECT @OrderId, @RequestedBy, [Id], [Payload], [JsonPath] FROM @Rows WHERE ISJSON([Payload]) = 1; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_JsonItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertCompositeOrderItems] @OrderId INT, @RequestedBy NVARCHAR(64), @Rows [dbo].[libdb_bench_CompositeOrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_CompositeItems] ([OrderId], [RequestedBy], [TenantId], [EntityId], [Revision], [ValueText]) SELECT @OrderId, @RequestedBy, [TenantId], [EntityId], [Revision], [ValueText] FROM @Rows; SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_bench_CompositeItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertMultiOrderGraph] @OrderId INT, @RequestedBy NVARCHAR(64), @Headers [dbo].[libdb_bench_MultiOrderHeader] READONLY, @Rows [dbo].[libdb_bench_OrderItem] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_MultiHeaders] ([OrderId], [RequestedBy], [HeaderId], [TenantId], [Title]) SELECT @OrderId, @RequestedBy, [HeaderId], [TenantId], [Title] FROM @Headers; INSERT INTO [dbo].[libdb_bench_MultiLines] ([OrderId], [RequestedBy], [HeaderId], [LineId], [Sku], [Qty]) SELECT @OrderId, @RequestedBy, ISNULL((SELECT TOP (1) [HeaderId] FROM @Headers ORDER BY [HeaderId]), 0), [Id], [Sku], [Qty] FROM @Rows; SELECT (SELECT COUNT_BIG(*) FROM @Headers) AS [HeaderRows], (SELECT COUNT_BIG(*) FROM @Rows) AS [LineRows]; END;
GO

CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountMediumOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_MediumItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountUltraWideOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_UltraWideItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountNullableOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_NullableItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountSparseOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_SparseItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountLobOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_LobItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountBinaryOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_BinaryItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountDecimalOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_DecimalItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountTemporalOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_TemporalItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountGuidOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_GuidItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountJsonOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_JsonItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountCompositeOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT COUNT_BIG(*) AS [Rows] FROM [dbo].[libdb_bench_CompositeItems] WHERE [OrderId] = @OrderId; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_CountMultiOrderGraph] @OrderId INT AS BEGIN SET NOCOUNT ON; SELECT (SELECT COUNT_BIG(*) FROM [dbo].[libdb_bench_MultiHeaders] WHERE [OrderId] = @OrderId) AS [HeaderRows], (SELECT COUNT_BIG(*) FROM [dbo].[libdb_bench_MultiLines] WHERE [OrderId] = @OrderId) AS [LineRows]; END;
GO

CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearMediumOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_MediumItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearUltraWideOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_UltraWideItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearNullableOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_NullableItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearSparseOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_SparseItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearLobOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_LobItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearBinaryOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_BinaryItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearDecimalOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_DecimalItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearTemporalOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_TemporalItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearGuidOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_GuidItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearJsonOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_JsonItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearCompositeOrderItems] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_CompositeItems] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ClearMultiOrderGraph] @OrderId INT AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_MultiLines] WHERE [OrderId] = @OrderId; DELETE FROM [dbo].[libdb_bench_MultiHeaders] WHERE [OrderId] = @OrderId; SELECT @@ROWCOUNT AS [DeletedRows]; END;
GO

CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_StartMethodRun] @MethodName NVARCHAR(128), @ShapeName NVARCHAR(128), @RowCount INT AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_MethodRuns] ([MethodName], [ShapeName], [RowCount]) VALUES (@MethodName, @ShapeName, @RowCount); SELECT CONVERT(BIGINT, SCOPE_IDENTITY()) AS [RunId]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_FinishMethodRun] @RunId BIGINT AS BEGIN SET NOCOUNT ON; UPDATE [dbo].[libdb_bench_MethodRuns] SET [FinishedAt] = SYSUTCDATETIME() WHERE [RunId] = @RunId; SELECT @@ROWCOUNT AS [RowsUpdated]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_RecordMethodMetric] @RunId BIGINT, @MetricName NVARCHAR(128), @MetricValue DECIMAL(38,10) AS BEGIN SET NOCOUNT ON; INSERT INTO [dbo].[libdb_bench_MethodRunMetrics] ([RunId], [MetricName], [MetricValue]) VALUES (@RunId, @MetricName, @MetricValue); SELECT CONVERT(BIGINT, SCOPE_IDENTITY()) AS [MetricId]; END;
GO
CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_ResetBenchmarkMatrix] AS BEGIN SET NOCOUNT ON; DELETE FROM [dbo].[libdb_bench_MethodRunMetrics]; DELETE FROM [dbo].[libdb_bench_MethodRuns]; DELETE FROM [dbo].[libdb_bench_DataReaderStage]; DELETE FROM [dbo].[libdb_bench_BulkCopyStage]; DELETE FROM [dbo].[libdb_bench_MultiLines]; DELETE FROM [dbo].[libdb_bench_MultiHeaders]; DELETE FROM [dbo].[libdb_bench_CompositeItems]; DELETE FROM [dbo].[libdb_bench_JsonItems]; DELETE FROM [dbo].[libdb_bench_GuidItems]; DELETE FROM [dbo].[libdb_bench_TemporalItems]; DELETE FROM [dbo].[libdb_bench_DecimalItems]; DELETE FROM [dbo].[libdb_bench_BinaryItems]; DELETE FROM [dbo].[libdb_bench_LobItems]; DELETE FROM [dbo].[libdb_bench_SparseItems]; DELETE FROM [dbo].[libdb_bench_NullableItems]; DELETE FROM [dbo].[libdb_bench_UltraWideItems]; DELETE FROM [dbo].[libdb_bench_MediumItems]; SELECT 1 AS [ResetCompleted]; END;
GO
