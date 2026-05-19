-- ============================================================================
-- File: verify-libdb-bench-test.sql
-- Purpose: Benchmark database readiness checks for LIBDB_BENCH_TEST.
-- Run after: setup-libdb-bench-test.sql and setup-libdb-bench-memory-optimized-tvp-optin.sql
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i verify-libdb-bench-test.sql -f 65001 -b
-- ============================================================================

USE [LIBDB_BENCH_TEST];
GO

SET NOCOUNT ON;

DECLARE @Failures TABLE ([Scope] NVARCHAR(120) NOT NULL, [Detail] NVARCHAR(4000) NOT NULL);

IF DB_NAME() NOT LIKE N'%BENCH%'
    INSERT INTO @Failures VALUES (N'bench-guard', N'Database name must contain BENCH for BenchmarkDatabase reset guard compatibility.');

DECLARE @ExpectedTables TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTables VALUES
(N'dbo.libdb_bench_OrderItems'), (N'dbo.libdb_bench_WideOrderItems'), (N'dbo.libdb_bench_Runs'),
(N'dbo.libdb_bench_MediumItems'), (N'dbo.libdb_bench_UltraWideItems'),
(N'dbo.libdb_bench_NullableItems'), (N'dbo.libdb_bench_SparseItems'),
(N'dbo.libdb_bench_LobItems'), (N'dbo.libdb_bench_BinaryItems'),
(N'dbo.libdb_bench_DecimalItems'), (N'dbo.libdb_bench_TemporalItems'),
(N'dbo.libdb_bench_GuidItems'), (N'dbo.libdb_bench_JsonItems'),
(N'dbo.libdb_bench_CompositeItems'), (N'dbo.libdb_bench_MultiHeaders'),
(N'dbo.libdb_bench_MultiLines'), (N'dbo.libdb_bench_MethodRuns'),
(N'dbo.libdb_bench_MethodRunMetrics'), (N'dbo.libdb_bench_BulkCopyStage'),
(N'dbo.libdb_bench_DataReaderStage'), (N'dbo.libdb_bench_MemoryOptimizedOrderItems');

INSERT INTO @Failures
SELECT N'table-exists', CONCAT(N'Missing table: ', [Name])
FROM @ExpectedTables
WHERE OBJECT_ID(QUOTENAME(PARSENAME([Name], 2)) + N'.' + QUOTENAME(PARSENAME([Name], 1)), N'U') IS NULL;

DECLARE @ExpectedTypes TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTypes VALUES
(N'dbo.libdb_bench_OrderItem'), (N'dbo.libdb_bench_WideOrderItem'),
(N'dbo.libdb_bench_MediumOrderItem'), (N'dbo.libdb_bench_UltraWideOrderItem'),
(N'dbo.libdb_bench_NullableOrderItem'), (N'dbo.libdb_bench_SparseOrderItem'),
(N'dbo.libdb_bench_LobOrderItem'), (N'dbo.libdb_bench_BinaryOrderItem'),
(N'dbo.libdb_bench_DecimalOrderItem'), (N'dbo.libdb_bench_TemporalOrderItem'),
(N'dbo.libdb_bench_GuidOrderItem'), (N'dbo.libdb_bench_JsonOrderItem'),
(N'dbo.libdb_bench_CompositeOrderItem'), (N'dbo.libdb_bench_MultiOrderHeader'),
(N'dbo.libdb_bench_MemoryOptimizedOrderItem');

INSERT INTO @Failures
SELECT N'tvp-type-exists', CONCAT(N'Missing TVP type: ', [Name])
FROM @ExpectedTypes
WHERE TYPE_ID([Name]) IS NULL;

DECLARE @ExpectedProcedures TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedProcedures VALUES
(N'dbo.libdb_bench_InsertOrderItems'), (N'dbo.libdb_bench_InsertWideOrderItems'),
(N'dbo.libdb_bench_InsertMediumOrderItems'), (N'dbo.libdb_bench_InsertUltraWideOrderItems'),
(N'dbo.libdb_bench_InsertNullableOrderItems'), (N'dbo.libdb_bench_InsertSparseOrderItems'),
(N'dbo.libdb_bench_InsertLobOrderItems'), (N'dbo.libdb_bench_InsertBinaryOrderItems'),
(N'dbo.libdb_bench_InsertDecimalOrderItems'), (N'dbo.libdb_bench_InsertTemporalOrderItems'),
(N'dbo.libdb_bench_InsertGuidOrderItems'), (N'dbo.libdb_bench_InsertJsonOrderItems'),
(N'dbo.libdb_bench_InsertCompositeOrderItems'), (N'dbo.libdb_bench_InsertMultiOrderGraph'),
(N'dbo.libdb_bench_CountMediumOrderItems'), (N'dbo.libdb_bench_CountUltraWideOrderItems'),
(N'dbo.libdb_bench_CountNullableOrderItems'), (N'dbo.libdb_bench_CountSparseOrderItems'),
(N'dbo.libdb_bench_CountLobOrderItems'), (N'dbo.libdb_bench_CountBinaryOrderItems'),
(N'dbo.libdb_bench_CountDecimalOrderItems'), (N'dbo.libdb_bench_CountTemporalOrderItems'),
(N'dbo.libdb_bench_CountGuidOrderItems'), (N'dbo.libdb_bench_CountJsonOrderItems'),
(N'dbo.libdb_bench_CountCompositeOrderItems'), (N'dbo.libdb_bench_CountMultiOrderGraph'),
(N'dbo.libdb_bench_ClearMediumOrderItems'), (N'dbo.libdb_bench_ClearUltraWideOrderItems'),
(N'dbo.libdb_bench_ClearNullableOrderItems'), (N'dbo.libdb_bench_ClearSparseOrderItems'),
(N'dbo.libdb_bench_ClearLobOrderItems'), (N'dbo.libdb_bench_ClearBinaryOrderItems'),
(N'dbo.libdb_bench_ClearDecimalOrderItems'), (N'dbo.libdb_bench_ClearTemporalOrderItems'),
(N'dbo.libdb_bench_ClearGuidOrderItems'), (N'dbo.libdb_bench_ClearJsonOrderItems'),
(N'dbo.libdb_bench_ClearCompositeOrderItems'), (N'dbo.libdb_bench_ClearMultiOrderGraph'),
(N'dbo.libdb_bench_StartMethodRun'), (N'dbo.libdb_bench_FinishMethodRun'),
(N'dbo.libdb_bench_RecordMethodMetric'), (N'dbo.libdb_bench_ResetBenchmarkMatrix'),
(N'dbo.libdb_bench_InsertMemoryOptimizedOrderItems');

INSERT INTO @Failures
SELECT N'procedure-exists', CONCAT(N'Missing procedure: ', [Name])
FROM @ExpectedProcedures
WHERE OBJECT_ID(QUOTENAME(PARSENAME([Name], 2)) + N'.' + QUOTENAME(PARSENAME([Name], 1)), N'P') IS NULL;

IF
(
    SELECT COUNT(*)
    FROM sys.table_types AS tt
    INNER JOIN sys.columns AS columns
        ON columns.object_id = tt.type_table_object_id
    WHERE SCHEMA_NAME(tt.schema_id) = N'dbo'
      AND tt.name = N'libdb_bench_WideOrderItem'
) <> 16
    INSERT INTO @Failures VALUES (N'wide-tvp-shape', N'dbo.libdb_bench_WideOrderItem must have 16 columns.');

IF EXISTS (SELECT 1 FROM sys.databases WHERE [name] = DB_NAME() AND [is_auto_close_on] = 1)
    INSERT INTO @Failures VALUES (N'memory-optimized-auto-close', N'AUTO_CLOSE must be OFF because LIBDB_BENCH_TEST includes MEMORY_OPTIMIZED_DATA.');

IF NOT EXISTS (SELECT 1 FROM sys.filegroups WHERE [type] = N'FX')
    INSERT INTO @Failures VALUES (N'memory-optimized-filegroup', N'LIBDB_BENCH_TEST must include a MEMORY_OPTIMIZED_DATA filegroup.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.table_types AS table_types
    WHERE SCHEMA_NAME(table_types.[schema_id]) = N'dbo'
      AND table_types.[name] = N'libdb_bench_MemoryOptimizedOrderItem'
      AND table_types.[is_memory_optimized] = 1
)
    INSERT INTO @Failures VALUES (N'memory-optimized-type', N'dbo.libdb_bench_MemoryOptimizedOrderItem must be memory optimized.');

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
    INSERT INTO @Failures VALUES (N'memory-optimized-hash-index', N'dbo.libdb_bench_MemoryOptimizedOrderItem must expose the expected hash index.');

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
    INSERT INTO @Failures VALUES (N'memory-optimized-tvp-param', N'dbo.libdb_bench_InsertMemoryOptimizedOrderItems @Rows must be READONLY dbo.libdb_bench_MemoryOptimizedOrderItem.');

DECLARE @Rows [dbo].[libdb_bench_OrderItem];
INSERT INTO @Rows ([Id], [Sku], [Qty], [Price]) VALUES (1, N'BENCH-VERIFY', 2, 12.30);
DECLARE @Narrow TABLE ([InsertedCount] BIGINT);
INSERT INTO @Narrow EXEC [dbo].[libdb_bench_InsertOrderItems] @OrderId = 230, @RequestedBy = N'verify', @Rows = @Rows;
IF NOT EXISTS (SELECT 1 FROM @Narrow WHERE [InsertedCount] >= 1)
    INSERT INTO @Failures VALUES (N'narrow-tvp-smoke', N'Narrow benchmark TVP insert did not return inserted count.');

DECLARE @WideRows [dbo].[libdb_bench_WideOrderItem];
INSERT INTO @WideRows
(
    [Id], [Sku], [Qty], [Price], [Discount], [Tax], [LineTotal], [IsGift],
    [WarehouseId], [Region], [BatchId], [RequestedAt], [SequenceNumber], [Priority], [Status], [Note]
)
VALUES
(
    1, N'BENCH-WIDE', 2, 12.30, 1.00, 0.80, 12.10, 0,
    10, N'KR', NEWID(), SYSUTCDATETIME(), 1, 1, 1, N'verify'
);
DECLARE @Wide TABLE ([InsertedCount] BIGINT);
INSERT INTO @Wide EXEC [dbo].[libdb_bench_InsertWideOrderItems] @OrderId = 231, @RequestedBy = N'verify', @Rows = @WideRows;
IF NOT EXISTS (SELECT 1 FROM @Wide WHERE [InsertedCount] >= 1)
    INSERT INTO @Failures VALUES (N'wide-tvp-smoke', N'Wide benchmark TVP insert did not return inserted count.');

DECLARE @MemoryOptimizedRows [dbo].[libdb_bench_MemoryOptimizedOrderItem];
INSERT INTO @MemoryOptimizedRows ([Id], [Sku], [Qty], [Price])
VALUES (1, N'BENCH-MEMOPT-1', 2, 12.30),
       (2, N'BENCH-MEMOPT-2', 3, 15.40);
DECLARE @MemoryOptimized TABLE ([InsertedCount] BIGINT);
INSERT INTO @MemoryOptimized
EXEC [dbo].[libdb_bench_InsertMemoryOptimizedOrderItems]
    @OrderId = 232,
    @RequestedBy = N'verify',
    @Rows = @MemoryOptimizedRows;
IF NOT EXISTS (SELECT 1 FROM @MemoryOptimized WHERE [InsertedCount] = 2)
    INSERT INTO @Failures VALUES (N'memory-optimized-tvp-smoke', N'Memory-optimized benchmark TVP insert did not return inserted count.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_query_store_options
    WHERE [desired_state_desc] = N'READ_WRITE'
      AND [actual_state_desc] = N'READ_WRITE'
)
    INSERT INTO @Failures VALUES (N'query-store', N'Query Store must be enabled for benchmark workload analysis.');

IF EXISTS (SELECT 1 FROM @Failures)
BEGIN
    SELECT [Scope], [Detail] FROM @Failures ORDER BY [Scope], [Detail];
    DECLARE @Message NVARCHAR(2048) = CONCAT(N'LIBDB_BENCH_TEST verification failed: ', (SELECT COUNT(*) FROM @Failures), N' issue(s).');
    THROW 51300, @Message, 1;
END;

SELECT N'LIBDB_BENCH_TEST verification passed.' AS [Result],
       (SELECT COUNT(*) FROM @ExpectedTables) AS [ExpectedTables],
       (SELECT COUNT(*) FROM @ExpectedTypes) AS [ExpectedTypes],
       (SELECT COUNT(*) FROM @ExpectedProcedures) AS [ExpectedProcedures],
       N'SqlBulkCopy and BenchmarkDotNet timing require .NET benchmark harness. Memory-optimized TVP opt-in is part of the final BENCH sync manifest.' AS [BenchmarkNote];
GO
