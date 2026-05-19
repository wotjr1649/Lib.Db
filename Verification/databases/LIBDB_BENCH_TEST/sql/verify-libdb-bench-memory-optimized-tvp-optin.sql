-- ============================================================================
-- File: verify-libdb-bench-memory-optimized-tvp-optin.sql
-- Purpose: Optional memory-optimized TVP verification for LIBDB_BENCH_TEST.
-- Run after: setup-libdb-bench-memory-optimized-tvp-optin.sql
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -b -i verify-libdb-bench-memory-optimized-tvp-optin.sql
-- ============================================================================

USE [LIBDB_BENCH_TEST];
GO

SET NOCOUNT ON;

DECLARE @Failures TABLE ([Scope] NVARCHAR(120) NOT NULL, [Detail] NVARCHAR(4000) NOT NULL);

IF DB_NAME() <> N'LIBDB_BENCH_TEST'
    INSERT INTO @Failures VALUES (N'bench-guard', N'This opt-in verification is restricted to LIBDB_BENCH_TEST.');

IF ISNULL(CONVERT(INT, SERVERPROPERTY(N'IsXTPSupported')), 0) <> 1
    INSERT INTO @Failures VALUES (N'xtp-support', N'This SQL Server instance does not report In-Memory OLTP support.');

IF EXISTS (SELECT 1 FROM sys.databases WHERE [name] = DB_NAME() AND [is_auto_close_on] = 1)
    INSERT INTO @Failures VALUES (N'auto-close', N'AUTO_CLOSE must be OFF for databases that use MEMORY_OPTIMIZED_DATA.');

IF NOT EXISTS (SELECT 1 FROM sys.filegroups WHERE [type] = N'FX')
    INSERT INTO @Failures VALUES (N'memory-optimized-filegroup', N'A MEMORY_OPTIMIZED_DATA filegroup is required for memory-optimized TVP types.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_files AS files
    INNER JOIN sys.filegroups AS filegroups
        ON filegroups.data_space_id = files.data_space_id
    WHERE filegroups.[type] = N'FX'
)
    INSERT INTO @Failures VALUES (N'memory-optimized-file', N'A MEMORY_OPTIMIZED_DATA file container is required.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.table_types AS table_types
    WHERE SCHEMA_NAME(table_types.[schema_id]) = N'dbo'
      AND table_types.[name] = N'libdb_bench_MemoryOptimizedOrderItem'
      AND table_types.[is_memory_optimized] = 1
)
    INSERT INTO @Failures VALUES (N'memory-optimized-type', N'dbo.libdb_bench_MemoryOptimizedOrderItem must be a memory-optimized table type.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.table_types AS table_types
    INNER JOIN sys.hash_indexes AS hash_indexes
        ON hash_indexes.[object_id] = table_types.[type_table_object_id]
    WHERE SCHEMA_NAME(table_types.[schema_id]) = N'dbo'
      AND table_types.[name] = N'libdb_bench_MemoryOptimizedOrderItem'
      AND hash_indexes.[bucket_count] = 1024
)
    INSERT INTO @Failures VALUES (N'memory-optimized-hash-index', N'Memory-optimized TVP type must expose the expected hash index.');

IF OBJECT_ID(N'[dbo].[libdb_bench_MemoryOptimizedOrderItems]', N'U') IS NULL
    INSERT INTO @Failures VALUES (N'target-table', N'Memory-optimized TVP target table is missing.');

IF OBJECT_ID(N'[dbo].[libdb_bench_InsertMemoryOptimizedOrderItems]', N'P') IS NULL
    INSERT INTO @Failures VALUES (N'procedure', N'Memory-optimized TVP procedure is missing.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.parameters AS parameters
    INNER JOIN sys.table_types AS table_types
        ON table_types.[user_type_id] = parameters.[user_type_id]
    WHERE parameters.[object_id] = OBJECT_ID(N'[dbo].[libdb_bench_InsertMemoryOptimizedOrderItems]', N'P')
      AND parameters.[name] = N'@Rows'
      AND parameters.[is_readonly] = 1
      AND SCHEMA_NAME(table_types.[schema_id]) = N'dbo'
      AND table_types.[name] = N'libdb_bench_MemoryOptimizedOrderItem'
      AND table_types.[is_memory_optimized] = 1
)
    INSERT INTO @Failures VALUES (N'procedure-tvp-parameter', N'Procedure must bind @Rows to the memory-optimized TVP type as READONLY.');

IF NOT EXISTS (SELECT 1 FROM @Failures)
BEGIN
    DECLARE @Rows [dbo].[libdb_bench_MemoryOptimizedOrderItem];
    INSERT INTO @Rows ([Id], [Sku], [Qty], [Price])
    VALUES (1, N'MEMOPT-VERIFY-1', 2, 12.30),
           (2, N'MEMOPT-VERIFY-2', 3, 15.40);

    DECLARE @Result TABLE ([InsertedCount] BIGINT);
    INSERT INTO @Result
    EXEC [dbo].[libdb_bench_InsertMemoryOptimizedOrderItems]
        @OrderId = 9230,
        @RequestedBy = N'verify-memory-optimized-tvp',
        @Rows = @Rows;

    IF NOT EXISTS (SELECT 1 FROM @Result WHERE [InsertedCount] = 2)
        INSERT INTO @Failures VALUES (N'memory-optimized-tvp-smoke', N'Memory-optimized TVP insert did not return the expected inserted count.');
END;

IF EXISTS (SELECT 1 FROM @Failures)
BEGIN
    SELECT [Scope], [Detail] FROM @Failures ORDER BY [Scope], [Detail];
    DECLARE @Message NVARCHAR(2048) = CONCAT(N'LIBDB_BENCH_TEST memory-optimized TVP opt-in verification failed: ', (SELECT COUNT(*) FROM @Failures), N' issue(s).');
    THROW 51424, @Message, 1;
END;

SELECT N'LIBDB_BENCH_TEST memory-optimized TVP opt-in verification passed.' AS [Result],
       (
           SELECT COUNT(*)
           FROM sys.table_types
           WHERE [is_memory_optimized] = 1
       ) AS [MemoryOptimizedTableTypes],
       N'Default v2.3.0 verification intentionally excludes this opt-in file.' AS [BoundaryNote];
GO
