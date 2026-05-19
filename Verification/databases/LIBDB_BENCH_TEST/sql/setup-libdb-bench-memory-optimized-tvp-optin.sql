-- ============================================================================
-- File: setup-libdb-bench-memory-optimized-tvp-optin.sql
-- Purpose: Optional memory-optimized TVP case for LIBDB_BENCH_TEST.
-- Scope: DATABASE. Do not include this file in the default DB setup flow.
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -b -i setup-libdb-bench-memory-optimized-tvp-optin.sql
-- ============================================================================

USE [master];
GO

SET NOCOUNT ON;

IF DB_ID(N'LIBDB_BENCH_TEST') IS NULL
    THROW 51420, N'LIBDB_BENCH_TEST must exist before memory-optimized TVP opt-in setup.', 1;

IF ISNULL(CONVERT(INT, SERVERPROPERTY(N'IsXTPSupported')), 0) <> 1
    THROW 51421, N'This SQL Server instance does not report In-Memory OLTP support.', 1;
GO

ALTER DATABASE [LIBDB_BENCH_TEST] SET AUTO_CLOSE OFF WITH NO_WAIT;
GO

USE [LIBDB_BENCH_TEST];
GO

SET NOCOUNT ON;

IF DB_NAME() <> N'LIBDB_BENCH_TEST'
    THROW 51422, N'This opt-in setup is restricted to LIBDB_BENCH_TEST.', 1;

DECLARE @MemoryOptimizedFileGroup SYSNAME;
SELECT TOP (1) @MemoryOptimizedFileGroup = [name]
FROM sys.filegroups
WHERE [type] = N'FX'
ORDER BY [name];

IF @MemoryOptimizedFileGroup IS NULL
BEGIN
    SET @MemoryOptimizedFileGroup = N'LIBDB_BENCH_TEST_memopt';

    DECLARE @AddFileGroupSql NVARCHAR(MAX) =
        N'ALTER DATABASE [LIBDB_BENCH_TEST] ADD FILEGROUP ' +
        QUOTENAME(@MemoryOptimizedFileGroup) +
        N' CONTAINS MEMORY_OPTIMIZED_DATA;';

    EXEC sys.sp_executesql @AddFileGroupSql;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_files AS files
    INNER JOIN sys.filegroups AS filegroups
        ON filegroups.data_space_id = files.data_space_id
    WHERE filegroups.[name] = @MemoryOptimizedFileGroup
)
BEGIN
    DECLARE @DataPath NVARCHAR(4000) = CONVERT(NVARCHAR(4000), SERVERPROPERTY(N'InstanceDefaultDataPath'));

    IF COALESCE(@DataPath, N'') = N''
    BEGIN
        SELECT TOP (1)
            @DataPath = LEFT([physical_name], LEN([physical_name]) - CHARINDEX(N'\', REVERSE([physical_name])) + 1)
        FROM sys.database_files
        WHERE [type_desc] = N'ROWS'
        ORDER BY [file_id];
    END;

    IF COALESCE(@DataPath, N'') = N''
        THROW 51423, N'Could not resolve SQL Server data path for memory-optimized filegroup.', 1;

    IF RIGHT(@DataPath, 1) NOT IN (N'\', N'/')
        SET @DataPath = @DataPath + N'\';

    DECLARE @MemoryOptimizedFile SYSNAME = N'LIBDB_BENCH_TEST_memopt';
    DECLARE @MemoryOptimizedFilePath NVARCHAR(4000) = @DataPath + @MemoryOptimizedFile;
    DECLARE @AddFileSql NVARCHAR(MAX) =
        N'ALTER DATABASE [LIBDB_BENCH_TEST] ADD FILE (NAME = N''' +
        REPLACE(@MemoryOptimizedFile, N'''', N'''''') +
        N''', FILENAME = N''' +
        REPLACE(@MemoryOptimizedFilePath, N'''', N'''''') +
        N''') TO FILEGROUP ' +
        QUOTENAME(@MemoryOptimizedFileGroup) +
        N';';

    EXEC sys.sp_executesql @AddFileSql;
END;
GO

ALTER DATABASE [LIBDB_BENCH_TEST] SET MEMORY_OPTIMIZED_ELEVATE_TO_SNAPSHOT = ON;
GO

USE [LIBDB_BENCH_TEST];
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[libdb_bench_InsertMemoryOptimizedOrderItems]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[libdb_bench_InsertMemoryOptimizedOrderItems];

IF OBJECT_ID(N'[dbo].[libdb_bench_MemoryOptimizedOrderItems]', N'U') IS NOT NULL
    DROP TABLE [dbo].[libdb_bench_MemoryOptimizedOrderItems];

IF TYPE_ID(N'dbo.libdb_bench_MemoryOptimizedOrderItem') IS NOT NULL
    DROP TYPE [dbo].[libdb_bench_MemoryOptimizedOrderItem];
GO

CREATE TYPE [dbo].[libdb_bench_MemoryOptimizedOrderItem] AS TABLE
(
    [Id] INT NOT NULL,
    [Sku] NVARCHAR(64) NOT NULL,
    [Qty] INT NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL,
    INDEX [IX_libdb_bench_MemoryOptimizedOrderItem_Id] HASH ([Id]) WITH (BUCKET_COUNT = 1024)
)
WITH (MEMORY_OPTIMIZED = ON);
GO

CREATE TABLE [dbo].[libdb_bench_MemoryOptimizedOrderItems]
(
    [OrderId] INT NOT NULL,
    [RequestedBy] NVARCHAR(64) NOT NULL,
    [Id] INT NOT NULL,
    [Sku] NVARCHAR(64) NOT NULL,
    [Qty] INT NOT NULL,
    [Price] DECIMAL(18,2) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_MemoryOptimizedOrderItems_CreatedAt] DEFAULT SYSUTCDATETIME()
);
GO

CREATE OR ALTER PROCEDURE [dbo].[libdb_bench_InsertMemoryOptimizedOrderItems]
    @OrderId INT,
    @RequestedBy NVARCHAR(64),
    @Rows [dbo].[libdb_bench_MemoryOptimizedOrderItem] READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [dbo].[libdb_bench_MemoryOptimizedOrderItems]
    WHERE [OrderId] = @OrderId;

    INSERT INTO [dbo].[libdb_bench_MemoryOptimizedOrderItems] ([OrderId], [RequestedBy], [Id], [Sku], [Qty], [Price])
    SELECT @OrderId, @RequestedBy, [Id], [Sku], [Qty], [Price]
    FROM @Rows;

    SELECT COUNT_BIG(*) AS [InsertedCount]
    FROM [dbo].[libdb_bench_MemoryOptimizedOrderItems]
    WHERE [OrderId] = @OrderId;
END;
GO

SELECT N'LIBDB_BENCH_TEST memory-optimized TVP opt-in setup completed.' AS [Result];
GO
