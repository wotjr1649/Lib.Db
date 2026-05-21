-- ============================================================================
-- File: verify-libdb-verification-test.sql
-- Purpose: Functional verification checks for LIBDB_VERIFICATION_TEST.
-- Run after: setup-libdb-verification-test.sql
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i verify-libdb-verification-test.sql -f 65001 -b
-- ============================================================================

USE [LIBDB_VERIFICATION_TEST];
GO

SET NOCOUNT ON;
SET XACT_ABORT OFF;

DECLARE @Failures TABLE
(
    [Scope] NVARCHAR(120) NOT NULL,
    [Detail] NVARCHAR(4000) NOT NULL
);

DECLARE @ExpectedTables TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTables ([Name]) VALUES
(N'adv.ResumableLogs'), (N'core.CursorState'), (N'core.Orders'), (N'core.Products'), (N'core.Users'),
(N'dbo.IF_BOX_LIST'), (N'dbo.IF_BRAND_MASTER'), (N'dbo.IF_CHUTE_INFO'), (N'dbo.libdb_aot_OrderItems'),
(N'dbo.libdb_bench_OrderItems'), (N'dbo.MENU_INFO'), (N'dbo.TS_CHUTE_BTN_LOG'), (N'dbo.TS_EMR_LOG'),
(N'dbo.TS_ERROR_LOG'), (N'dbo.TS_TILT_LOG'), (N'dbo.TS_TRAY_FLOW'), (N'dbo.USR_INFO'),
(N'exception.ChildTable'), (N'exception.ParentTable'), (N'exception.UniqueTable'),
(N'gap.BulkTarget'), (N'gap.JsonData'), (N'gap.MergeTarget'), (N'perf.BulkTest'),
(N'resilience.RetryTest'), (N'resilience.TimeoutTest'), (N'test.DeadlockA'), (N'test.DeadlockB'),
(N'tvp.TypeTest'), (N'verify.QuotedIdentifierRows'), (N'verify.ResultMappingRows'),
(N'verify.VerificationCustomerSegments'), (N'verify.VerificationOrderAudit'),
(N'verify.VerificationOrderHeaders'), (N'verify.VerificationOrderLines');

INSERT INTO @Failures ([Scope], [Detail])
SELECT N'table-exists', CONCAT(N'Missing table: ', e.[Name])
FROM @ExpectedTables e
WHERE OBJECT_ID(QUOTENAME(PARSENAME(e.[Name], 2)) + N'.' + QUOTENAME(PARSENAME(e.[Name], 1)), N'U') IS NULL;

DECLARE @ExpectedTypes TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedTypes ([Name]) VALUES
(N'core.Tvp_Core_User'), (N'dbo.libdb_aot_OrderItem'), (N'dbo.libdb_bench_OrderItem'),
(N'dbo.T_PrecisionEvent'), (N'dbo.T_StandardEvent'), (N'gap.Tvp_BulkTarget'),
(N'perf.Tvp_Perf_BulkInsert'), (N'tvp.Tvp_Tvp_AllTypes'), (N'tvp.Tvp_Tvp_Nullable'),
(N'tvp.Tvp_Tvp_SchemaMismatch'), (N'tvp.TypeTest'), (N'verify.Tvp_VerificationOrderHeader'),
(N'verify.Tvp_VerificationOrderLine');

INSERT INTO @Failures ([Scope], [Detail])
SELECT N'tvp-type-exists', CONCAT(N'Missing TVP type: ', e.[Name])
FROM @ExpectedTypes e
WHERE TYPE_ID(e.[Name]) IS NULL;

DECLARE @ExpectedProcedures TABLE ([Name] SYSNAME NOT NULL PRIMARY KEY);
INSERT INTO @ExpectedProcedures ([Name]) VALUES
(N'adv.usp_Adv_GenerateLogs'), (N'adv.usp_Adv_OutputParameters'),
(N'core.usp_Core_Bulk_Insert_Users'), (N'core.usp_Core_Get_Dashboard'), (N'core.usp_Core_Get_User'),
(N'core.usp_Core_Insert_User'), (N'core.usp_Core_Search_Users'), (N'core.usp_Core_Transaction_Test'),
(N'dbo.IF_SP_BARCODE'), (N'dbo.IF_SP_CHUTE_BTN_LOG'), (N'dbo.IF_SP_DAS_SELECT'), (N'dbo.IF_SP_EMR_LOG'),
(N'dbo.IF_SP_ERROR_LOG'), (N'dbo.IF_SP_TILT_LOG'), (N'dbo.IF_SP_TILT_STOP'), (N'dbo.IF_SP_TRAY_IN'),
(N'dbo.libdb_aot_InsertOrderItems'), (N'dbo.libdb_bench_InsertOrderItems'), (N'dbo.usp_Test_Reset_All_Data'),
(N'exception.usp_Exception_DivideByZero'), (N'exception.usp_Exception_ForeignKeyViolation'),
(N'exception.usp_Exception_InvalidObjectName'), (N'exception.usp_Exception_UniqueViolation'),
(N'gap.usp_BulkInsert_Tvp'), (N'gap.usp_IsolationLevel_ReadUncommitted'),
(N'gap.usp_IsolationLevel_Serializable'), (N'gap.usp_Json_Insert'), (N'gap.usp_Json_Query'),
(N'gap.usp_Merge_Upsert'), (N'gap.usp_Paginate'), (N'gap.usp_WindowFunction_RankUsers'),
(N'perf.usp_Perf_Bulk_Insert'), (N'perf.usp_Perf_Query_With_Param'),
(N'resilience.usp_Resilience_Simulate_Delay'), (N'resilience.usp_Resilience_Simulate_Failure'),
(N'test.usp_Composite_InsertAndValidate'), (N'test.usp_Composite_V2'), (N'test.usp_Core_Get_Empty'),
(N'test.usp_Core_Get_NullScalar'), (N'test.usp_Deadlock_TableA'), (N'test.usp_Deadlock_TableB'),
(N'test.usp_Error_Custom_50001'), (N'test.usp_Error_NotNull_Violation'), (N'test.usp_Error_TryCatch_Rollback'),
(N'test.usp_Exception_QuerySyntax'), (N'test.usp_Output_With_Error'), (N'test.usp_RaiseError_Unknown_999'),
(N'test.usp_Savepoint_PartialCommit'), (N'test.usp_Simulate_TransactionAborted'),
(N'test.usp_Status_Branch_Logic'), (N'tvp.usp_Tvp_Bulk_Insert_AllTypes'), (N'tvp.usp_Tvp_Get_AllTypes'),
(N'tvp.usp_Tvp_Test_Schema_Mismatch'), (N'verify.usp_GetGeneratedRows'), (N'verify.usp_GetSuspendRows'),
(N'verify.usp_Verification_UpsertOrders');

INSERT INTO @Failures ([Scope], [Detail])
SELECT N'procedure-exists', CONCAT(N'Missing procedure: ', e.[Name])
FROM @ExpectedProcedures e
WHERE OBJECT_ID(QUOTENAME(PARSENAME(e.[Name], 2)) + N'.' + QUOTENAME(PARSENAME(e.[Name], 1)), N'P') IS NULL;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.parameters
    WHERE object_id = OBJECT_ID(N'dbo.libdb_bench_InsertOrderItems')
      AND name = N'@Rows'
      AND user_type_id = TYPE_ID(N'dbo.libdb_bench_OrderItem')
      AND is_readonly = 1
)
    INSERT INTO @Failures VALUES (N'tvp-param-shape', N'dbo.libdb_bench_InsertOrderItems @Rows is not READONLY dbo.libdb_bench_OrderItem.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.table_types AS tt
    JOIN sys.columns AS c ON c.object_id = tt.type_table_object_id
    WHERE tt.user_type_id = TYPE_ID(N'tvp.Tvp_Tvp_AllTypes')
      AND c.name = N'DecimalValue'
      AND c.precision = 18
      AND c.scale = 4
)
    INSERT INTO @Failures VALUES (N'tvp-column-shape', N'tvp.Tvp_Tvp_AllTypes.DecimalValue must be decimal(18,4).');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[verify].[VerificationOrderHeaders]', N'U')
      AND name = N'IX_verify_VerificationOrderHeaders_OpenStatus'
      AND has_filter = 1
)
    INSERT INTO @Failures VALUES (N'representative-index-shape', N'verify.VerificationOrderHeaders must expose the filtered open-status index.');

IF
(
    SELECT COUNT(*)
    FROM sys.parameters
    WHERE object_id = OBJECT_ID(N'[verify].[usp_Verification_UpsertOrders]', N'P')
      AND user_type_id IN (TYPE_ID(N'verify.Tvp_VerificationOrderHeader'), TYPE_ID(N'verify.Tvp_VerificationOrderLine'))
      AND is_readonly = 1
) <> 2
    INSERT INTO @Failures VALUES (N'representative-tvp-param-shape', N'verify.usp_Verification_UpsertOrders must expose two READONLY TVP parameters.');

IF
(
    SELECT COUNT(*)
    FROM sys.parameters
    WHERE object_id = OBJECT_ID(N'[verify].[usp_Verification_UpsertOrders]', N'P')
      AND is_output = 1
) <> 2
    INSERT INTO @Failures VALUES (N'representative-output-param-shape', N'verify.usp_Verification_UpsertOrders must expose two output parameters.');

IF
(
    SELECT COUNT(*)
    FROM sys.indexes AS i
    JOIN sys.table_types AS tt ON tt.type_table_object_id = i.object_id
    WHERE tt.user_type_id IN (TYPE_ID(N'verify.Tvp_VerificationOrderHeader'), TYPE_ID(N'verify.Tvp_VerificationOrderLine'))
      AND i.index_id > 0
) < 3
    INSERT INTO @Failures VALUES (N'representative-tvp-index-shape', N'Representative TVP types must expose clustered/unique key indexes.');

IF
(
    SELECT COUNT(*)
    FROM sys.check_constraints AS ck
    JOIN sys.table_types AS tt ON tt.type_table_object_id = ck.parent_object_id
    WHERE tt.user_type_id IN (TYPE_ID(N'verify.Tvp_VerificationOrderHeader'), TYPE_ID(N'verify.Tvp_VerificationOrderLine'))
) < 4
    INSERT INTO @Failures VALUES (N'representative-tvp-check-shape', N'Representative TVP types must expose validation CHECK constraints.');

DECLARE @NewUserId INT;
DECLARE @Email NVARCHAR(255) = CONCAT(N'verify-', CONVERT(NVARCHAR(36), NEWID()), N'@example.test');
DECLARE @UserInsert TABLE ([NewUserId] INT);
INSERT INTO @UserInsert EXEC [core].[usp_Core_Insert_User] @UserName = N'Verify User', @Email = @Email, @Age = 31;
SELECT @NewUserId = [NewUserId] FROM @UserInsert;
IF @NewUserId IS NULL INSERT INTO @Failures VALUES (N'core-smoke', N'core.usp_Core_Insert_User did not return NewUserId.');

DECLARE @Dashboard TABLE
(
    [UserId] INT,
    [UserName] NVARCHAR(100),
    [Email] NVARCHAR(255),
    [Age] INT NULL,
    [CreatedAt] DATETIME2(7)
);
INSERT INTO @Dashboard EXEC [core].[usp_Core_Get_User] @UserId = @NewUserId;
IF NOT EXISTS (SELECT 1 FROM @Dashboard WHERE [UserId] = @NewUserId)
    INSERT INTO @Failures VALUES (N'core-smoke', N'core.usp_Core_Get_User did not return inserted user.');

DECLARE @Out INT, @InOut INT = 7;
EXEC [adv].[usp_Adv_OutputParameters] @InputVal = 5, @OutputVal = @Out OUTPUT, @InOutVal = @InOut OUTPUT;
IF @Out <> 10 OR @InOut <> 12 INSERT INTO @Failures VALUES (N'output-param-smoke', N'adv output parameters returned unexpected values.');

DECLARE @Users [core].[Tvp_Core_User];
INSERT INTO @Users ([UserName], [Email], [Age]) VALUES (N'TVP User', CONCAT(N'tvp-', CONVERT(NVARCHAR(36), NEWID()), N'@example.test'), 22);
DECLARE @CoreTvpResult TABLE ([RowsAffected] INT);
INSERT INTO @CoreTvpResult EXEC [core].[usp_Core_Bulk_Insert_Users] @Users = @Users;
IF NOT EXISTS (SELECT 1 FROM @CoreTvpResult WHERE [RowsAffected] = 1)
    INSERT INTO @Failures VALUES (N'core-tvp-smoke', N'core TVP insert did not affect one row.');

DECLARE @AllTypes [tvp].[Tvp_Tvp_AllTypes];
INSERT INTO @AllTypes ([DateOnlyValue], [TimeOnlyValue], [HalfValue], [GuidValue], [DecimalValue])
VALUES (CONVERT(DATE, '2026-05-18'), CONVERT(TIME, '12:34:56'), CONVERT(REAL, 1.5), NEWID(), CONVERT(DECIMAL(18,4), 12.3456));
DECLARE @TvpResult TABLE ([RowsAffected] INT);
INSERT INTO @TvpResult EXEC [tvp].[usp_Tvp_Bulk_Insert_AllTypes] @Items = @AllTypes;
IF NOT EXISTS (SELECT 1 FROM @TvpResult WHERE [RowsAffected] = 1)
    INSERT INTO @Failures VALUES (N'tvp-smoke', N'tvp all-types TVP did not affect one row.');

DECLARE @BenchRows [dbo].[libdb_bench_OrderItem];
INSERT INTO @BenchRows ([Id], [Sku], [Qty], [Price]) VALUES (1, N'VERIFY-SKU', 2, 10.50);
DECLARE @BenchResult TABLE ([InsertedCount] BIGINT);
INSERT INTO @BenchResult EXEC [dbo].[libdb_bench_InsertOrderItems] @OrderId = 230, @RequestedBy = N'verify', @Rows = @BenchRows;
IF NOT EXISTS (SELECT 1 FROM @BenchResult WHERE [InsertedCount] >= 1)
    INSERT INTO @Failures VALUES (N'runtime-tvp-smoke', N'dbo.libdb_bench_InsertOrderItems did not insert rows.');

DECLARE @GapRows [gap].[Tvp_BulkTarget];
INSERT INTO @GapRows ([Data], [BatchId]) VALUES (N'gap verify', 230);
DECLARE @GapResult TABLE ([RowsAffected] INT);
INSERT INTO @GapResult EXEC [gap].[usp_BulkInsert_Tvp] @Items = @GapRows;
IF NOT EXISTS (SELECT 1 FROM @GapResult WHERE [RowsAffected] = 1)
    INSERT INTO @Failures VALUES (N'gap-tvp-smoke', N'gap TVP insert did not affect one row.');

DECLARE @RepTenantId INT = 230000 + ABS(CHECKSUM(NEWID())) % 100000;
DECLARE @RepExternalOrderId NVARCHAR(64) = CONCAT(N'VERIFY-', CONVERT(NVARCHAR(36), NEWID()));
DECLARE @RepCorrelationId UNIQUEIDENTIFIER = NEWID();
DECLARE @RepHeaders [verify].[Tvp_VerificationOrderHeader];
DECLARE @RepLines [verify].[Tvp_VerificationOrderLine];
INSERT INTO @RepHeaders
(
    [RowNo], [ExternalOrderId], [CustomerCode], [SegmentCode],
    [OrderStatus], [SubmittedAt], [TotalAmount], [MetadataJson]
)
VALUES
(
    1, @RepExternalOrderId, N'CUST-230', N'VIP',
    N'N', SYSUTCDATETIME(), 129.9900, N'{"source":"verify-script","priority":3}'
);
INSERT INTO @RepLines ([RowNo], [LineNo], [ProductCode], [Quantity], [UnitPrice], [DiscountAmount], [TaxRate])
VALUES
(1, 1, N'SKU-RED', 2, 29.9900, 0.0000, 0.100000),
(1, 2, N'SKU-BLUE', 1, 70.0100, 0.0000, 0.100000);

DECLARE @RepInsertedOrders INT;
DECLARE @RepInsertedLines INT;
DECLARE @RepResult TABLE ([InsertedOrders] INT, [InsertedLines] INT, [OpenOrderCount] INT);
INSERT INTO @RepResult
EXEC [verify].[usp_Verification_UpsertOrders]
    @TenantId = @RepTenantId,
    @RequestedBy = N'verify-script',
    @CorrelationId = @RepCorrelationId,
    @Headers = @RepHeaders,
    @Lines = @RepLines,
    @InsertedOrders = @RepInsertedOrders OUTPUT,
    @InsertedLines = @RepInsertedLines OUTPUT;

IF @RepInsertedOrders <> 1 OR @RepInsertedLines <> 2
    INSERT INTO @Failures VALUES (N'representative-tvp-smoke', N'Representative mixed scalar + multi-TVP procedure returned unexpected output counts.');
IF NOT EXISTS (SELECT 1 FROM @RepResult WHERE [InsertedOrders] = 1 AND [InsertedLines] = 2 AND [OpenOrderCount] >= 1)
    INSERT INTO @Failures VALUES (N'representative-tvp-smoke', N'Representative mixed scalar + multi-TVP procedure returned an unexpected result set.');

DECLARE @JsonResult TABLE ([NewId] INT);
INSERT INTO @JsonResult EXEC [gap].[usp_Json_Insert] @JsonPayload = N'{"kind":"verify","value":230}';
IF NOT EXISTS (SELECT 1 FROM @JsonResult WHERE [NewId] IS NOT NULL)
    INSERT INTO @Failures VALUES (N'json-smoke', N'gap.usp_Json_Insert did not return NewId.');
IF JSON_VALUE(N'{"kind":"verify"}', N'$.kind') <> N'verify'
    INSERT INTO @Failures VALUES (N'json-smoke', N'JSON_VALUE returned unexpected result.');

DECLARE @Merge TABLE ([MergeAction] NVARCHAR(10));
INSERT INTO @Merge EXEC [gap].[usp_Merge_Upsert] @Id = 230, @Name = N'Verify Merge';
IF NOT EXISTS (SELECT 1 FROM @Merge WHERE [MergeAction] IN (N'INSERT', N'UPDATE'))
    INSERT INTO @Failures VALUES (N'merge-smoke', N'MERGE upsert did not return action.');

DECLARE @Status NVARCHAR(20);
EXEC [test].[usp_Status_Branch_Logic] @UserId = @NewUserId, @Status = @Status OUTPUT;
IF @Status NOT IN (N'NEW', N'ACTIVE', N'VIP')
    INSERT INTO @Failures VALUES (N'branch-smoke', N'Status branch returned unexpected value.');

BEGIN TRY
    EXEC [exception].[usp_Exception_DivideByZero];
    INSERT INTO @Failures VALUES (N'expected-error', N'DivideByZero procedure did not fail.');
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 8134 INSERT INTO @Failures VALUES (N'expected-error', CONCAT(N'Unexpected divide-by-zero error number: ', ERROR_NUMBER()));
END CATCH;

IF CAST(DATABASEPROPERTYEX(DB_NAME(), 'IsQueryStoreOn') AS INT) <> 1
    INSERT INTO @Failures VALUES (N'query-store', N'Query Store must be enabled for verification workload analysis.');

IF EXISTS (SELECT 1 FROM @Failures)
BEGIN
    SELECT [Scope], [Detail] FROM @Failures ORDER BY [Scope], [Detail];
    DECLARE @FailureDetails NVARCHAR(1600) =
    (
        SELECT STRING_AGG(CONCAT([Scope], N': ', [Detail]), N'; ') WITHIN GROUP (ORDER BY [Scope], [Detail])
        FROM (SELECT TOP (5) [Scope], [Detail] FROM @Failures ORDER BY [Scope], [Detail]) AS f
    );
    DECLARE @Message NVARCHAR(2048) = CONCAT(
        N'LIBDB_VERIFICATION_TEST verification failed: ',
        (SELECT COUNT(*) FROM @Failures),
        N' issue(s). ',
        COALESCE(@FailureDetails, N'')
    );
    THROW 51000, @Message, 1;
END;

SELECT N'LIBDB_VERIFICATION_TEST verification passed.' AS [Result],
       (SELECT COUNT(*) FROM @ExpectedTables) AS [ExpectedTables],
       (SELECT COUNT(*) FROM @ExpectedTypes) AS [ExpectedTvpTypes],
       (SELECT COUNT(*) FROM @ExpectedProcedures) AS [ExpectedProcedures];
GO
