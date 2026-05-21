-- ============================================================================
-- File: setup-libdb-verification-test.sql
-- Purpose: Functional integration database for Lib.Db v2.3.0.
-- Target DB: LIBDB_VERIFICATION_TEST
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i setup-libdb-verification-test.sql -f 65001
-- Notes:
--   - This script is idempotent and keeps existing data unless seed rows are missing.
--   - Stress, chaos, and BenchmarkDotNet fixtures live in separate DB scripts.
-- ============================================================================

USE [master];
GO

IF DB_ID(N'LIBDB_VERIFICATION_TEST') IS NULL
BEGIN
    CREATE DATABASE [LIBDB_VERIFICATION_TEST];
END;
GO

ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET QUERY_STORE = ON;
GO

USE [LIBDB_VERIFICATION_TEST];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'core') EXEC(N'CREATE SCHEMA [core]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'adv') EXEC(N'CREATE SCHEMA [adv]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'exception') EXEC(N'CREATE SCHEMA [exception]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'gap') EXEC(N'CREATE SCHEMA [gap]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'perf') EXEC(N'CREATE SCHEMA [perf]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'resilience') EXEC(N'CREATE SCHEMA [resilience]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'test') EXEC(N'CREATE SCHEMA [test]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'tvp') EXEC(N'CREATE SCHEMA [tvp]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'verify') EXEC(N'CREATE SCHEMA [verify]');
GO

IF OBJECT_ID(N'[core].[Users]', N'U') IS NULL
BEGIN
    CREATE TABLE [core].[Users]
    (
        [UserId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_core_Users] PRIMARY KEY,
        [UserName] NVARCHAR(100) NOT NULL,
        [Email] NVARCHAR(255) NOT NULL CONSTRAINT [UQ_core_Users_Email] UNIQUE,
        [Age] INT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_core_Users_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[core].[Products]', N'U') IS NULL
BEGIN
    CREATE TABLE [core].[Products]
    (
        [ProductId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_core_Products] PRIMARY KEY,
        [ProductName] NVARCHAR(200) NOT NULL,
        [Price] DECIMAL(18,2) NOT NULL,
        [Stock] INT NOT NULL CONSTRAINT [DF_core_Products_Stock] DEFAULT 0,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_core_Products_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[core].[Orders]', N'U') IS NULL
BEGIN
    CREATE TABLE [core].[Orders]
    (
        [OrderId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_core_Orders] PRIMARY KEY,
        [UserId] INT NOT NULL,
        [ProductId] INT NOT NULL CONSTRAINT [DF_core_Orders_ProductId] DEFAULT 1,
        [Quantity] INT NOT NULL CONSTRAINT [DF_core_Orders_Quantity] DEFAULT 1,
        [TotalPrice] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_core_Orders_TotalPrice] DEFAULT 0,
        [OrderDate] DATETIME2(7) NOT NULL CONSTRAINT [DF_core_Orders_OrderDate] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[core].[CursorState]', N'U') IS NULL
BEGIN
    CREATE TABLE [core].[CursorState]
    (
        [InstanceHash] VARCHAR(100) NOT NULL,
        [QueryKey] VARCHAR(100) NOT NULL,
        [CursorValue] NVARCHAR(MAX) NULL,
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_core_CursorState_UpdatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_core_CursorState] PRIMARY KEY ([InstanceHash], [QueryKey])
    );
END;

IF OBJECT_ID(N'[adv].[ResumableLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [adv].[ResumableLogs]
    (
        [LogId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_adv_ResumableLogs] PRIMARY KEY,
        [Message] NVARCHAR(4000) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_adv_ResumableLogs_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[exception].[ParentTable]', N'U') IS NULL
BEGIN
    CREATE TABLE [exception].[ParentTable]
    (
        [ParentId] INT NOT NULL CONSTRAINT [PK_exception_ParentTable] PRIMARY KEY,
        [ParentName] NVARCHAR(100) NOT NULL
    );
END;

IF OBJECT_ID(N'[exception].[ChildTable]', N'U') IS NULL
BEGIN
    CREATE TABLE [exception].[ChildTable]
    (
        [ChildId] INT NOT NULL CONSTRAINT [PK_exception_ChildTable] PRIMARY KEY,
        [ParentId] INT NOT NULL,
        [ChildName] NVARCHAR(100) NOT NULL,
        CONSTRAINT [FK_exception_ChildTable_ParentTable] FOREIGN KEY ([ParentId])
            REFERENCES [exception].[ParentTable]([ParentId])
    );
END;

IF OBJECT_ID(N'[exception].[UniqueTable]', N'U') IS NULL
BEGIN
    CREATE TABLE [exception].[UniqueTable]
    (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_exception_UniqueTable] PRIMARY KEY,
        [UniqueValue] NVARCHAR(100) NOT NULL CONSTRAINT [UQ_exception_UniqueTable_UniqueValue] UNIQUE,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_exception_UniqueTable_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[resilience].[RetryTest]', N'U') IS NULL
BEGIN
    CREATE TABLE [resilience].[RetryTest]
    (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_resilience_RetryTest] PRIMARY KEY,
        [AttemptNumber] INT NOT NULL,
        [SuccessFlag] BIT NOT NULL CONSTRAINT [DF_resilience_RetryTest_SuccessFlag] DEFAULT 0,
        [AttemptedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_resilience_RetryTest_AttemptedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[resilience].[TimeoutTest]', N'U') IS NULL
BEGIN
    CREATE TABLE [resilience].[TimeoutTest]
    (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_resilience_TimeoutTest] PRIMARY KEY,
        [DelaySeconds] INT NOT NULL,
        [CompletedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_resilience_TimeoutTest_CompletedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[test].[DeadlockA]', N'U') IS NULL
    CREATE TABLE [test].[DeadlockA] ([Id] INT NOT NULL CONSTRAINT [PK_test_DeadlockA] PRIMARY KEY, [Val] INT NOT NULL);

IF OBJECT_ID(N'[test].[DeadlockB]', N'U') IS NULL
    CREATE TABLE [test].[DeadlockB] ([Id] INT NOT NULL CONSTRAINT [PK_test_DeadlockB] PRIMARY KEY, [Val] INT NOT NULL);

IF OBJECT_ID(N'[tvp].[TypeTest]', N'U') IS NULL
BEGIN
    CREATE TABLE [tvp].[TypeTest]
    (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tvp_TypeTest] PRIMARY KEY,
        [DateOnlyValue] DATE NOT NULL,
        [TimeOnlyValue] TIME NOT NULL,
        [HalfValue] REAL NOT NULL,
        [GuidValue] UNIQUEIDENTIFIER NOT NULL,
        [DecimalValue] DECIMAL(18,4) NOT NULL,
        [NullableDateOnly] DATE NULL,
        [NullableTimeOnly] TIME NULL,
        [NullableHalf] REAL NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_tvp_TypeTest_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[gap].[BulkTarget]', N'U') IS NULL
BEGIN
    CREATE TABLE [gap].[BulkTarget]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_gap_BulkTarget] PRIMARY KEY,
        [Data] NVARCHAR(200) NOT NULL,
        [BatchId] INT NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_gap_BulkTarget_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[gap].[JsonData]', N'U') IS NULL
BEGIN
    CREATE TABLE [gap].[JsonData]
    (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_gap_JsonData] PRIMARY KEY,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_gap_JsonData_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[gap].[MergeTarget]', N'U') IS NULL
BEGIN
    CREATE TABLE [gap].[MergeTarget]
    (
        [Id] INT NOT NULL CONSTRAINT [PK_gap_MergeTarget] PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_gap_MergeTarget_UpdatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[perf].[BulkTest]', N'U') IS NULL
BEGIN
    CREATE TABLE [perf].[BulkTest]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_perf_BulkTest] PRIMARY KEY,
        [BatchNumber] INT NOT NULL,
        [Data] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2(7) NULL CONSTRAINT [DF_perf_BulkTest_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX [IX_perf_BulkTest_BatchNumber] ON [perf].[BulkTest] ([BatchNumber]);
END;

IF OBJECT_ID(N'[verify].[ResultMappingRows]', N'U') IS NULL
BEGIN
    CREATE TABLE [verify].[ResultMappingRows]
    (
        [CELL_NO] INT NOT NULL CONSTRAINT [PK_verify_ResultMappingRows] PRIMARY KEY,
        [SLOT_NAME] NVARCHAR(40) NOT NULL,
        [SCAN_DATE] DATE NOT NULL,
        [USER_ID] INT NOT NULL,
        [USER_NAME] NVARCHAR(100) NOT NULL,
        [EMAIL] NVARCHAR(255) NOT NULL,
        [AGE] INT NULL
    );
END;

IF OBJECT_ID(N'[verify].[QuotedIdentifierRows]', N'U') IS NULL
BEGIN
    CREATE TABLE [verify].[QuotedIdentifierRows]
    (
        [RowId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_verify_QuotedIdentifierRows] PRIMARY KEY,
        [RawCode] NVARCHAR(50) NOT NULL,
        [NormalizedCode] AS UPPER([RawCode]) PERSISTED
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[verify].[QuotedIdentifierRows]') AND name = N'IX_QuotedIdentifierRows_NormalizedCode')
BEGIN
    CREATE INDEX [IX_QuotedIdentifierRows_NormalizedCode] ON [verify].[QuotedIdentifierRows] ([NormalizedCode]);
END;

IF OBJECT_ID(N'[verify].[VerificationCustomerSegments]', N'U') IS NULL
BEGIN
    CREATE TABLE [verify].[VerificationCustomerSegments]
    (
        [TenantId] INT NOT NULL,
        [SegmentCode] NVARCHAR(20) NOT NULL,
        [SegmentName] NVARCHAR(100) NOT NULL,
        [Priority] INT NOT NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_verify_VerificationCustomerSegments_IsActive] DEFAULT 1,
        [ValidFrom] DATE NOT NULL,
        [ValidTo] DATE NULL,
        CONSTRAINT [PK_verify_VerificationCustomerSegments] PRIMARY KEY ([TenantId], [SegmentCode]),
        CONSTRAINT [CK_verify_VerificationCustomerSegments_Priority] CHECK ([Priority] BETWEEN 1 AND 10),
        CONSTRAINT [CK_verify_VerificationCustomerSegments_ValidRange] CHECK ([ValidTo] IS NULL OR [ValidTo] >= [ValidFrom])
    );
END;

IF OBJECT_ID(N'[verify].[VerificationOrderHeaders]', N'U') IS NULL
BEGIN
    CREATE TABLE [verify].[VerificationOrderHeaders]
    (
        [VerificationOrderId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_verify_VerificationOrderHeaders] PRIMARY KEY,
        [TenantId] INT NOT NULL,
        [ExternalOrderId] NVARCHAR(64) NOT NULL,
        [CustomerCode] NVARCHAR(40) NOT NULL,
        [SegmentCode] NVARCHAR(20) NOT NULL,
        [OrderStatus] CHAR(1) NOT NULL,
        [RequestedBy] NVARCHAR(128) NOT NULL,
        [CorrelationId] UNIQUEIDENTIFIER NOT NULL,
        [SubmittedAt] DATETIME2(7) NOT NULL,
        [ApprovedAt] DATETIME2(7) NULL,
        [TotalAmount] DECIMAL(19,4) NOT NULL,
        [TaxAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_verify_VerificationOrderHeaders_TaxAmount] DEFAULT 0,
        [NetAmount] AS ([TotalAmount] + [TaxAmount]) PERSISTED,
        [MetadataJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_verify_VerificationOrderHeaders_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_verify_VerificationOrderHeaders_UpdatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [UQ_verify_VerificationOrderHeaders_External] UNIQUE ([TenantId], [ExternalOrderId]),
        CONSTRAINT [FK_verify_VerificationOrderHeaders_Segment] FOREIGN KEY ([TenantId], [SegmentCode])
            REFERENCES [verify].[VerificationCustomerSegments] ([TenantId], [SegmentCode]),
        CONSTRAINT [CK_verify_VerificationOrderHeaders_Status] CHECK ([OrderStatus] IN ('N', 'A', 'H', 'C')),
        CONSTRAINT [CK_verify_VerificationOrderHeaders_Amount] CHECK ([TotalAmount] >= 0 AND [TaxAmount] >= 0),
        CONSTRAINT [CK_verify_VerificationOrderHeaders_MetadataJson] CHECK ([MetadataJson] IS NULL OR ISJSON([MetadataJson]) = 1)
    );
END;

IF OBJECT_ID(N'[verify].[VerificationOrderLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [verify].[VerificationOrderLines]
    (
        [VerificationOrderId] BIGINT NOT NULL,
        [LineNo] INT NOT NULL,
        [ProductCode] NVARCHAR(64) NOT NULL,
        [Quantity] INT NOT NULL,
        [UnitPrice] DECIMAL(19,4) NOT NULL,
        [DiscountAmount] DECIMAL(19,4) NOT NULL CONSTRAINT [DF_verify_VerificationOrderLines_DiscountAmount] DEFAULT 0,
        [TaxRate] DECIMAL(9,6) NOT NULL,
        [LineAmount] AS ((CONVERT(DECIMAL(19,4), [Quantity]) * [UnitPrice]) - [DiscountAmount]) PERSISTED,
        CONSTRAINT [PK_verify_VerificationOrderLines] PRIMARY KEY ([VerificationOrderId], [LineNo]),
        CONSTRAINT [FK_verify_VerificationOrderLines_Header] FOREIGN KEY ([VerificationOrderId])
            REFERENCES [verify].[VerificationOrderHeaders] ([VerificationOrderId]) ON DELETE CASCADE,
        CONSTRAINT [CK_verify_VerificationOrderLines_PositiveQuantity] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_verify_VerificationOrderLines_Amounts] CHECK ([UnitPrice] >= 0 AND [DiscountAmount] >= 0),
        CONSTRAINT [CK_verify_VerificationOrderLines_TaxRate] CHECK ([TaxRate] >= 0 AND [TaxRate] <= 1)
    );
END;

IF OBJECT_ID(N'[verify].[VerificationOrderAudit]', N'U') IS NULL
BEGIN
    CREATE TABLE [verify].[VerificationOrderAudit]
    (
        [AuditId] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_verify_VerificationOrderAudit] PRIMARY KEY,
        [VerificationOrderId] BIGINT NOT NULL,
        [EventKind] CHAR(1) NOT NULL,
        [CorrelationId] UNIQUEIDENTIFIER NOT NULL,
        [EventPayload] NVARCHAR(MAX) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_verify_VerificationOrderAudit_CreatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [FK_verify_VerificationOrderAudit_Header] FOREIGN KEY ([VerificationOrderId])
            REFERENCES [verify].[VerificationOrderHeaders] ([VerificationOrderId]) ON DELETE CASCADE,
        CONSTRAINT [CK_verify_VerificationOrderAudit_EventKind] CHECK ([EventKind] IN ('I', 'U', 'E')),
        CONSTRAINT [CK_verify_VerificationOrderAudit_EventPayloadJson] CHECK (ISJSON([EventPayload]) = 1)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[verify].[VerificationOrderHeaders]') AND name = N'IX_verify_VerificationOrderHeaders_OpenStatus')
BEGIN
    CREATE INDEX [IX_verify_VerificationOrderHeaders_OpenStatus]
        ON [verify].[VerificationOrderHeaders] ([TenantId], [OrderStatus], [SubmittedAt])
        INCLUDE ([ApprovedAt], [ExternalOrderId], [NetAmount])
        WHERE [ApprovedAt] IS NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[verify].[VerificationOrderHeaders]') AND name = N'IX_verify_VerificationOrderHeaders_Correlation')
BEGIN
    CREATE INDEX [IX_verify_VerificationOrderHeaders_Correlation]
        ON [verify].[VerificationOrderHeaders] ([CorrelationId])
        INCLUDE ([TenantId], [ExternalOrderId], [OrderStatus]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[verify].[VerificationOrderLines]') AND name = N'IX_verify_VerificationOrderLines_Product')
BEGIN
    CREATE INDEX [IX_verify_VerificationOrderLines_Product]
        ON [verify].[VerificationOrderLines] ([ProductCode], [VerificationOrderId])
        INCLUDE ([Quantity], [UnitPrice], [LineAmount]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[verify].[VerificationOrderAudit]') AND name = N'IX_verify_VerificationOrderAudit_Correlation')
BEGIN
    CREATE INDEX [IX_verify_VerificationOrderAudit_Correlation]
        ON [verify].[VerificationOrderAudit] ([CorrelationId], [CreatedAt]);
END;

IF OBJECT_ID(N'[dbo].[libdb_aot_OrderItems]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[libdb_aot_OrderItems]
    (
        [OrderId] INT NOT NULL,
        [RequestedBy] NVARCHAR(64) NOT NULL,
        [Id] INT NOT NULL,
        [Sku] NVARCHAR(64) NOT NULL,
        [Qty] INT NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_aot_OrderItems_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[dbo].[libdb_bench_OrderItems]', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'[dbo].[IF_CHUTE_INFO]', N'U') IS NULL
    CREATE TABLE [dbo].[IF_CHUTE_INFO] ([CHUTE_NO] INT NOT NULL CONSTRAINT [PK_IF_CHUTE_INFO] PRIMARY KEY, [CHUTE_NAME] NVARCHAR(100) NOT NULL, [STATUS] NVARCHAR(20) NOT NULL);

IF OBJECT_ID(N'[dbo].[IF_BRAND_MASTER]', N'U') IS NULL
    CREATE TABLE [dbo].[IF_BRAND_MASTER] ([BRAND_CD] NVARCHAR(20) NOT NULL CONSTRAINT [PK_IF_BRAND_MASTER] PRIMARY KEY, [BRAND_NM] NVARCHAR(100) NOT NULL);

IF OBJECT_ID(N'[dbo].[IF_BOX_LIST]', N'U') IS NULL
    CREATE TABLE [dbo].[IF_BOX_LIST] ([BOX_ID] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_IF_BOX_LIST] PRIMARY KEY, [BIZ_DAY] CHAR(8) NOT NULL, [BOX_NO] NVARCHAR(50) NOT NULL);

IF OBJECT_ID(N'[dbo].[USR_INFO]', N'U') IS NULL
    CREATE TABLE [dbo].[USR_INFO] ([USER_ID] NVARCHAR(50) NOT NULL CONSTRAINT [PK_USR_INFO] PRIMARY KEY, [USER_NM] NVARCHAR(100) NOT NULL);

IF OBJECT_ID(N'[dbo].[MENU_INFO]', N'U') IS NULL
    CREATE TABLE [dbo].[MENU_INFO] ([MENU_ID] INT NOT NULL CONSTRAINT [PK_MENU_INFO] PRIMARY KEY, [MENU_NM] NVARCHAR(100) NOT NULL);

IF OBJECT_ID(N'[dbo].[TS_TILT_LOG]', N'U') IS NULL
    CREATE TABLE [dbo].[TS_TILT_LOG] ([Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TS_TILT_LOG] PRIMARY KEY, [PLC_SEQ] INT NOT NULL, [CHUTE_NO] INT NOT NULL, [TRAY_NO] INT NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_TS_TILT_LOG_CreatedAt] DEFAULT SYSUTCDATETIME());

IF OBJECT_ID(N'[dbo].[TS_CHUTE_BTN_LOG]', N'U') IS NULL
    CREATE TABLE [dbo].[TS_CHUTE_BTN_LOG] ([Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TS_CHUTE_BTN_LOG] PRIMARY KEY, [CHUTE_NO] NVARCHAR(20) NOT NULL, [STATUS] NVARCHAR(20) NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_TS_CHUTE_BTN_LOG_CreatedAt] DEFAULT SYSUTCDATETIME());

IF OBJECT_ID(N'[dbo].[TS_EMR_LOG]', N'U') IS NULL
    CREATE TABLE [dbo].[TS_EMR_LOG] ([Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TS_EMR_LOG] PRIMARY KEY, [EMR_NO] INT NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_TS_EMR_LOG_CreatedAt] DEFAULT SYSUTCDATETIME());

IF OBJECT_ID(N'[dbo].[TS_ERROR_LOG]', N'U') IS NULL
    CREATE TABLE [dbo].[TS_ERROR_LOG] ([Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TS_ERROR_LOG] PRIMARY KEY, [CLASS] INT NOT NULL, [COMPUTER] NVARCHAR(100) NOT NULL, [EVENT_ID] INT NOT NULL, [MSG] NVARCHAR(4000) NOT NULL, [MUSTCON] NVARCHAR(10) NOT NULL, [STATE] INT NOT NULL, [SOURCE] NVARCHAR(100) NOT NULL, [PLCSEQ] BIGINT NOT NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_TS_ERROR_LOG_CreatedAt] DEFAULT SYSUTCDATETIME());

IF OBJECT_ID(N'[dbo].[TS_TRAY_FLOW]', N'U') IS NULL
    CREATE TABLE [dbo].[TS_TRAY_FLOW] ([Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TS_TRAY_FLOW] PRIMARY KEY, [EventName] NVARCHAR(50) NOT NULL, [Payload] NVARCHAR(4000) NULL, [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_TS_TRAY_FLOW_CreatedAt] DEFAULT SYSUTCDATETIME());
GO

IF TYPE_ID(N'core.Tvp_Core_User') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.table_types AS tt
    INNER JOIN sys.schemas AS s ON s.schema_id = tt.schema_id
    WHERE s.name = N'core'
      AND tt.name = N'Tvp_Core_User'
      AND (SELECT COUNT(*) FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id) = 3
      AND EXISTS (SELECT 1 FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id AND c.column_id = 1 AND c.name = N'UserName')
      AND EXISTS (SELECT 1 FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id AND c.column_id = 2 AND c.name = N'Email')
      AND EXISTS (SELECT 1 FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id AND c.column_id = 3 AND c.name = N'Age')
)
BEGIN
    DROP PROCEDURE IF EXISTS [core].[usp_Core_Bulk_Insert_Users];
    DROP TYPE [core].[Tvp_Core_User];
END
GO

IF TYPE_ID(N'core.Tvp_Core_User') IS NULL
    CREATE TYPE [core].[Tvp_Core_User] AS TABLE ([UserName] NVARCHAR(100) NOT NULL, [Email] NVARCHAR(255) NOT NULL, [Age] INT NULL);
GO

IF TYPE_ID(N'tvp.Tvp_Tvp_AllTypes') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.table_types AS tt
    INNER JOIN sys.schemas AS s ON s.schema_id = tt.schema_id
    WHERE s.name = N'tvp'
      AND tt.name = N'Tvp_Tvp_AllTypes'
      AND (SELECT COUNT(*) FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id) = 5
      AND EXISTS (SELECT 1 FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id AND c.column_id = 1 AND c.name = N'DateOnlyValue')
      AND EXISTS (SELECT 1 FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id AND c.column_id = 2 AND c.name = N'TimeOnlyValue')
      AND EXISTS (SELECT 1 FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id AND c.column_id = 3 AND c.name = N'HalfValue')
      AND EXISTS (SELECT 1 FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id AND c.column_id = 4 AND c.name = N'GuidValue')
      AND EXISTS (SELECT 1 FROM sys.columns AS c WHERE c.object_id = tt.type_table_object_id AND c.column_id = 5 AND c.name = N'DecimalValue')
)
BEGIN
    DROP PROCEDURE IF EXISTS [tvp].[usp_Tvp_Bulk_Insert_AllTypes];
    DROP TYPE [tvp].[Tvp_Tvp_AllTypes];
END
GO

IF TYPE_ID(N'tvp.Tvp_Tvp_AllTypes') IS NULL
    CREATE TYPE [tvp].[Tvp_Tvp_AllTypes] AS TABLE ([DateOnlyValue] DATE NOT NULL, [TimeOnlyValue] TIME NOT NULL, [HalfValue] REAL NOT NULL, [GuidValue] UNIQUEIDENTIFIER NOT NULL, [DecimalValue] DECIMAL(18,4) NOT NULL);
GO

IF TYPE_ID(N'tvp.Tvp_Tvp_Nullable') IS NULL
    CREATE TYPE [tvp].[Tvp_Tvp_Nullable] AS TABLE ([NullableDateOnly] DATE NULL, [NullableTimeOnly] TIME NULL, [NullableHalf] REAL NULL);
GO

IF TYPE_ID(N'tvp.Tvp_Tvp_SchemaMismatch') IS NULL
    CREATE TYPE [tvp].[Tvp_Tvp_SchemaMismatch] AS TABLE ([ColumnA] NVARCHAR(50) NULL, [ColumnB] INT NULL, [ColumnC] DATETIME2 NULL);
GO

IF TYPE_ID(N'gap.Tvp_BulkTarget') IS NULL
    CREATE TYPE [gap].[Tvp_BulkTarget] AS TABLE ([Data] NVARCHAR(200) NOT NULL, [BatchId] INT NOT NULL);
GO

IF TYPE_ID(N'dbo.libdb_aot_OrderItem') IS NULL
    CREATE TYPE [dbo].[libdb_aot_OrderItem] AS TABLE ([Id] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL);
GO

IF TYPE_ID(N'dbo.libdb_bench_OrderItem') IS NULL
    CREATE TYPE [dbo].[libdb_bench_OrderItem] AS TABLE ([Id] INT NOT NULL, [Sku] NVARCHAR(64) NOT NULL, [Qty] INT NOT NULL, [Price] DECIMAL(18,2) NOT NULL);
GO

IF TYPE_ID(N'dbo.T_StandardEvent') IS NULL
    CREATE TYPE [dbo].[T_StandardEvent] AS TABLE ([EventId] INT NULL, [CreatedAt] DATETIME2(3) NULL);
GO

IF TYPE_ID(N'dbo.T_PrecisionEvent') IS NULL
    CREATE TYPE [dbo].[T_PrecisionEvent] AS TABLE ([EventId] INT NULL, [CreatedAt] DATETIME2(7) NULL);
GO

IF TYPE_ID(N'tvp.TypeTest') IS NULL
    CREATE TYPE [tvp].[TypeTest] AS TABLE ([Id] INT NULL, [Name] NVARCHAR(50) NULL, [Value] DECIMAL(18,2) NULL);
GO

IF TYPE_ID(N'perf.Tvp_Perf_BulkInsert') IS NULL
    CREATE TYPE [perf].[Tvp_Perf_BulkInsert] AS TABLE ([BatchNumber] INT NOT NULL, [Data] NVARCHAR(500) NULL);
GO

IF TYPE_ID(N'verify.Tvp_VerificationOrderHeader') IS NULL
    CREATE TYPE [verify].[Tvp_VerificationOrderHeader] AS TABLE
    (
        [RowNo] INT NOT NULL,
        [ExternalOrderId] NVARCHAR(64) NOT NULL,
        [CustomerCode] NVARCHAR(40) NOT NULL,
        [SegmentCode] NVARCHAR(20) NOT NULL,
        [OrderStatus] CHAR(1) NOT NULL,
        [SubmittedAt] DATETIME2(7) NOT NULL,
        [TotalAmount] DECIMAL(19,4) NOT NULL,
        [MetadataJson] NVARCHAR(MAX) NULL,
        PRIMARY KEY CLUSTERED ([RowNo]),
        UNIQUE NONCLUSTERED ([ExternalOrderId]),
        CHECK ([OrderStatus] IN ('N', 'A', 'H', 'C')),
        CHECK ([TotalAmount] >= 0),
        CHECK ([MetadataJson] IS NULL OR ISJSON([MetadataJson]) = 1)
    );
GO

IF TYPE_ID(N'verify.Tvp_VerificationOrderLine') IS NULL
    CREATE TYPE [verify].[Tvp_VerificationOrderLine] AS TABLE
    (
        [RowNo] INT NOT NULL,
        [LineNo] INT NOT NULL,
        [ProductCode] NVARCHAR(64) NOT NULL,
        [Quantity] INT NOT NULL,
        [UnitPrice] DECIMAL(19,4) NOT NULL,
        [DiscountAmount] DECIMAL(19,4) NOT NULL,
        [TaxRate] DECIMAL(9,6) NOT NULL,
        PRIMARY KEY CLUSTERED ([RowNo], [LineNo]),
        CHECK ([Quantity] > 0),
        CHECK ([UnitPrice] >= 0 AND [DiscountAmount] >= 0),
        CHECK ([TaxRate] >= 0 AND [TaxRate] <= 1)
    );
GO

CREATE OR ALTER PROCEDURE [core].[usp_Core_Insert_User]
    @UserName NVARCHAR(100),
    @Email NVARCHAR(255),
    @Age INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [core].[Users] ([UserName], [Email], [Age]) VALUES (@UserName, @Email, @Age);
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS [NewUserId];
END;
GO

CREATE OR ALTER PROCEDURE [core].[usp_Core_Get_User]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [UserId], [UserName], [Email], [Age], [CreatedAt] FROM [core].[Users] WHERE [UserId] = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE [core].[usp_Core_Search_Users]
    @SearchTerm NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [UserId], [UserName], [Email], [Age], [CreatedAt]
    FROM [core].[Users]
    WHERE [UserName] LIKE N'%' + @SearchTerm + N'%' OR [Email] LIKE N'%' + @SearchTerm + N'%';
END;
GO

CREATE OR ALTER PROCEDURE [core].[usp_Core_Get_Dashboard]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [UserId], [UserName], [Email] FROM [core].[Users] WHERE [UserId] = @UserId;
    SELECT [OrderId], [ProductId], [Quantity], [TotalPrice], [OrderDate] FROM [core].[Orders] WHERE [UserId] = @UserId;
    SELECT COUNT(*) AS [TotalOrders], SUM([TotalPrice]) AS [TotalSpent] FROM [core].[Orders] WHERE [UserId] = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE [core].[usp_Core_Bulk_Insert_Users]
    @Users [core].[Tvp_Core_User] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [core].[Users] ([UserName], [Email], [Age]) SELECT [UserName], [Email], [Age] FROM @Users;
    SELECT @@ROWCOUNT AS [RowsAffected];
END;
GO

CREATE OR ALTER PROCEDURE [core].[usp_Core_Transaction_Test]
    @UserName NVARCHAR(100),
    @Email NVARCHAR(255),
    @ShouldRollback BIT = 0
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    SAVE TRANSACTION [SavePoint1];
    INSERT INTO [core].[Users] ([UserName], [Email]) VALUES (@UserName, @Email);
    IF @ShouldRollback = 1
    BEGIN
        ROLLBACK TRANSACTION [SavePoint1];
        COMMIT TRANSACTION;
        SELECT N'ROLLED_BACK_TO_SAVEPOINT' AS [Result];
        RETURN;
    END;
    COMMIT TRANSACTION;
    SELECT N'COMMITTED' AS [Result];
END;
GO

CREATE OR ALTER PROCEDURE [adv].[usp_Adv_OutputParameters]
    @InputVal INT,
    @OutputVal INT OUTPUT,
    @InOutVal INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @OutputVal = @InputVal * 2;
    SET @InOutVal = ISNULL(@InOutVal, 0) + @InputVal;
    SELECT @InputVal AS [InputVal], @OutputVal AS [OutputVal], @InOutVal AS [InOutVal];
END;
GO

CREATE OR ALTER PROCEDURE [adv].[usp_Adv_GenerateLogs]
    @Count INT
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH n AS
    (
        SELECT TOP (@Count) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [rn]
        FROM sys.all_objects
    )
    INSERT INTO [adv].[ResumableLogs] ([Message])
    SELECT CONCAT(N'Log ', [rn]) FROM n;
    SELECT @@ROWCOUNT AS [RowsAffected];
END;
GO

CREATE OR ALTER PROCEDURE [exception].[usp_Exception_ForeignKeyViolation]
    @NonExistentParentId INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [exception].[ChildTable] ([ChildId], [ParentId], [ChildName])
    VALUES (999, @NonExistentParentId, N'Test Child');
END;
GO

CREATE OR ALTER PROCEDURE [exception].[usp_Exception_UniqueViolation]
    @DuplicateValue NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [exception].[UniqueTable] ([UniqueValue]) VALUES (@DuplicateValue);
    INSERT INTO [exception].[UniqueTable] ([UniqueValue]) VALUES (@DuplicateValue);
END;
GO

CREATE OR ALTER PROCEDURE [exception].[usp_Exception_InvalidObjectName]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM [exception].[NonExistentTable];
END;
GO

CREATE OR ALTER PROCEDURE [exception].[usp_Exception_DivideByZero]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 10 / 0 AS [Result];
END;
GO

CREATE OR ALTER PROCEDURE [resilience].[usp_Resilience_Simulate_Delay]
    @DelaySeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @delay CHAR(8) = CONVERT(CHAR(8), DATEADD(SECOND, CASE WHEN @DelaySeconds BETWEEN 0 AND 59 THEN @DelaySeconds ELSE 1 END, 0), 108);
    WAITFOR DELAY @delay;
    INSERT INTO [resilience].[TimeoutTest] ([DelaySeconds]) VALUES (@DelaySeconds);
    SELECT @DelaySeconds AS [DelaySeconds], N'Completed' AS [Status];
END;
GO

CREATE OR ALTER PROCEDURE [resilience].[usp_Resilience_Simulate_Failure]
    @FailureProbability INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Random INT = ABS(CHECKSUM(NEWID())) % 100;
    IF @Random < @FailureProbability
        THROW 50060, N'Simulated transient failure', 1;
    INSERT INTO [resilience].[RetryTest] ([AttemptNumber], [SuccessFlag]) VALUES (1, 1);
    SELECT N'Success' AS [Status];
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Error_TryCatch_Rollback]
    @ShouldFail BIT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT INTO [core].[Users] ([UserName], [Email])
        VALUES (CONCAT(N'TxTest_', CONVERT(NVARCHAR(36), NEWID())), CONCAT(CONVERT(NVARCHAR(36), NEWID()), N'@tx.example'));
        IF @ShouldFail = 1
            THROW 50010, N'Intentional rollback test failure.', 1;
        COMMIT TRANSACTION;
        SELECT N'COMMITTED' AS [Result];
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Status_Branch_Logic]
    @UserId INT,
    @Status NVARCHAR(20) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @OrderCount INT;
    SELECT @OrderCount = COUNT(*) FROM [core].[Orders] WHERE [UserId] = @UserId;
    IF @OrderCount = 0 SET @Status = N'NEW';
    ELSE IF @OrderCount < 5 SET @Status = N'ACTIVE';
    ELSE SET @Status = N'VIP';
    SELECT @UserId AS [UserId], @Status AS [Status], @OrderCount AS [OrderCount];
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Composite_InsertAndValidate]
    @UserName NVARCHAR(100),
    @Email NVARCHAR(255),
    @NewUserId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Inserted TABLE ([NewUserId] INT);
    INSERT INTO [core].[Users] ([UserName], [Email])
    OUTPUT INSERTED.[UserId] INTO @Inserted
    VALUES (@UserName, @Email);
    SELECT @NewUserId = [NewUserId] FROM @Inserted;
    EXEC [core].[usp_Core_Get_User] @UserId = @NewUserId;
    IF @NewUserId IS NULL
        THROW 50020, N'User insert validation failed.', 1;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Error_Custom_50001]
    @OrderId INT,
    @Action NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Action = N'VALIDATE'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM [core].[Orders] WHERE [OrderId] = @OrderId)
            THROW 50001, N'Order not found.', 1;
        SELECT [OrderId], [UserId], [Quantity] FROM [core].[Orders] WHERE [OrderId] = @OrderId;
        RETURN;
    END;
    IF @Action = N'RETRY' THROW 50002, N'Retry limit exceeded.', 1;
    THROW 50003, N'Unknown action.', 1;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Error_NotNull_Violation]
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [core].[Users] ([UserName], [Email]) VALUES (NULL, N'not-null@example.test');
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Exception_QuerySyntax]
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(200) = N'SELECTX * FROM core.Users';
    EXEC sp_executesql @sql;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Deadlock_TableA]
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE [test].[DeadlockA] SET [Val] = [Val] + 1 WHERE [Id] = 1;
    WAITFOR DELAY '00:00:02';
    UPDATE [test].[DeadlockB] SET [Val] = [Val] + 1 WHERE [Id] = 1;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Deadlock_TableB]
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    UPDATE [test].[DeadlockB] SET [Val] = [Val] + 1 WHERE [Id] = 1;
    WAITFOR DELAY '00:00:02';
    UPDATE [test].[DeadlockA] SET [Val] = [Val] + 1 WHERE [Id] = 1;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Simulate_TransactionAborted]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    INSERT INTO [core].[Users] ([UserName], [Email]) VALUES (NULL, N'txabort@example.test');
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_RaiseError_Unknown_999]
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR(N'Unmapped error code 999 test.', 16, 1);
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Savepoint_PartialCommit]
    @EmailA NVARCHAR(255),
    @EmailB NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    INSERT INTO [core].[Users] ([UserName], [Email], [Age]) VALUES (N'SavepointA', @EmailA, 10);
    SAVE TRANSACTION [SP_AfterA];
    INSERT INTO [core].[Users] ([UserName], [Email], [Age]) VALUES (N'SavepointB', @EmailB, 20);
    ROLLBACK TRANSACTION [SP_AfterA];
    COMMIT TRANSACTION;
    SELECT N'PARTIAL_COMMIT' AS [Result];
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Composite_V2]
    @UserName NVARCHAR(100),
    @Email NVARCHAR(255),
    @NewUserId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @InsertedIds TABLE ([UserId] INT);
    INSERT INTO [core].[Users] ([UserName], [Email])
    OUTPUT INSERTED.[UserId] INTO @InsertedIds
    VALUES (@UserName, @Email);
    SELECT @NewUserId = [UserId] FROM @InsertedIds;
    SELECT [UserId], [UserName], [Email], [Age], [CreatedAt]
    FROM [core].[Users]
    WHERE [UserId] = @NewUserId;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Output_With_Error]
    @InputId INT,
    @OutputName NVARCHAR(100) OUTPUT,
    @OutputAge INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @OutputName = [UserName], @OutputAge = [Age] FROM [core].[Users] WHERE [UserId] = @InputId;
    IF @OutputName IS NULL THROW 50030, N'User not found.', 1;
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Core_Get_NullScalar]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT NULL AS [NullValue];
END;
GO

CREATE OR ALTER PROCEDURE [test].[usp_Core_Get_Empty]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [UserId], [UserName], [Email] FROM [core].[Users] WHERE 1 = 0;
END;
GO

CREATE OR ALTER PROCEDURE [tvp].[usp_Tvp_Bulk_Insert_AllTypes]
    @Items [tvp].[Tvp_Tvp_AllTypes] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [tvp].[TypeTest] ([DateOnlyValue], [TimeOnlyValue], [HalfValue], [GuidValue], [DecimalValue])
    SELECT [DateOnlyValue], [TimeOnlyValue], [HalfValue], [GuidValue], [DecimalValue] FROM @Items;
    SELECT @@ROWCOUNT AS [RowsAffected];
END;
GO

CREATE OR ALTER PROCEDURE [tvp].[usp_Tvp_Get_AllTypes]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [DateOnlyValue], [TimeOnlyValue], [HalfValue], [GuidValue], [DecimalValue],
           [NullableDateOnly], [NullableTimeOnly], [NullableHalf], [CreatedAt]
    FROM [tvp].[TypeTest];
END;
GO

CREATE OR ALTER PROCEDURE [tvp].[usp_Tvp_Test_Schema_Mismatch]
    @Items [tvp].[Tvp_Tvp_SchemaMismatch] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [ColumnA], [ColumnB], [ColumnC] FROM @Items;
END;
GO

CREATE OR ALTER PROCEDURE [gap].[usp_BulkInsert_Tvp]
    @Items [gap].[Tvp_BulkTarget] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [gap].[BulkTarget] ([Data], [BatchId]) SELECT [Data], [BatchId] FROM @Items;
    SELECT @@ROWCOUNT AS [RowsAffected];
END;
GO

CREATE OR ALTER PROCEDURE [gap].[usp_IsolationLevel_ReadUncommitted]
    @TargetId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
    SELECT [UserId], [UserName], [Email]
    FROM [core].[Users] WITH (NOLOCK)
    WHERE [UserId] = @TargetId;
END;
GO

CREATE OR ALTER PROCEDURE [gap].[usp_IsolationLevel_Serializable]
    @TargetId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;
    SELECT [UserId], [UserName], [Email]
    FROM [core].[Users]
    WHERE [UserId] = @TargetId;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [gap].[usp_Json_Insert]
    @JsonPayload NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [gap].[JsonData] ([Payload]) VALUES (@JsonPayload);
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS [NewId];
END;
GO

CREATE OR ALTER PROCEDURE [gap].[usp_Json_Query]
    @Key NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], JSON_VALUE([Payload], CONCAT(N'$.', @Key)) AS [ExtractedValue], [Payload]
    FROM [gap].[JsonData]
    WHERE ISJSON([Payload]) = 1;
END;
GO

CREATE OR ALTER PROCEDURE [gap].[usp_Merge_Upsert]
    @Id INT,
    @Name NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ActionTable TABLE ([MergeAction] NVARCHAR(10));
    MERGE [gap].[MergeTarget] AS target
    USING (SELECT @Id AS [Id], @Name AS [Name]) AS source
    ON target.[Id] = source.[Id]
    WHEN MATCHED THEN UPDATE SET [Name] = source.[Name], [UpdatedAt] = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT ([Id], [Name]) VALUES (source.[Id], source.[Name])
    OUTPUT $action INTO @ActionTable;
    SELECT [MergeAction] FROM @ActionTable;
END;
GO

CREATE OR ALTER PROCEDURE [gap].[usp_Paginate]
    @PageNum INT,
    @PageSize INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [UserId], [UserName], [Email], [Age]
    FROM [core].[Users]
    ORDER BY [UserId]
    OFFSET ((@PageNum - 1) * @PageSize) ROWS FETCH NEXT @PageSize ROWS ONLY;
    SELECT COUNT(*) AS [TotalCount] FROM [core].[Users];
END;
GO

CREATE OR ALTER PROCEDURE [gap].[usp_WindowFunction_RankUsers]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [UserId], [UserName], [Email], [Age],
           ROW_NUMBER() OVER (ORDER BY [UserId]) AS [RowNum],
           RANK() OVER (ORDER BY ISNULL([Age], 0) DESC) AS [AgeRank],
           DENSE_RANK() OVER (ORDER BY ISNULL([Age], 0) DESC) AS [DenseAgeRank],
           COUNT(*) OVER () AS [TotalUsers]
    FROM [core].[Users]
    ORDER BY [UserId];
END;
GO

CREATE OR ALTER PROCEDURE [perf].[usp_Perf_Bulk_Insert]
    @Items [perf].[Tvp_Perf_BulkInsert] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [perf].[BulkTest] ([BatchNumber], [Data])
    SELECT [BatchNumber], [Data] FROM @Items;
    SELECT @@ROWCOUNT AS [RowsAffected];
END;
GO

CREATE OR ALTER PROCEDURE [perf].[usp_Perf_Query_With_Param]
    @BatchNumber INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [BatchNumber], [Data], [CreatedAt]
    FROM [perf].[BulkTest]
    WHERE [BatchNumber] = @BatchNumber;
END;
GO

CREATE OR ALTER PROCEDURE [verify].[usp_GetSuspendRows]
    @ScanDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [CELL_NO], [SLOT_NAME] FROM [verify].[ResultMappingRows] WHERE [SCAN_DATE] = @ScanDate ORDER BY [CELL_NO];
END;
GO

CREATE OR ALTER PROCEDURE [verify].[usp_GetGeneratedRows]
    @ScanDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [USER_ID] AS [UserId], [USER_NAME] AS [UserName], [EMAIL] AS [Email], [AGE] AS [Age]
    FROM [verify].[ResultMappingRows]
    WHERE [SCAN_DATE] = @ScanDate
    ORDER BY [USER_ID];
END;
GO

CREATE OR ALTER PROCEDURE [verify].[usp_Verification_UpsertOrders]
    @TenantId INT,
    @RequestedBy NVARCHAR(128),
    @CorrelationId UNIQUEIDENTIFIER,
    @Headers [verify].[Tvp_VerificationOrderHeader] READONLY,
    @Lines [verify].[Tvp_VerificationOrderLine] READONLY,
    @InsertedOrders INT = NULL OUTPUT,
    @InsertedLines INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OrderMap TABLE
    (
        [RowNo] INT NOT NULL PRIMARY KEY,
        [VerificationOrderId] BIGINT NOT NULL
    );

    BEGIN TRANSACTION;

    MERGE [verify].[VerificationCustomerSegments] AS target
    USING
    (
        SELECT DISTINCT @TenantId AS [TenantId], [SegmentCode]
        FROM @Headers
    ) AS source
    ON target.[TenantId] = source.[TenantId] AND target.[SegmentCode] = source.[SegmentCode]
    WHEN NOT MATCHED THEN
        INSERT ([TenantId], [SegmentCode], [SegmentName], [Priority], [ValidFrom])
        VALUES (source.[TenantId], source.[SegmentCode], CONCAT(N'Segment ', source.[SegmentCode]), 5, CONVERT(DATE, SYSUTCDATETIME()));

    UPDATE target
       SET [CustomerCode] = source.[CustomerCode],
           [SegmentCode] = source.[SegmentCode],
           [OrderStatus] = source.[OrderStatus],
           [RequestedBy] = @RequestedBy,
           [CorrelationId] = @CorrelationId,
           [SubmittedAt] = source.[SubmittedAt],
           [TotalAmount] = source.[TotalAmount],
           [TaxAmount] = 0,
           [MetadataJson] = source.[MetadataJson],
           [UpdatedAt] = SYSUTCDATETIME()
    FROM [verify].[VerificationOrderHeaders] AS target
    JOIN @Headers AS source
      ON target.[TenantId] = @TenantId
     AND target.[ExternalOrderId] = source.[ExternalOrderId];

    INSERT INTO [verify].[VerificationOrderHeaders]
    (
        [TenantId], [ExternalOrderId], [CustomerCode], [SegmentCode], [OrderStatus],
        [RequestedBy], [CorrelationId], [SubmittedAt], [TotalAmount], [TaxAmount], [MetadataJson]
    )
    SELECT
        @TenantId, source.[ExternalOrderId], source.[CustomerCode], source.[SegmentCode], source.[OrderStatus],
        @RequestedBy, @CorrelationId, source.[SubmittedAt], source.[TotalAmount], 0, source.[MetadataJson]
    FROM @Headers AS source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM [verify].[VerificationOrderHeaders] AS target
        WHERE target.[TenantId] = @TenantId
          AND target.[ExternalOrderId] = source.[ExternalOrderId]
    );

    INSERT INTO @OrderMap ([RowNo], [VerificationOrderId])
    SELECT source.[RowNo], target.[VerificationOrderId]
    FROM @Headers AS source
    JOIN [verify].[VerificationOrderHeaders] AS target
      ON target.[TenantId] = @TenantId
     AND target.[ExternalOrderId] = source.[ExternalOrderId];

    DELETE lineTarget
    FROM [verify].[VerificationOrderLines] AS lineTarget
    JOIN @OrderMap AS map
      ON map.[VerificationOrderId] = lineTarget.[VerificationOrderId];

    INSERT INTO [verify].[VerificationOrderLines]
    (
        [VerificationOrderId], [LineNo], [ProductCode], [Quantity], [UnitPrice], [DiscountAmount], [TaxRate]
    )
    SELECT
        map.[VerificationOrderId], source.[LineNo], source.[ProductCode], source.[Quantity],
        source.[UnitPrice], source.[DiscountAmount], source.[TaxRate]
    FROM @Lines AS source
    JOIN @OrderMap AS map
      ON map.[RowNo] = source.[RowNo];

    SET @InsertedLines = @@ROWCOUNT;
    SET @InsertedOrders = (SELECT COUNT(*) FROM @OrderMap);

    INSERT INTO [verify].[VerificationOrderAudit] ([VerificationOrderId], [EventKind], [CorrelationId], [EventPayload])
    SELECT
        map.[VerificationOrderId],
        N'I',
        @CorrelationId,
        CONCAT(N'{"externalOrderId":"', STRING_ESCAPE(headerSource.[ExternalOrderId], 'json'), N'","lineCount":', COUNT(lineSource.[LineNo]), N'}')
    FROM @OrderMap AS map
    JOIN @Headers AS headerSource
      ON headerSource.[RowNo] = map.[RowNo]
    LEFT JOIN @Lines AS lineSource
      ON lineSource.[RowNo] = map.[RowNo]
    GROUP BY map.[VerificationOrderId], headerSource.[ExternalOrderId];

    COMMIT TRANSACTION;

    SELECT
        @InsertedOrders AS [InsertedOrders],
        @InsertedLines AS [InsertedLines],
        (
            SELECT COUNT(*)
            FROM [verify].[VerificationOrderHeaders]
            WHERE [TenantId] = @TenantId AND [ApprovedAt] IS NULL
        ) AS [OpenOrderCount];
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

CREATE OR ALTER PROCEDURE [dbo].[libdb_aot_InsertOrderItems]
    @OrderId INT,
    @RequestedBy NVARCHAR(64),
    @Rows [dbo].[libdb_aot_OrderItem] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[libdb_aot_OrderItems] ([OrderId], [RequestedBy], [Id], [Sku], [Qty])
    SELECT @OrderId, @RequestedBy, [Id], [Sku], [Qty] FROM @Rows;
    SELECT COUNT_BIG(*) AS [InsertedCount] FROM [dbo].[libdb_aot_OrderItems] WHERE [OrderId] = @OrderId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[usp_Test_Reset_All_Data]
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [core].[Orders];
    DELETE FROM [core].[Users];
    DELETE FROM [core].[Products];
    DELETE FROM [adv].[ResumableLogs];
    DELETE FROM [exception].[ChildTable];
    DELETE FROM [exception].[ParentTable];
    DELETE FROM [exception].[UniqueTable];
    IF OBJECT_ID(N'[dbo].[libdb_aot_OrderItems]', N'U') IS NOT NULL DELETE FROM [dbo].[libdb_aot_OrderItems];
    IF OBJECT_ID(N'[dbo].[libdb_bench_OrderItems]', N'U') IS NOT NULL DELETE FROM [dbo].[libdb_bench_OrderItems];
    DELETE FROM [perf].[BulkTest];
    DELETE FROM [resilience].[RetryTest];
    DELETE FROM [resilience].[TimeoutTest];
    IF OBJECT_ID(N'[gap].[BulkTarget]', N'U') IS NOT NULL DELETE FROM [gap].[BulkTarget];
    IF OBJECT_ID(N'[gap].[JsonData]', N'U') IS NOT NULL DELETE FROM [gap].[JsonData];
    IF OBJECT_ID(N'[gap].[MergeTarget]', N'U') IS NOT NULL DELETE FROM [gap].[MergeTarget];
    IF OBJECT_ID(N'[verify].[VerificationOrderAudit]', N'U') IS NOT NULL DELETE FROM [verify].[VerificationOrderAudit];
    IF OBJECT_ID(N'[verify].[VerificationOrderLines]', N'U') IS NOT NULL DELETE FROM [verify].[VerificationOrderLines];
    IF OBJECT_ID(N'[verify].[VerificationOrderHeaders]', N'U') IS NOT NULL DELETE FROM [verify].[VerificationOrderHeaders];
    IF OBJECT_ID(N'[verify].[VerificationCustomerSegments]', N'U') IS NOT NULL DELETE FROM [verify].[VerificationCustomerSegments];
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[IF_SP_TILT_LOG]
    @V_PLC_SEQ INT,
    @V_CHUTE_NO INT,
    @V_TRAY_NO INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[TS_TILT_LOG] ([PLC_SEQ], [CHUTE_NO], [TRAY_NO])
    VALUES (@V_PLC_SEQ, @V_CHUTE_NO, @V_TRAY_NO);
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[IF_SP_CHUTE_BTN_LOG]
    @V_CHUTE_NO NVARCHAR(20),
    @V_STATUS NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[TS_CHUTE_BTN_LOG] ([CHUTE_NO], [STATUS])
    VALUES (@V_CHUTE_NO, @V_STATUS);
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[IF_SP_EMR_LOG]
    @V_EMR_NO INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[TS_EMR_LOG] ([EMR_NO]) VALUES (@V_EMR_NO);
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[IF_SP_ERROR_LOG]
    @V_CLASS INT,
    @V_COMPUTER NVARCHAR(100),
    @V_EVENT_ID INT,
    @V_MSG NVARCHAR(4000),
    @V_MUSTCON NVARCHAR(10),
    @V_STATE INT,
    @V_SOURCE NVARCHAR(100),
    @V_PLCSEQ BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[TS_ERROR_LOG] ([CLASS], [COMPUTER], [EVENT_ID], [MSG], [MUSTCON], [STATE], [SOURCE], [PLCSEQ])
    VALUES (@V_CLASS, @V_COMPUTER, @V_EVENT_ID, @V_MSG, @V_MUSTCON, @V_STATE, @V_SOURCE, @V_PLCSEQ);
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[IF_SP_TRAY_IN]
    @V_INDUCTION INT,
    @V_TRAY_NO INT,
    @V_BARCODE NVARCHAR(100),
    @V_DELIVERY NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[TS_TRAY_FLOW] ([EventName], [Payload])
    VALUES (N'TRAY_IN', CONCAT(@V_INDUCTION, N'|', @V_TRAY_NO, N'|', @V_BARCODE, N'|', @V_DELIVERY));
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[IF_SP_BARCODE]
    @SCAN_SEQ INT,
    @V_INDUCTION INT,
    @V_DELIVERY NVARCHAR(20),
    @V_INVOICE NVARCHAR(100),
    @V_BARCODE NVARCHAR(100),
    @V_INPUT_STATUS NVARCHAR(20),
    @O_T_OQTY INT OUTPUT,
    @O_T_WQTY INT OUTPUT,
    @O_T_RQTY INT OUTPUT,
    @O_ITEM_CD NVARCHAR(50) OUTPUT,
    @O_ITEM_STYLE NVARCHAR(50) OUTPUT,
    @O_ITEM_COLOR NVARCHAR(50) OUTPUT,
    @O_ITEM_SIZE NVARCHAR(50) OUTPUT,
    @O_ITEM_NM NVARCHAR(100) OUTPUT,
    @O_SORT_TYPE NVARCHAR(50) OUTPUT,
    @O_SKU_OQTY INT OUTPUT,
    @O_SKU_WQTY INT OUTPUT,
    @O_SKU_RQTY INT OUTPUT,
    @ERROR_NO INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @O_T_OQTY = ISNULL(@O_T_OQTY, 0);
    SET @O_T_WQTY = ISNULL(@O_T_WQTY, 0);
    SET @O_T_RQTY = ISNULL(@O_T_RQTY, 0);
    SET @O_ITEM_CD = N'TEST_ITEM';
    SET @O_ITEM_STYLE = N'TEST_STYLE';
    SET @O_ITEM_COLOR = N'TEST_COLOR';
    SET @O_ITEM_SIZE = N'TEST_SIZE';
    SET @O_ITEM_NM = N'Test Item';
    SET @O_SORT_TYPE = N'TEST';
    SET @O_SKU_OQTY = 0;
    SET @O_SKU_WQTY = 0;
    SET @O_SKU_RQTY = 0;
    SET @ERROR_NO = 0;
    INSERT INTO [dbo].[TS_TRAY_FLOW] ([EventName], [Payload])
    VALUES (N'BARCODE', CONCAT(@SCAN_SEQ, N'|', @V_INDUCTION, N'|', @V_DELIVERY, N'|', @V_INVOICE, N'|', @V_BARCODE, N'|', @V_INPUT_STATUS));
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[IF_SP_DAS_SELECT]
    @V_BIZ_DAY CHAR(8),
    @V_DISP_YN NVARCHAR(1)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[TS_TRAY_FLOW] ([EventName], [Payload])
    VALUES (N'DAS_SELECT', CONCAT(@V_BIZ_DAY, N'|', @V_DISP_YN));
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[IF_SP_TILT_STOP]
    @V_CHUTE_NO INT,
    @V_BOXYN NVARCHAR(1)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[TS_TRAY_FLOW] ([EventName], [Payload])
    VALUES (N'TILT_STOP', CONCAT(@V_CHUTE_NO, N'|', @V_BOXYN));
END;
GO

MERGE [core].[Users] AS target
USING (VALUES
    (N'Alice', N'alice@test.example', 28),
    (N'Bob', N'bob@test.example', 35),
    (N'Charlie', N'charlie@test.example', 42)
) AS source ([UserName], [Email], [Age])
ON target.[Email] = source.[Email]
WHEN NOT MATCHED THEN INSERT ([UserName], [Email], [Age]) VALUES (source.[UserName], source.[Email], source.[Age]);

MERGE [core].[Products] AS target
USING (VALUES
    (N'Product A', CONVERT(DECIMAL(18,2), 100.00), 50),
    (N'Product B', CONVERT(DECIMAL(18,2), 200.00), 30),
    (N'Product C', CONVERT(DECIMAL(18,2), 300.00), 20)
) AS source ([ProductName], [Price], [Stock])
ON target.[ProductName] = source.[ProductName]
WHEN NOT MATCHED THEN INSERT ([ProductName], [Price], [Stock]) VALUES (source.[ProductName], source.[Price], source.[Stock]);

IF NOT EXISTS (SELECT 1 FROM [test].[DeadlockA] WHERE [Id] = 1) INSERT INTO [test].[DeadlockA] ([Id], [Val]) VALUES (1, 100);
IF NOT EXISTS (SELECT 1 FROM [test].[DeadlockB] WHERE [Id] = 1) INSERT INTO [test].[DeadlockB] ([Id], [Val]) VALUES (1, 200);

MERGE [verify].[ResultMappingRows] AS target
USING (VALUES
    (17, N'A01', CONVERT(DATE, '2026-05-17'), 1001, N'Generated User', N'generated.user@example.test', 27)
) AS source ([CELL_NO], [SLOT_NAME], [SCAN_DATE], [USER_ID], [USER_NAME], [EMAIL], [AGE])
ON target.[CELL_NO] = source.[CELL_NO]
WHEN MATCHED THEN UPDATE SET
    [SLOT_NAME] = source.[SLOT_NAME],
    [SCAN_DATE] = source.[SCAN_DATE],
    [USER_ID] = source.[USER_ID],
    [USER_NAME] = source.[USER_NAME],
    [EMAIL] = source.[EMAIL],
    [AGE] = source.[AGE]
WHEN NOT MATCHED THEN
    INSERT ([CELL_NO], [SLOT_NAME], [SCAN_DATE], [USER_ID], [USER_NAME], [EMAIL], [AGE])
    VALUES (source.[CELL_NO], source.[SLOT_NAME], source.[SCAN_DATE], source.[USER_ID], source.[USER_NAME], source.[EMAIL], source.[AGE]);

IF NOT EXISTS (SELECT 1 FROM [verify].[QuotedIdentifierRows] WHERE [RawCode] = N'tbl_order')
    INSERT INTO [verify].[QuotedIdentifierRows] ([RawCode]) VALUES (N'tbl_order');

IF NOT EXISTS (SELECT 1 FROM [dbo].[IF_CHUTE_INFO] WHERE [CHUTE_NO] = 1)
    INSERT INTO [dbo].[IF_CHUTE_INFO] ([CHUTE_NO], [CHUTE_NAME], [STATUS]) VALUES (1, N'CHUTE-001', N'OPEN'), (2, N'CHUTE-002', N'CLOSED');

IF NOT EXISTS (SELECT 1 FROM [dbo].[IF_BRAND_MASTER])
    INSERT INTO [dbo].[IF_BRAND_MASTER] ([BRAND_CD], [BRAND_NM])
    VALUES (N'B01', N'Brand 01'), (N'B02', N'Brand 02'), (N'B03', N'Brand 03'), (N'B04', N'Brand 04'), (N'B05', N'Brand 05'), (N'B06', N'Brand 06'), (N'B07', N'Brand 07');

IF NOT EXISTS (SELECT 1 FROM [dbo].[IF_BOX_LIST] WHERE [BIZ_DAY] = '20260309')
    INSERT INTO [dbo].[IF_BOX_LIST] ([BIZ_DAY], [BOX_NO]) VALUES ('20260309', N'BOX-001'), ('20260309', N'BOX-002');

IF NOT EXISTS (SELECT 1 FROM [dbo].[USR_INFO])
    INSERT INTO [dbo].[USR_INFO] ([USER_ID], [USER_NM]) VALUES (N'u01', N'User 01'), (N'u02', N'User 02'), (N'u03', N'User 03');

IF NOT EXISTS (SELECT 1 FROM [dbo].[MENU_INFO])
BEGIN
    INSERT INTO [dbo].[MENU_INFO] ([MENU_ID], [MENU_NM])
    SELECT v.[Id], CONCAT(N'Menu ', FORMAT(v.[Id], '00'))
    FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),(16),(17),(18)) AS v([Id]);
END;
GO
