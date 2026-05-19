-- ============================================================================
-- File: verify-libdb-stress-test.sql
-- Purpose: Stress database readiness checks for LIBDB_STRESS_TEST.
-- Run after: setup-libdb-stress-test.sql
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i verify-libdb-stress-test.sql -f 65001 -b
-- ============================================================================

USE [LIBDB_STRESS_TEST];
GO

SET NOCOUNT ON;

DECLARE @Failures TABLE ([Scope] NVARCHAR(120) NOT NULL, [Detail] NVARCHAR(4000) NOT NULL);

DECLARE @ExpectedTables TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTables VALUES
(N'stress.Users'), (N'stress.Orders'), (N'stress.PoolProbe'), (N'stress.LockA'), (N'stress.LockB'),
(N'stress.TvpLoadRuns'), (N'stress.TvpLoadEvents'), (N'stress.TvpShapeNarrow'),
(N'stress.TvpShapeMedium'), (N'stress.TvpShapeWide'), (N'stress.TvpShapeSparse'),
(N'stress.TvpShapeLob'), (N'stress.TvpShapeBinary'), (N'stress.TvpShapeTemporal'),
(N'stress.TvpShapeComposite'), (N'stress.TvpShapeJson'), (N'stress.TvpMultiHeader'),
(N'stress.TvpMultiLine');

INSERT INTO @Failures
SELECT N'table-exists', CONCAT(N'Missing table: ', [Name])
FROM @ExpectedTables
WHERE OBJECT_ID(QUOTENAME(PARSENAME([Name], 2)) + N'.' + QUOTENAME(PARSENAME([Name], 1)), N'U') IS NULL;

DECLARE @ExpectedProcedures TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedProcedures VALUES
(N'stress.usp_SeedUsers'), (N'stress.usp_ReadUsersPage'), (N'stress.usp_InsertOrders'),
(N'stress.usp_MixedReadWrite'), (N'stress.usp_PoolProbe'), (N'stress.usp_Deadlock_Left'),
(N'stress.usp_Deadlock_Right'), (N'stress.usp_ResetStressData'),
(N'stress.usp_Tvp_Narrow_Insert'), (N'stress.usp_Tvp_Medium_Insert'),
(N'stress.usp_Tvp_Wide_Insert'), (N'stress.usp_Tvp_Sparse_Insert'),
(N'stress.usp_Tvp_Lob_Insert'), (N'stress.usp_Tvp_Binary_Insert'),
(N'stress.usp_Tvp_Temporal_Insert'), (N'stress.usp_Tvp_Composite_Upsert'),
(N'stress.usp_Tvp_Json_Insert'), (N'stress.usp_Tvp_Multi_Insert'),
(N'stress.usp_Tvp_Narrow_CountOnly'), (N'stress.usp_Tvp_Narrow_WithOutput'),
(N'stress.usp_Tvp_Narrow_MultiResult'), (N'stress.usp_Tvp_Narrow_ScalarAndTvp'),
(N'stress.usp_Tvp_Narrow_ZeroRows'), (N'stress.usp_Tvp_Narrow_OptionalFilter'),
(N'stress.usp_Tvp_Medium_MixedReadWrite'), (N'stress.usp_Tvp_Medium_Page'),
(N'stress.usp_Tvp_Wide_JoinUsers'), (N'stress.usp_Tvp_Lob_Checksum'),
(N'stress.usp_Tvp_Binary_Checksum'), (N'stress.usp_Tvp_Temporal_Window'),
(N'stress.usp_Tvp_Composite_MergeLikeUpdate'), (N'stress.usp_Tvp_Json_OpenJson'),
(N'stress.usp_Tvp_Load_Run_Start'), (N'stress.usp_Tvp_Load_Run_Finish'),
(N'stress.usp_Tvp_QueryStore_Probe'), (N'stress.usp_Tvp_ResetMatrixData');

INSERT INTO @Failures
SELECT N'procedure-exists', CONCAT(N'Missing procedure: ', [Name])
FROM @ExpectedProcedures
WHERE OBJECT_ID(QUOTENAME(PARSENAME([Name], 2)) + N'.' + QUOTENAME(PARSENAME([Name], 1)), N'P') IS NULL;

DECLARE @ExpectedTypes TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTypes VALUES
(N'stress.Tvp_OrderLine'), (N'stress.Tvp_Narrow'), (N'stress.Tvp_Medium'),
(N'stress.Tvp_Wide'), (N'stress.Tvp_Sparse'), (N'stress.Tvp_Lob'),
(N'stress.Tvp_Binary'), (N'stress.Tvp_Temporal'), (N'stress.Tvp_Composite'),
(N'stress.Tvp_Json');

INSERT INTO @Failures
SELECT N'tvp-type-exists', CONCAT(N'Missing TVP type: ', [Name])
FROM @ExpectedTypes
WHERE TYPE_ID([Name]) IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'stress.Users') AND name = N'IX_stress_Users_Tenant_Bucket')
    INSERT INTO @Failures VALUES (N'index-exists', N'Missing stress.Users tenant/bucket index.');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'stress.Orders') AND name = N'IX_stress_Orders_Tenant_User')
    INSERT INTO @Failures VALUES (N'index-exists', N'Missing stress.Orders tenant/user index.');

DECLARE @Seed TABLE ([RowsInserted] INT);
INSERT INTO @Seed EXEC [stress].[usp_SeedUsers] @TenantId = 230, @RowCount = 32;
IF NOT EXISTS (SELECT 1 FROM @Seed WHERE [RowsInserted] = 32)
    INSERT INTO @Failures VALUES (N'seed-smoke', N'stress.usp_SeedUsers did not insert 32 rows.');

DECLARE @Page TABLE ([UserId] BIGINT, [TenantId] INT, [UserName] NVARCHAR(100), [Email] NVARCHAR(255), [Bucket] INT);
INSERT INTO @Page EXEC [stress].[usp_ReadUsersPage] @TenantId = 230, @Bucket = 1, @Offset = 0, @Fetch = 8;
IF NOT EXISTS (SELECT 1 FROM @Page)
    INSERT INTO @Failures VALUES (N'paging-smoke', N'stress.usp_ReadUsersPage returned no rows.');

DECLARE @FirstUserId BIGINT = (SELECT TOP (1) [UserId] FROM @Page ORDER BY [UserId]);
DECLARE @Rows [stress].[Tvp_OrderLine];
IF @FirstUserId IS NOT NULL
    INSERT INTO @Rows ([UserId], [Sku], [Qty], [Price]) VALUES (@FirstUserId, N'STRESS-VERIFY', 1, 9.99);
DECLARE @InsertOrders TABLE ([RowsInserted] INT);
INSERT INTO @InsertOrders EXEC [stress].[usp_InsertOrders] @TenantId = 230, @Rows = @Rows;
IF NOT EXISTS (SELECT 1 FROM @InsertOrders WHERE [RowsInserted] = 1)
    INSERT INTO @Failures VALUES (N'tvp-insert-smoke', N'stress.usp_InsertOrders did not insert one order.');

DECLARE @Probe TABLE ([ProbeId] BIGINT, [Spid] INT);
INSERT INTO @Probe EXEC [stress].[usp_PoolProbe] @WorkerName = N'verify', @DelayMilliseconds = 0;
IF NOT EXISTS (SELECT 1 FROM @Probe WHERE [ProbeId] IS NOT NULL AND [Spid] > 0)
    INSERT INTO @Failures VALUES (N'pool-probe-smoke', N'stress.usp_PoolProbe did not return probe id/spid.');

IF NOT EXISTS (SELECT 1 FROM [stress].[LockA] WHERE [Id] = 1)
    INSERT INTO @Failures VALUES (N'concurrency-readiness', N'stress.LockA seed row is missing.');
IF NOT EXISTS (SELECT 1 FROM [stress].[LockB] WHERE [Id] = 1)
    INSERT INTO @Failures VALUES (N'concurrency-readiness', N'stress.LockB seed row is missing.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_query_store_options
    WHERE [desired_state_desc] = N'READ_WRITE'
      AND [actual_state_desc] = N'READ_WRITE'
)
    INSERT INTO @Failures VALUES (N'query-store', N'Query Store must be enabled for stress workload analysis.');

IF EXISTS (SELECT 1 FROM @Failures)
BEGIN
    SELECT [Scope], [Detail] FROM @Failures ORDER BY [Scope], [Detail];
    DECLARE @Message NVARCHAR(2048) = CONCAT(N'LIBDB_STRESS_TEST verification failed: ', (SELECT COUNT(*) FROM @Failures), N' issue(s).');
    THROW 51100, @Message, 1;
END;

SELECT N'LIBDB_STRESS_TEST verification passed.' AS [Result],
       (SELECT COUNT(*) FROM @ExpectedTables) AS [ExpectedTables],
       (SELECT COUNT(*) FROM @ExpectedTypes) AS [ExpectedTypes],
       (SELECT COUNT(*) FROM @ExpectedProcedures) AS [ExpectedProcedures],
       N'Multi-session deadlock/load execution requires external harness.' AS [ConcurrencyNote];
GO
