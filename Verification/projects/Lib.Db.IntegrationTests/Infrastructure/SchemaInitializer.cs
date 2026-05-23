// ============================================================================
// 파일: Infrastructure/SchemaInitializer.cs
// 설명: LIBDB_VERIFICATION_TEST DB의 모든 스키마/SP/테이블/TVP를 멱등하게 생성하는 초기화 유틸
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// 테스트 DB 스키마 초기화를 담당하는 정적 유틸리티 클래스.
/// <para><b>[설계 의도]</b> 모든 CREATE OR ALTER 구문으로 멱등성을 보장하여,
/// DB에 이미 객체가 존재해도 안전하게 재생성한다.</para>
/// </summary>
internal static class SchemaInitializer
{
    #region 통합 초기화 (EnsureAllSchemasAsync)

    /// <summary>
    /// 기존 TestDatabaseFixture의 모든 스키마 + [test] 스키마를 통합 생성한다.
    /// <para>호출 순서: 스키마 생성 -> 테이블 생성 -> TVP 타입 생성 -> SP 생성</para>
    /// </summary>
    /// <param name="db">Verification DB에 연결된 <see cref="IProcedureStage"/></param>
    public static async Task EnsureAllSchemasAsync(IProcedureStage db)
    {
        // 1. [core] 스키마
        await EnsureCoreSchemaAsync(db).ConfigureAwait(false);

        // 2. [adv] 스키마
        await EnsureAdvSchemaAsync(db).ConfigureAwait(false);

        // 3. [exception] 스키마
        await EnsureExceptionSchemaAsync(db).ConfigureAwait(false);

        // 4. [perf] 스키마
        await EnsurePerfSchemaAsync(db).ConfigureAwait(false);

        // 5. [tvp] 스키마
        await EnsureTvpSchemaAsync(db).ConfigureAwait(false);

        // 6. [resilience] 스키마
        await EnsureResilienceSchemaAsync(db).ConfigureAwait(false);

        // 7. [test] 스키마 (추가 SP 15개 + 테이블 2개)
        await EnsureTestSchemaAsync(db).ConfigureAwait(false);

        // 8. [gap] 스키마 (기능 완전성 검증)
        await EnsureGapSchemaAsync(db).ConfigureAwait(false);

        // 9. [verify] 스키마 (v2.2.1 차단 이슈 회귀 검증)
        await EnsureV221BlockerVerificationSchemaAsync(db).ConfigureAwait(false);

        // 10. [verify] 대표 복잡도/TVP shape 검증 객체
        await EnsureRepresentativeVerificationSchemaAsync(db).ConfigureAwait(false);

        // 11. v2.3 runtime TVP 검증/벤치마크용 제한 prefix 객체
        await EnsureRuntimeTvpBenchSchemaAsync(db).ConfigureAwait(false);

        // 12. dbo 유틸리티 SP
        await EnsureDboUtilityAsync(db).ConfigureAwait(false);
    }

    #endregion

    #region Sorter 테스트 스키마

    /// <summary>
    /// Sorter 테스트가 기대하는 조회 테이블과 로그/흐름 SP를 멱등 생성한다.
    /// </summary>
    /// <param name="db">Sorter DB에 연결된 <see cref="IProcedureStage"/></param>
    public static async Task EnsureSorterSchemaAsync(IProcedureStage db)
    {
        await db.Sql("""
            IF OBJECT_ID('[dbo].[IF_CHUTE_INFO]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[IF_CHUTE_INFO] (
                    CHUTE_NO INT NOT NULL PRIMARY KEY,
                    CHUTE_NAME NVARCHAR(100) NOT NULL,
                    STATUS NVARCHAR(20) NOT NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM [dbo].[IF_CHUTE_INFO])
            BEGIN
                INSERT INTO [dbo].[IF_CHUTE_INFO] (CHUTE_NO, CHUTE_NAME, STATUS)
                VALUES (1, N'CHUTE-001', N'OPEN'), (2, N'CHUTE-002', N'CLOSED');
            END;

            IF OBJECT_ID('[dbo].[IF_BRAND_MASTER]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[IF_BRAND_MASTER] (
                    BRAND_CD NVARCHAR(20) NOT NULL PRIMARY KEY,
                    BRAND_NM NVARCHAR(100) NOT NULL
                );
            END;

            IF (SELECT COUNT(*) FROM [dbo].[IF_BRAND_MASTER]) <> 7
            BEGIN
                DELETE FROM [dbo].[IF_BRAND_MASTER];
                INSERT INTO [dbo].[IF_BRAND_MASTER] (BRAND_CD, BRAND_NM)
                VALUES
                    (N'B01', N'Brand 01'), (N'B02', N'Brand 02'),
                    (N'B03', N'Brand 03'), (N'B04', N'Brand 04'),
                    (N'B05', N'Brand 05'), (N'B06', N'Brand 06'),
                    (N'B07', N'Brand 07');
            END;

            IF OBJECT_ID('[dbo].[IF_BOX_LIST]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[IF_BOX_LIST] (
                    BOX_ID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    BIZ_DAY CHAR(8) NOT NULL,
                    BOX_NO NVARCHAR(50) NOT NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM [dbo].[IF_BOX_LIST] WHERE BIZ_DAY = '20260309')
            BEGIN
                INSERT INTO [dbo].[IF_BOX_LIST] (BIZ_DAY, BOX_NO)
                VALUES ('20260309', N'BOX-001'), ('20260309', N'BOX-002');
            END;

            IF OBJECT_ID('[dbo].[USR_INFO]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[USR_INFO] (
                    USER_ID NVARCHAR(50) NOT NULL PRIMARY KEY,
                    USER_NM NVARCHAR(100) NOT NULL
                );
            END;

            IF (SELECT COUNT(*) FROM [dbo].[USR_INFO]) <> 3
            BEGIN
                DELETE FROM [dbo].[USR_INFO];
                INSERT INTO [dbo].[USR_INFO] (USER_ID, USER_NM)
                VALUES (N'u01', N'User 01'), (N'u02', N'User 02'), (N'u03', N'User 03');
            END;

            IF OBJECT_ID('[dbo].[MENU_INFO]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[MENU_INFO] (
                    MENU_ID INT NOT NULL PRIMARY KEY,
                    MENU_NM NVARCHAR(100) NOT NULL
                );
            END;

            IF (SELECT COUNT(*) FROM [dbo].[MENU_INFO]) <> 18
            BEGIN
                DELETE FROM [dbo].[MENU_INFO];
                INSERT INTO [dbo].[MENU_INFO] (MENU_ID, MENU_NM)
                SELECT v.Id, CONCAT(N'Menu ', FORMAT(v.Id, '00'))
                FROM (VALUES
                    (1), (2), (3), (4), (5), (6),
                    (7), (8), (9), (10), (11), (12),
                    (13), (14), (15), (16), (17), (18)
                ) AS v(Id);
            END;

            IF OBJECT_ID('[dbo].[TS_TILT_LOG]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TS_TILT_LOG] (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    PLC_SEQ INT NOT NULL,
                    CHUTE_NO INT NOT NULL,
                    TRAY_NO INT NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                );
            END;

            IF OBJECT_ID('[dbo].[TS_CHUTE_BTN_LOG]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TS_CHUTE_BTN_LOG] (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    CHUTE_NO NVARCHAR(20) NOT NULL,
                    STATUS NVARCHAR(20) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                );
            END;

            IF OBJECT_ID('[dbo].[TS_EMR_LOG]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TS_EMR_LOG] (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    EMR_NO INT NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                );
            END;

            IF OBJECT_ID('[dbo].[TS_ERROR_LOG]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TS_ERROR_LOG] (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    CLASS INT NOT NULL,
                    COMPUTER NVARCHAR(100) NOT NULL,
                    EVENT_ID INT NOT NULL,
                    MSG NVARCHAR(4000) NOT NULL,
                    MUSTCON NVARCHAR(10) NOT NULL,
                    STATE INT NOT NULL,
                    SOURCE NVARCHAR(100) NOT NULL,
                    PLCSEQ BIGINT NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                );
            END;

            IF OBJECT_ID('[dbo].[TS_TRAY_FLOW]', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TS_TRAY_FLOW] (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    EventName NVARCHAR(50) NOT NULL,
                    Payload NVARCHAR(4000) NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                );
            END;
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[IF_SP_TILT_LOG]
                @V_PLC_SEQ INT,
                @V_CHUTE_NO INT,
                @V_TRAY_NO INT
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [dbo].[TS_TILT_LOG] (PLC_SEQ, CHUTE_NO, TRAY_NO)
                VALUES (@V_PLC_SEQ, @V_CHUTE_NO, @V_TRAY_NO);
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[IF_SP_CHUTE_BTN_LOG]
                @V_CHUTE_NO NVARCHAR(20),
                @V_STATUS NVARCHAR(20)
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [dbo].[TS_CHUTE_BTN_LOG] (CHUTE_NO, STATUS)
                VALUES (@V_CHUTE_NO, @V_STATUS);
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[IF_SP_EMR_LOG]
                @V_EMR_NO INT
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [dbo].[TS_EMR_LOG] (EMR_NO) VALUES (@V_EMR_NO);
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
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
                INSERT INTO [dbo].[TS_ERROR_LOG] (CLASS, COMPUTER, EVENT_ID, MSG, MUSTCON, STATE, SOURCE, PLCSEQ)
                VALUES (@V_CLASS, @V_COMPUTER, @V_EVENT_ID, @V_MSG, @V_MUSTCON, @V_STATE, @V_SOURCE, @V_PLCSEQ);
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[IF_SP_TRAY_IN]
                @V_INDUCTION INT,
                @V_TRAY_NO INT,
                @V_BARCODE NVARCHAR(100),
                @V_DELIVERY NVARCHAR(20)
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [dbo].[TS_TRAY_FLOW] (EventName, Payload)
                VALUES (N'TRAY_IN', CONCAT(@V_INDUCTION, N'|', @V_TRAY_NO, N'|', @V_BARCODE, N'|', @V_DELIVERY));
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
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

                INSERT INTO [dbo].[TS_TRAY_FLOW] (EventName, Payload)
                VALUES (N'BARCODE', CONCAT(@SCAN_SEQ, N'|', @V_INDUCTION, N'|', @V_DELIVERY, N'|', @V_INVOICE, N'|', @V_BARCODE, N'|', @V_INPUT_STATUS));
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[IF_SP_DAS_SELECT]
                @V_BIZ_DAY CHAR(8),
                @V_DISP_YN NVARCHAR(1)
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [dbo].[TS_TRAY_FLOW] (EventName, Payload)
                VALUES (N'DAS_SELECT', CONCAT(@V_BIZ_DAY, N'|', @V_DISP_YN));
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[IF_SP_TILT_STOP]
                @V_CHUTE_NO INT,
                @V_BOXYN NVARCHAR(1)
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [dbo].[TS_TRAY_FLOW] (EventName, Payload)
                VALUES (N'TILT_STOP', CONCAT(@V_CHUTE_NO, N'|', @V_BOXYN));
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region v2.3 Runtime TVP benchmark-safe objects

    private static async Task EnsureRuntimeTvpBenchSchemaAsync(IProcedureStage db)
    {
        await db.Sql("""
            IF TYPE_ID(N'[dbo].[libdb_aot_OrderItem]') IS NULL
            BEGIN
                CREATE TYPE [dbo].[libdb_aot_OrderItem] AS TABLE
                (
                    [Id] INT NOT NULL,
                    [Sku] NVARCHAR(64) NOT NULL,
                    [Qty] INT NOT NULL
                );
            END;

            IF OBJECT_ID('[dbo].[libdb_aot_OrderItems]', 'U') IS NULL
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
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [dbo].[libdb_aot_InsertOrderItems]
                @OrderId INT,
                @RequestedBy NVARCHAR(64),
                @Rows [dbo].[libdb_aot_OrderItem] READONLY
            AS
            BEGIN
                SET NOCOUNT ON;

                INSERT INTO [dbo].[libdb_aot_OrderItems] ([OrderId], [RequestedBy], [Id], [Sku], [Qty])
                SELECT @OrderId, @RequestedBy, [Id], [Sku], [Qty]
                FROM @Rows;

                SELECT COUNT_BIG(*) AS [InsertedCount]
                FROM [dbo].[libdb_aot_OrderItems]
                WHERE [OrderId] = @OrderId;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[dbo].[libdb_bench_InsertOrderItems]', 'P') IS NOT NULL
                DROP PROCEDURE [dbo].[libdb_bench_InsertOrderItems];

            IF OBJECT_ID('[dbo].[libdb_bench_OrderItems]', 'U') IS NOT NULL
                DROP TABLE [dbo].[libdb_bench_OrderItems];

            IF TYPE_ID(N'[dbo].[libdb_bench_OrderItem]') IS NOT NULL
                DROP TYPE [dbo].[libdb_bench_OrderItem];

            CREATE TYPE [dbo].[libdb_bench_OrderItem] AS TABLE
            (
                [Id] INT NOT NULL,
                [Sku] NVARCHAR(64) NOT NULL,
                [Qty] INT NOT NULL,
                [Price] DECIMAL(18, 2) NOT NULL
            );

            CREATE TABLE [dbo].[libdb_bench_OrderItems]
            (
                [OrderId] INT NOT NULL,
                [RequestedBy] NVARCHAR(64) NOT NULL,
                [Id] INT NOT NULL,
                [Sku] NVARCHAR(64) NOT NULL,
                [Qty] INT NOT NULL,
                [Price] DECIMAL(18, 2) NOT NULL,
                [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_libdb_bench_OrderItems_CreatedAt] DEFAULT SYSUTCDATETIME()
            );
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
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
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [test] 스키마 (추가 SP 15개 + 테이블 2개)

    /// <summary>
    /// [test] 스키마 + SP 15개 + 테이블 2개를 CREATE OR ALTER로 생성한다.
    /// </summary>
    /// <param name="db">Verification DB에 연결된 <see cref="IProcedureStage"/></param>
    public static async Task EnsureTestSchemaAsync(IProcedureStage db)
    {
        // 스키마 생성
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'test')
                EXEC('CREATE SCHEMA [test]')
            """).ExecuteAsync().ConfigureAwait(false);

        // 1. usp_Error_Custom_50001 -- 주문 검증 실패 (재호출 가능)
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Error_Custom_50001]
                @OrderId INT,
                @Action NVARCHAR(20)
            AS
            BEGIN
                SET NOCOUNT ON;
                IF @Action = 'VALIDATE'
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM core.Orders WHERE OrderId = @OrderId)
                        THROW 50001, N'주문을 찾을 수 없습니다. 재시도하거나 새 주문을 생성하세요.', 1;
                    SELECT OrderId, UserId, Quantity FROM core.Orders WHERE OrderId = @OrderId;
                END
                ELSE IF @Action = 'RETRY'
                BEGIN
                    THROW 50002, N'재시도 횟수를 초과했습니다. 관리자에게 문의하세요.', 1;
                END
                ELSE
                    THROW 50003, N'알 수 없는 액션입니다.', 1;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 2. usp_Error_TryCatch_Rollback -- TRY-CATCH + 트랜잭션 롤백
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Error_TryCatch_Rollback]
                @ShouldFail BIT
            AS
            BEGIN
                SET NOCOUNT ON;
                BEGIN TRY
                    BEGIN TRANSACTION;
                    INSERT INTO core.Users (UserName, Email)
                    VALUES ('TxTest_' + CAST(NEWID() AS NVARCHAR(36)), 'txtest@test.com');
                    IF @ShouldFail = 1
                        THROW 50010, N'의도적 실패: 트랜잭션이 롤백됩니다.', 1;
                    COMMIT TRANSACTION;
                    SELECT 'COMMITTED' AS Result;
                END TRY
                BEGIN CATCH
                    IF XACT_STATE() <> 0
                        ROLLBACK TRANSACTION;
                    THROW;
                END CATCH
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 3. usp_Status_Branch_Logic -- 상태 분기 로직
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Status_Branch_Logic]
                @UserId INT,
                @Status NVARCHAR(20) OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @OrderCount INT;
                SELECT @OrderCount = COUNT(*) FROM core.Orders WHERE UserId = @UserId;

                IF @OrderCount = 0
                BEGIN
                    SET @Status = 'NEW';
                    SELECT @UserId AS UserId, @Status AS Status, 0 AS OrderCount;
                END
                ELSE IF @OrderCount < 5
                BEGIN
                    SET @Status = 'ACTIVE';
                    SELECT @UserId AS UserId, @Status AS Status, @OrderCount AS OrderCount;
                END
                ELSE
                BEGIN
                    SET @Status = 'VIP';
                    SELECT @UserId AS UserId, @Status AS Status, @OrderCount AS OrderCount;
                END
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 4. usp_Composite_InsertAndValidate -- SP->SP 호출 조합
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Composite_InsertAndValidate]
                @UserName NVARCHAR(100),
                @Email NVARCHAR(255),
                @NewUserId INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                EXEC core.usp_Core_Insert_User @UserName = @UserName, @Email = @Email;
                SET @NewUserId = SCOPE_IDENTITY();
                EXEC core.usp_Core_Get_User @UserId = @NewUserId;
                IF @NewUserId IS NULL
                    THROW 50020, N'사용자 삽입 후 검증 실패', 1;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 5. usp_Output_With_Error -- OUTPUT + 에러 조건
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Output_With_Error]
                @InputId INT,
                @OutputName NVARCHAR(100) OUTPUT,
                @OutputAge INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT @OutputName = UserName, @OutputAge = Age
                  FROM core.Users WHERE UserId = @InputId;
                IF @OutputName IS NULL
                    THROW 50030, N'사용자를 찾을 수 없습니다.', 1;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 6. usp_Exception_QuerySyntax -- 구문 오류 (동적 SQL)
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Exception_QuerySyntax]
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @sql NVARCHAR(200) = N'SELECTX * FROM core.Users';
                EXEC sp_executesql @sql;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 7. usp_Core_Get_NullScalar -- NULL 스칼라 반환
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Core_Get_NullScalar]
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT NULL AS NullValue;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 8. usp_Core_Get_Empty -- 빈 결과셋
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Core_Get_Empty]
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT UserId, UserName, Email FROM core.Users WHERE 1 = 0;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 9. usp_Error_NotNull_Violation -- NOT NULL 제약 위반
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Error_NotNull_Violation]
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO core.Users (UserName, Email) VALUES (NULL, 'test@test.com');
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 10-1. Deadlock 테스트용 테이블 2개
        await db.Sql("""
            IF OBJECT_ID('[test].[DeadlockA]', 'U') IS NULL
                CREATE TABLE [test].[DeadlockA] (Id INT PRIMARY KEY, Val INT);
            IF OBJECT_ID('[test].[DeadlockB]', 'U') IS NULL
                CREATE TABLE [test].[DeadlockB] (Id INT PRIMARY KEY, Val INT);
            """).ExecuteAsync().ConfigureAwait(false);

        // Deadlock 시드 데이터 (멱등)
        await db.Sql("""
            IF NOT EXISTS (SELECT 1 FROM [test].[DeadlockA] WHERE Id = 1)
                INSERT INTO [test].[DeadlockA] VALUES (1, 100);
            IF NOT EXISTS (SELECT 1 FROM [test].[DeadlockB] WHERE Id = 1)
                INSERT INTO [test].[DeadlockB] VALUES (1, 200);
            """).ExecuteAsync().ConfigureAwait(false);

        // 10-2. usp_Deadlock_TableA
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Deadlock_TableA]
            AS
            BEGIN
                SET NOCOUNT ON;
                BEGIN TRANSACTION;
                UPDATE [test].[DeadlockA] SET Val = Val + 1 WHERE Id = 1;
                WAITFOR DELAY '00:00:02';
                UPDATE [test].[DeadlockB] SET Val = Val + 1 WHERE Id = 1;
                COMMIT TRANSACTION;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 10-3. usp_Deadlock_TableB
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Deadlock_TableB]
            AS
            BEGIN
                SET NOCOUNT ON;
                BEGIN TRANSACTION;
                UPDATE [test].[DeadlockB] SET Val = Val + 1 WHERE Id = 1;
                WAITFOR DELAY '00:00:02';
                UPDATE [test].[DeadlockA] SET Val = Val + 1 WHERE Id = 1;
                COMMIT TRANSACTION;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 12. usp_Simulate_TransactionAborted — XACT_ABORT ON + NOT NULL 위반 → DOOMED 트랜잭션
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Simulate_TransactionAborted]
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                BEGIN TRANSACTION;

                -- NOT NULL 위반으로 에러 발생 → XACT_ABORT에 의해 트랜잭션 DOOMED
                INSERT INTO [core].[Users] (UserName, Email)
                VALUES (NULL, 'txabort_test@test.com');

                -- 여기 도달 불가 (XACT_ABORT가 즉시 중단)
                COMMIT TRANSACTION;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 13. usp_RaiseError_Unknown_999 — RAISERROR로 매핑 안 된 에러 발생
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_RaiseError_Unknown_999]
            AS
            BEGIN
                SET NOCOUNT ON;
                RAISERROR(N'매핑되지 않은 에러 코드 999 테스트', 16, 1);
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 14. usp_Savepoint_PartialCommit — 세이브포인트 부분 커밋 (A 유지, B 롤백)
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Savepoint_PartialCommit]
                @EmailA NVARCHAR(255),
                @EmailB NVARCHAR(255)
            AS
            BEGIN
                SET NOCOUNT ON;

                BEGIN TRANSACTION;

                -- 1단계: User A 삽입 (유지됨)
                INSERT INTO [core].[Users] (UserName, Email, Age)
                VALUES ('SavepointA', @EmailA, 10);

                -- 세이브포인트 설정
                SAVE TRANSACTION SP_AfterA;

                -- 2단계: User B 삽입 (롤백됨)
                INSERT INTO [core].[Users] (UserName, Email, Age)
                VALUES ('SavepointB', @EmailB, 20);

                -- 세이브포인트로 롤백 → B만 취소
                ROLLBACK TRANSACTION SP_AfterA;

                -- 전체 커밋 → A만 영속
                COMMIT TRANSACTION;

                SELECT 'PARTIAL_COMMIT' AS Result;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // 15. usp_Composite_V2 — SP→SP V2, OUTPUT 절로 INSERT된 UserId 직접 캡처
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [test].[usp_Composite_V2]
                @UserName NVARCHAR(100),
                @Email NVARCHAR(255),
                @NewUserId INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;

                -- OUTPUT 절로 INSERT된 ID 직접 캡처 (SCOPE_IDENTITY 범위 문제 우회)
                DECLARE @InsertedIds TABLE (UserId INT);

                INSERT INTO [core].[Users] (UserName, Email)
                OUTPUT INSERTED.UserId INTO @InsertedIds
                VALUES (@UserName, @Email);

                SELECT @NewUserId = UserId FROM @InsertedIds;

                -- 삽입된 사용자 정보 반환
                SELECT UserId, UserName, Email, Age, CreatedAt
                FROM [core].[Users]
                WHERE UserId = @NewUserId;
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region 기본 시드 데이터 (SeedBaseDataAsync)

    /// <summary>
    /// 기본 시드 데이터를 삽입한다 (Alice, Bob, Charlie + Products 3개).
    /// <para>중복 방지를 위해 IF NOT EXISTS 패턴을 사용한다.</para>
    /// </summary>
    /// <param name="db">Verification DB에 연결된 <see cref="IProcedureStage"/></param>
    public static async Task SeedBaseDataAsync(IProcedureStage db)
    {
        try
        {
            await db.Sql("""
                -- Users: Alice, Bob, Charlie
                IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE Email = 'alice@test.com')
                    INSERT INTO [core].[Users] (UserName, Email, Age)
                    VALUES ('Alice', 'alice@test.com', 28);

                IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE Email = 'bob@test.com')
                    INSERT INTO [core].[Users] (UserName, Email, Age)
                    VALUES ('Bob', 'bob@test.com', 35);

                IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE Email = 'charlie@test.com')
                    INSERT INTO [core].[Users] (UserName, Email, Age)
                    VALUES ('Charlie', 'charlie@test.com', 42);

                -- Products: 3개 상품
                IF NOT EXISTS (SELECT 1 FROM [core].[Products] WHERE ProductName = 'Product A')
                    INSERT INTO [core].[Products] (ProductName, Price, Stock)
                    VALUES ('Product A', 100.00, 50);

                IF NOT EXISTS (SELECT 1 FROM [core].[Products] WHERE ProductName = 'Product B')
                    INSERT INTO [core].[Products] (ProductName, Price, Stock)
                    VALUES ('Product B', 200.00, 30);

                IF NOT EXISTS (SELECT 1 FROM [core].[Products] WHERE ProductName = 'Product C')
                    INSERT INTO [core].[Products] (ProductName, Price, Stock)
                    VALUES ('Product C', 300.00, 20);
                """).ExecuteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Seed 데이터 삽입 실패: {ex.Message}");
        }
    }

    #endregion

    #region [core] 스키마

    /// <summary>
    /// [core] 스키마: 테이블 4개 (Users, Products, Orders, SearchIndex) + SP 6개를 생성한다.
    /// </summary>
    private static async Task EnsureCoreSchemaAsync(IProcedureStage db)
    {
        // 스키마 생성
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'core')
                EXEC('CREATE SCHEMA [core]')
            """).ExecuteAsync().ConfigureAwait(false);

        // Users 테이블
        await db.Sql("""
            IF OBJECT_ID('[core].[Users]', 'U') IS NULL
            BEGIN
                CREATE TABLE [core].[Users] (
                    UserId INT IDENTITY(1,1) PRIMARY KEY,
                    UserName NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(255) NOT NULL UNIQUE,
                    Age INT NULL,
                    CreatedAt DATETIME2 DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // Products 테이블
        await db.Sql("""
            IF OBJECT_ID('[core].[Products]', 'U') IS NULL
            BEGIN
                CREATE TABLE [core].[Products] (
                    ProductId INT IDENTITY(1,1) PRIMARY KEY,
                    ProductName NVARCHAR(200) NOT NULL,
                    Price DECIMAL(18,2) NOT NULL,
                    Stock INT NOT NULL DEFAULT 0,
                    CreatedAt DATETIME2 DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // Orders 테이블
        await db.Sql("""
            IF OBJECT_ID('[core].[Orders]', 'U') IS NULL
            BEGIN
                CREATE TABLE [core].[Orders] (
                    OrderId INT IDENTITY(1,1) PRIMARY KEY,
                    UserId INT NOT NULL,
                    ProductId INT NOT NULL DEFAULT 1,
                    Quantity INT NOT NULL DEFAULT 1,
                    TotalPrice DECIMAL(18,2) NOT NULL DEFAULT 0,
                    OrderDate DATETIME2 DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[core].[CursorState]', 'U') IS NULL
            BEGIN
                CREATE TABLE [core].[CursorState] (
                    [InstanceHash] VARCHAR(100) NOT NULL,
                    [QueryKey] VARCHAR(100) NOT NULL,
                    [CursorValue] NVARCHAR(MAX) NULL,
                    [UpdatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_core_CursorState_UpdatedAt] DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT [PK_core_CursorState] PRIMARY KEY ([InstanceHash], [QueryKey])
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // TVP 타입: Tvp_Core_User
        await db.Sql("""
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
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF TYPE_ID('[core].[Tvp_Core_User]') IS NULL
            BEGIN
                CREATE TYPE [core].[Tvp_Core_User] AS TABLE (
                    UserName NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(255) NOT NULL,
                    Age INT NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Core_Insert_User
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [core].[usp_Core_Insert_User]
                @UserName NVARCHAR(100),
                @Email NVARCHAR(255),
                @Age INT = NULL
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [core].[Users] (UserName, Email, Age)
                VALUES (@UserName, @Email, @Age);
                SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewUserId;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Core_Get_User
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [core].[usp_Core_Get_User]
                @UserId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT UserId, UserName, Email, Age, CreatedAt
                FROM [core].[Users]
                WHERE UserId = @UserId;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Core_Search_Users
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [core].[usp_Core_Search_Users]
                @SearchTerm NVARCHAR(100)
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT UserId, UserName, Email, Age, CreatedAt
                FROM [core].[Users]
                WHERE UserName LIKE '%' + @SearchTerm + '%'
                   OR Email LIKE '%' + @SearchTerm + '%';
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Core_Get_Dashboard
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [core].[usp_Core_Get_Dashboard]
                @UserId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                -- ResultSet 1: 사용자 정보
                SELECT UserId, UserName, Email
                FROM [core].[Users]
                WHERE UserId = @UserId;

                -- ResultSet 2: 주문 목록
                SELECT OrderId, ProductId, Quantity, TotalPrice, OrderDate
                FROM [core].[Orders]
                WHERE UserId = @UserId;

                -- ResultSet 3: 집계
                SELECT COUNT(*) AS TotalOrders, SUM(TotalPrice) AS TotalSpent
                FROM [core].[Orders]
                WHERE UserId = @UserId;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Core_Bulk_Insert_Users
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [core].[usp_Core_Bulk_Insert_Users]
                @Users [core].[Tvp_Core_User] READONLY
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [core].[Users] (UserName, Email, Age)
                SELECT UserName, Email, Age FROM @Users;
                SELECT @@ROWCOUNT AS RowsAffected;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Core_Transaction_Test
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [core].[usp_Core_Transaction_Test]
                @UserName NVARCHAR(100),
                @Email NVARCHAR(255),
                @ShouldRollback BIT = 0
            AS
            BEGIN
                SET XACT_ABORT ON;
                SET NOCOUNT ON;
                BEGIN TRANSACTION;
                SAVE TRANSACTION SavePoint1;

                INSERT INTO [core].[Users] (UserName, Email)
                VALUES (@UserName, @Email);

                IF @ShouldRollback = 1
                BEGIN
                    ROLLBACK TRANSACTION SavePoint1;
                    COMMIT TRANSACTION;
                    SELECT 'ROLLED_BACK_TO_SAVEPOINT' AS Result;
                    RETURN;
                END

                COMMIT TRANSACTION;
                SELECT 'COMMITTED' AS Result;
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [adv] 스키마

    /// <summary>
    /// [adv] 스키마: ResumableLogs 테이블 + SP 2개를 생성한다.
    /// </summary>
    private static async Task EnsureAdvSchemaAsync(IProcedureStage db)
    {
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'adv')
                EXEC('CREATE SCHEMA [adv]')
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[adv].[ResumableLogs]', 'U') IS NULL
            BEGIN
                CREATE TABLE [adv].[ResumableLogs] (
                    LogId INT IDENTITY(1,1) PRIMARY KEY,
                    Message NVARCHAR(100),
                    CreatedAt DATETIME2 NOT NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [adv].[usp_Adv_OutputParameters]
                @InputVal INT,
                @OutputVal INT OUTPUT,
                @InOutVal INT OUTPUT
            AS
            BEGIN
                SET @OutputVal = @InputVal * 2;
                SET @InOutVal = @InOutVal + @InputVal;
                RETURN @InputVal;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [adv].[usp_Adv_GenerateLogs]
                @Count INT
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @i INT = 0;
                WHILE @i < @Count
                BEGIN
                    INSERT INTO [adv].[ResumableLogs] (Message, CreatedAt)
                    VALUES (CONCAT('Log_', @i), DATEADD(MS, @i, SYSDATETIME()));
                    SET @i = @i + 1;
                END
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [exception] 스키마

    /// <summary>
    /// [exception] 스키마: 테이블 3개 (ParentTable, ChildTable, UniqueTable) + SP 4개를 생성한다.
    /// </summary>
    private static async Task EnsureExceptionSchemaAsync(IProcedureStage db)
    {
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'exception')
                EXEC('CREATE SCHEMA [exception]')
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[exception].[ParentTable]', 'U') IS NULL
            BEGIN
                CREATE TABLE [exception].[ParentTable] (
                    ParentId INT PRIMARY KEY,
                    ParentName NVARCHAR(100) NOT NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[exception].[ChildTable]', 'U') IS NULL
            BEGIN
                CREATE TABLE [exception].[ChildTable] (
                    ChildId INT PRIMARY KEY,
                    ParentId INT NOT NULL,
                    ChildName NVARCHAR(100) NOT NULL,
                    CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId)
                        REFERENCES [exception].[ParentTable](ParentId)
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[exception].[UniqueTable]', 'U') IS NULL
            BEGIN
                CREATE TABLE [exception].[UniqueTable] (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    UniqueValue NVARCHAR(100) NOT NULL UNIQUE,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [exception].[usp_Exception_ForeignKeyViolation]
                @NonExistentParentId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [exception].[ChildTable] (ChildId, ParentId, ChildName)
                VALUES (999, @NonExistentParentId, 'Test Child');
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [exception].[usp_Exception_UniqueViolation]
                @DuplicateValue NVARCHAR(100)
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [exception].[UniqueTable] (UniqueValue) VALUES (@DuplicateValue);
                INSERT INTO [exception].[UniqueTable] (UniqueValue) VALUES (@DuplicateValue);
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [exception].[usp_Exception_InvalidObjectName]
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT * FROM [exception].[NonExistentTable];
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [exception].[usp_Exception_DivideByZero]
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @Result INT;
                SET @Result = 10 / 0;
                SELECT @Result AS Result;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // exception 초기 데이터 (멱등)
        await db.Sql("""
            DELETE FROM [exception].[ChildTable];
            DELETE FROM [exception].[ParentTable];
            DELETE FROM [exception].[UniqueTable];
            INSERT INTO [exception].[ParentTable] (ParentId, ParentName)
            VALUES (1, 'Parent 1'), (2, 'Parent 2');
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [perf] 스키마

    /// <summary>
    /// [perf] 스키마: BulkTest 테이블 + TVP + SP 2개를 생성한다.
    /// </summary>
    private static async Task EnsurePerfSchemaAsync(IProcedureStage db)
    {
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'perf')
                EXEC('CREATE SCHEMA [perf]')
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[perf].[BulkTest]', 'U') IS NULL
            BEGIN
                CREATE TABLE [perf].[BulkTest] (
                    [Id] [bigint] IDENTITY(1,1) PRIMARY KEY,
                    [BatchNumber] [int] NOT NULL,
                    [Data] [nvarchar](500) NULL,
                    [CreatedAt] [datetime2](7) NULL DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF TYPE_ID('[perf].[Tvp_Perf_BulkInsert]') IS NULL
            BEGIN
                CREATE TYPE [perf].[Tvp_Perf_BulkInsert] AS TABLE (
                    [BatchNumber] [int] NOT NULL,
                    [Data] [nvarchar](500) NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [perf].[usp_Perf_Bulk_Insert]
                @Items [perf].[Tvp_Perf_BulkInsert] READONLY
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [perf].[BulkTest] (BatchNumber, Data)
                SELECT BatchNumber, Data FROM @Items;
                SELECT @@ROWCOUNT AS RowsAffected;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [perf].[usp_Perf_Query_With_Param]
                @BatchNumber INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT Id, BatchNumber, Data, CreatedAt
                FROM [perf].[BulkTest]
                WHERE BatchNumber = @BatchNumber;
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [tvp] 스키마

    /// <summary>
    /// [tvp] 스키마: TypeTest 테이블 + TVP 타입 5개 + SP 3개를 생성한다.
    /// </summary>
    private static async Task EnsureTvpSchemaAsync(IProcedureStage db)
    {
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'tvp')
                EXEC('CREATE SCHEMA [tvp]')
            """).ExecuteAsync().ConfigureAwait(false);

        // TypeTest 테이블
        await db.Sql("""
            IF OBJECT_ID('[tvp].[TypeTest]', 'U') IS NULL
            BEGIN
                CREATE TABLE [tvp].[TypeTest] (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    DateOnlyValue DATE NOT NULL,
                    TimeOnlyValue TIME NOT NULL,
                    HalfValue REAL NOT NULL,
                    GuidValue UNIQUEIDENTIFIER NOT NULL,
                    DecimalValue DECIMAL(18,4) NOT NULL,
                    NullableDateOnly DATE NULL,
                    NullableTimeOnly TIME NULL,
                    NullableHalf REAL NULL,
                    CreatedAt DATETIME2 DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF TYPE_ID(N'dbo.T_StandardEvent') IS NULL
            BEGIN
                CREATE TYPE [dbo].[T_StandardEvent] AS TABLE (
                    [EventId] INT NULL,
                    [CreatedAt] DATETIME2(3) NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF TYPE_ID(N'dbo.T_PrecisionEvent') IS NULL
            BEGIN
                CREATE TYPE [dbo].[T_PrecisionEvent] AS TABLE (
                    [EventId] INT NULL,
                    [CreatedAt] DATETIME2(7) NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // TVP 타입: TypeTest (기본)
        await db.Sql("""
            IF TYPE_ID('[tvp].[TypeTest]') IS NULL
            BEGIN
                CREATE TYPE [tvp].[TypeTest] AS TABLE (
                    Id INT,
                    Name NVARCHAR(50),
                    Value DECIMAL(18,2)
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // TVP 타입: Tvp_Tvp_AllTypes
        await db.Sql("""
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
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF TYPE_ID('[tvp].[Tvp_Tvp_AllTypes]') IS NULL
            BEGIN
                CREATE TYPE [tvp].[Tvp_Tvp_AllTypes] AS TABLE (
                    DateOnlyValue DATE NOT NULL,
                    TimeOnlyValue TIME NOT NULL,
                    HalfValue REAL NOT NULL,
                    GuidValue UNIQUEIDENTIFIER NOT NULL,
                    DecimalValue DECIMAL(18,4) NOT NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // TVP 타입: Tvp_Tvp_Nullable
        await db.Sql("""
            IF TYPE_ID('[tvp].[Tvp_Tvp_Nullable]') IS NULL
            BEGIN
                CREATE TYPE [tvp].[Tvp_Tvp_Nullable] AS TABLE (
                    NullableDateOnly DATE NULL,
                    NullableTimeOnly TIME NULL,
                    NullableHalf REAL NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // TVP 타입: Tvp_Tvp_SchemaMismatch
        await db.Sql("""
            IF TYPE_ID('[tvp].[Tvp_Tvp_SchemaMismatch]') IS NULL
            BEGIN
                CREATE TYPE [tvp].[Tvp_Tvp_SchemaMismatch] AS TABLE (
                    ColumnA NVARCHAR(50) NULL,
                    ColumnB INT NULL,
                    ColumnC DATETIME NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Tvp_Bulk_Insert_AllTypes
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [tvp].[usp_Tvp_Bulk_Insert_AllTypes]
                @Items [tvp].[Tvp_Tvp_AllTypes] READONLY
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [tvp].[TypeTest] (DateOnlyValue, TimeOnlyValue, HalfValue, GuidValue, DecimalValue)
                SELECT DateOnlyValue, TimeOnlyValue, HalfValue, GuidValue, DecimalValue FROM @Items;
                SELECT @@ROWCOUNT AS RowsAffected;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Tvp_Get_AllTypes
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [tvp].[usp_Tvp_Get_AllTypes]
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT Id, DateOnlyValue, TimeOnlyValue, HalfValue, GuidValue, DecimalValue,
                       NullableDateOnly, NullableTimeOnly, NullableHalf, CreatedAt
                FROM [tvp].[TypeTest];
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP: usp_Tvp_Test_Schema_Mismatch
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [tvp].[usp_Tvp_Test_Schema_Mismatch]
                @Items [tvp].[Tvp_Tvp_SchemaMismatch] READONLY
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT ColumnA, ColumnB, ColumnC FROM @Items;
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [resilience] 스키마

    /// <summary>
    /// [resilience] 스키마: 테이블 2개 (RetryTest, TimeoutTest) + SP 2개를 생성한다.
    /// </summary>
    private static async Task EnsureResilienceSchemaAsync(IProcedureStage db)
    {
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'resilience')
                EXEC('CREATE SCHEMA [resilience]')
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[resilience].[RetryTest]', 'U') IS NULL
            BEGIN
                CREATE TABLE [resilience].[RetryTest] (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    AttemptNumber INT NOT NULL,
                    SuccessFlag BIT NOT NULL DEFAULT 0,
                    AttemptedAt DATETIME2 DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[resilience].[TimeoutTest]', 'U') IS NULL
            BEGIN
                CREATE TABLE [resilience].[TimeoutTest] (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    DelaySeconds INT NOT NULL,
                    CompletedAt DATETIME2 DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [resilience].[usp_Resilience_Simulate_Delay]
                @DelaySeconds INT
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @delay NVARCHAR(8) = '00:00:' + RIGHT('0' + CAST(@DelaySeconds AS NVARCHAR), 2);
                WAITFOR DELAY @delay;
                INSERT INTO [resilience].[TimeoutTest] (DelaySeconds) VALUES (@DelaySeconds);
                SELECT @DelaySeconds AS DelaySeconds, 'Completed' AS Status;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [resilience].[usp_Resilience_Simulate_Failure]
                @FailureProbability INT
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @Random INT = ABS(CHECKSUM(NEWID())) % 100;
                IF @Random < @FailureProbability
                    RAISERROR('Simulated transient failure', 16, 1);
                ELSE
                BEGIN
                    INSERT INTO [resilience].[RetryTest] (AttemptNumber, SuccessFlag)
                    VALUES (1, 1);
                    SELECT 'Success' AS Status;
                END
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [gap] 스키마 (기능 완전성 검증: 테이블 3개 + TVP 1개 + SP 8개)

    /// <summary>
    /// [gap] 스키마: 테이블 3개 (BulkTarget, JsonData, MergeTarget) + TVP 1개 + SP 8개를 CREATE OR ALTER로 생성한다.
    /// <para><b>[설계 의도]</b> SQL Server 기능 완전성(벌크, 격리 수준, JSON, MERGE, 페이지네이션, 윈도우 함수) 검증용.</para>
    /// </summary>
    private static async Task EnsureGapSchemaAsync(IProcedureStage db)
    {
        // 스키마 생성
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'gap')
                EXEC('CREATE SCHEMA [gap]')
            """).ExecuteAsync().ConfigureAwait(false);

        // BulkTarget 테이블
        await db.Sql("""
            IF OBJECT_ID('[gap].[BulkTarget]', 'U') IS NULL
            BEGIN
                CREATE TABLE [gap].[BulkTarget] (
                    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
                    Data NVARCHAR(200) NOT NULL,
                    BatchId INT NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // TVP 타입: Tvp_BulkTarget
        await db.Sql("""
            IF TYPE_ID('[gap].[Tvp_BulkTarget]') IS NULL
            BEGIN
                CREATE TYPE [gap].[Tvp_BulkTarget] AS TABLE (
                    Data NVARCHAR(200) NOT NULL,
                    BatchId INT NOT NULL
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // JsonData 테이블
        await db.Sql("""
            IF OBJECT_ID('[gap].[JsonData]', 'U') IS NULL
            BEGIN
                CREATE TABLE [gap].[JsonData] (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Payload NVARCHAR(MAX) NOT NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // MergeTarget 테이블
        await db.Sql("""
            IF OBJECT_ID('[gap].[MergeTarget]', 'U') IS NULL
            BEGIN
                CREATE TABLE [gap].[MergeTarget] (
                    Id INT PRIMARY KEY,
                    Name NVARCHAR(100) NOT NULL,
                    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // BulkMutationTarget 테이블
        await db.Sql("""
            IF OBJECT_ID('[gap].[BulkMutationTarget]', 'U') IS NULL
            BEGIN
                CREATE TABLE [gap].[BulkMutationTarget] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_BulkMutationTarget] PRIMARY KEY,
                    [ExternalId] int NOT NULL,
                    [Name] nvarchar(100) NOT NULL,
                    [Price] decimal(18,2) NOT NULL,
                    [UpdatedAt] datetime2(7) NOT NULL CONSTRAINT [DF_BulkMutationTarget_UpdatedAt] DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT [CK_BulkMutationTarget_Price_NonNegative] CHECK ([Price] >= 0)
                )
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[gap].[BulkMutationTarget]', 'U') IS NOT NULL
               AND OBJECT_ID('[gap].[CK_BulkMutationTarget_Price_NonNegative]', 'C') IS NULL
            BEGIN
                ALTER TABLE [gap].[BulkMutationTarget] WITH CHECK
                ADD CONSTRAINT [CK_BulkMutationTarget_Price_NonNegative] CHECK ([Price] >= 0);
            END;
            """).ExecuteAsync().ConfigureAwait(false);

        // SP 1: usp_BulkInsert_Tvp
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [gap].[usp_BulkInsert_Tvp]
                @Items [gap].[Tvp_BulkTarget] READONLY
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [gap].[BulkTarget] (Data, BatchId)
                SELECT Data, BatchId FROM @Items;
                SELECT @@ROWCOUNT AS RowsAffected;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP 2: usp_IsolationLevel_ReadUncommitted
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [gap].[usp_IsolationLevel_ReadUncommitted]
                @TargetId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
                SELECT UserId, UserName, Email
                FROM core.Users WITH (NOLOCK)
                WHERE UserId = @TargetId;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP 3: usp_IsolationLevel_Serializable
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [gap].[usp_IsolationLevel_Serializable]
                @TargetId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
                BEGIN TRANSACTION;
                SELECT UserId, UserName, Email
                FROM core.Users
                WHERE UserId = @TargetId;
                COMMIT TRANSACTION;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP 4: usp_Json_Insert
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [gap].[usp_Json_Insert]
                @JsonPayload NVARCHAR(MAX)
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [gap].[JsonData] (Payload)
                VALUES (@JsonPayload);
                SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewId;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP 5: usp_Json_Query
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [gap].[usp_Json_Query]
                @Key NVARCHAR(100)
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT Id, JSON_VALUE(Payload, CONCAT('$.', @Key)) AS ExtractedValue, Payload
                FROM [gap].[JsonData]
                WHERE ISJSON(Payload) = 1;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP 6: usp_Merge_Upsert
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [gap].[usp_Merge_Upsert]
                @Id INT,
                @Name NVARCHAR(100)
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @ActionTable TABLE (MergeAction NVARCHAR(10));

                MERGE [gap].[MergeTarget] AS target
                USING (SELECT @Id AS Id, @Name AS Name) AS source
                ON target.Id = source.Id
                WHEN MATCHED THEN
                    UPDATE SET Name = source.Name, UpdatedAt = SYSDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (Id, Name) VALUES (source.Id, source.Name)
                OUTPUT $action INTO @ActionTable;

                SELECT MergeAction FROM @ActionTable;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP 7: usp_Paginate
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [gap].[usp_Paginate]
                @PageNum INT,
                @PageSize INT
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @TotalCount INT;
                SELECT @TotalCount = COUNT(*) FROM core.Users;

                SELECT UserId, UserName, Email, Age
                FROM core.Users
                ORDER BY UserId
                OFFSET ((@PageNum - 1) * @PageSize) ROWS
                FETCH NEXT @PageSize ROWS ONLY;

                SELECT @TotalCount AS TotalCount;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        // SP 8: usp_WindowFunction_RankUsers
        await db.Sql("""
            CREATE OR ALTER PROCEDURE [gap].[usp_WindowFunction_RankUsers]
            AS
            BEGIN
                SET NOCOUNT ON;

                ;WITH UserRanking AS (
                    SELECT
                        UserId,
                        UserName,
                        Email,
                        Age,
                        ROW_NUMBER() OVER (ORDER BY UserId) AS RowNum,
                        RANK() OVER (ORDER BY ISNULL(Age, 0) DESC) AS AgeRank,
                        DENSE_RANK() OVER (ORDER BY ISNULL(Age, 0) DESC) AS DenseAgeRank,
                        COUNT(*) OVER () AS TotalUsers
                    FROM core.Users
                )
                SELECT * FROM UserRanking
                ORDER BY RowNum;
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [verify] v2.2.1 차단 이슈 검증 스키마

    /// <summary>
    /// v2.2.1에서 확인된 결과 매핑/DateOnly/QUOTED_IDENTIFIER 회귀를 실 DB에서 검증할 객체를 생성한다.
    /// </summary>
    private static async Task EnsureV221BlockerVerificationSchemaAsync(IProcedureStage db)
    {
        await db.Sql("""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'verify')
                EXEC('CREATE SCHEMA [verify]')
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            IF OBJECT_ID('[verify].[ResultMappingRows]', 'U') IS NULL
            BEGIN
                CREATE TABLE [verify].[ResultMappingRows] (
                    CELL_NO INT NOT NULL PRIMARY KEY,
                    SLOT_NAME NVARCHAR(40) NOT NULL,
                    SCAN_DATE DATE NOT NULL,
                    USER_ID INT NOT NULL,
                    USER_NAME NVARCHAR(100) NOT NULL,
                    EMAIL NVARCHAR(255) NOT NULL,
                    AGE INT NULL
                );
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            MERGE [verify].[ResultMappingRows] AS target
            USING (VALUES
                (17, N'A01', CONVERT(date, '2026-05-17'), 1001, N'Generated User', N'generated.user@example.test', 27)
            ) AS source (CELL_NO, SLOT_NAME, SCAN_DATE, USER_ID, USER_NAME, EMAIL, AGE)
            ON target.CELL_NO = source.CELL_NO
            WHEN MATCHED THEN UPDATE SET
                SLOT_NAME = source.SLOT_NAME,
                SCAN_DATE = source.SCAN_DATE,
                USER_ID = source.USER_ID,
                USER_NAME = source.USER_NAME,
                EMAIL = source.EMAIL,
                AGE = source.AGE
            WHEN NOT MATCHED THEN
                INSERT (CELL_NO, SLOT_NAME, SCAN_DATE, USER_ID, USER_NAME, EMAIL, AGE)
                VALUES (source.CELL_NO, source.SLOT_NAME, source.SCAN_DATE, source.USER_ID, source.USER_NAME, source.EMAIL, source.AGE);
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            SET QUOTED_IDENTIFIER ON;

            IF OBJECT_ID('[verify].[QuotedIdentifierRows]', 'U') IS NULL
            BEGIN
                CREATE TABLE [verify].[QuotedIdentifierRows] (
                    RowId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    RawCode NVARCHAR(50) NOT NULL,
                    NormalizedCode AS UPPER([RawCode]) PERSISTED
                );
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID('[verify].[QuotedIdentifierRows]')
                  AND name = 'IX_QuotedIdentifierRows_NormalizedCode'
            )
            BEGIN
                CREATE INDEX [IX_QuotedIdentifierRows_NormalizedCode]
                    ON [verify].[QuotedIdentifierRows] ([NormalizedCode]);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM [verify].[QuotedIdentifierRows]
                WHERE [RawCode] = N'tbl_order'
            )
            BEGIN
                INSERT INTO [verify].[QuotedIdentifierRows] ([RawCode])
                VALUES (N'tbl_order');
            END;
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [verify].[usp_GetSuspendRows]
                @ScanDate DATE
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    CELL_NO,
                    SLOT_NAME
                FROM [verify].[ResultMappingRows]
                WHERE SCAN_DATE = @ScanDate
                ORDER BY CELL_NO;
            END
            """).ExecuteAsync().ConfigureAwait(false);

        await db.Sql("""
            CREATE OR ALTER PROCEDURE [verify].[usp_GetGeneratedRows]
                @ScanDate DATE
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    USER_ID AS UserId,
                    USER_NAME AS UserName,
                    EMAIL AS Email,
                    AGE AS Age
                FROM [verify].[ResultMappingRows]
                WHERE SCAN_DATE = @ScanDate
                ORDER BY USER_ID;
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion

    #region [verify] 대표 복잡도/TVP shape 검증 스키마

    /// <summary>
    /// v2.3 런타임 TVP의 대표 검증 DB 역할을 위한 복합 테이블, TVP type, mixed-parameter SP를 생성한다.
    /// </summary>
    private static async Task EnsureRepresentativeVerificationSchemaAsync(IProcedureStage db)
    {
        await ExecuteRequiredSchemaBatchAsync(db, """
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'verify')
                EXEC('CREATE SCHEMA [verify]');

            IF OBJECT_ID('[verify].[VerificationCustomerSegments]', 'U') IS NULL
            BEGIN
                CREATE TABLE [verify].[VerificationCustomerSegments] (
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

            IF OBJECT_ID('[verify].[VerificationOrderHeaders]', 'U') IS NULL
            BEGIN
                CREATE TABLE [verify].[VerificationOrderHeaders] (
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

            IF OBJECT_ID('[verify].[VerificationOrderLines]', 'U') IS NULL
            BEGIN
                CREATE TABLE [verify].[VerificationOrderLines] (
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

            IF OBJECT_ID('[verify].[VerificationOrderAudit]', 'U') IS NULL
            BEGIN
                CREATE TABLE [verify].[VerificationOrderAudit] (
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

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('[verify].[VerificationOrderHeaders]') AND name = 'IX_verify_VerificationOrderHeaders_OpenStatus')
            BEGIN
                CREATE INDEX [IX_verify_VerificationOrderHeaders_OpenStatus]
                    ON [verify].[VerificationOrderHeaders] ([TenantId], [OrderStatus], [SubmittedAt])
                    INCLUDE ([ApprovedAt], [ExternalOrderId], [NetAmount])
                    WHERE [ApprovedAt] IS NULL;
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('[verify].[VerificationOrderHeaders]') AND name = 'IX_verify_VerificationOrderHeaders_Correlation')
            BEGIN
                CREATE INDEX [IX_verify_VerificationOrderHeaders_Correlation]
                    ON [verify].[VerificationOrderHeaders] ([CorrelationId])
                    INCLUDE ([TenantId], [ExternalOrderId], [OrderStatus]);
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('[verify].[VerificationOrderLines]') AND name = 'IX_verify_VerificationOrderLines_Product')
            BEGIN
                CREATE INDEX [IX_verify_VerificationOrderLines_Product]
                    ON [verify].[VerificationOrderLines] ([ProductCode], [VerificationOrderId])
                    INCLUDE ([Quantity], [UnitPrice], [LineAmount]);
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('[verify].[VerificationOrderAudit]') AND name = 'IX_verify_VerificationOrderAudit_Correlation')
            BEGIN
                CREATE INDEX [IX_verify_VerificationOrderAudit_Correlation]
                    ON [verify].[VerificationOrderAudit] ([CorrelationId], [CreatedAt]);
            END;
            """).ConfigureAwait(false);

        await ExecuteRequiredSchemaBatchAsync(db, """
            IF TYPE_ID('[verify].[Tvp_VerificationOrderHeader]') IS NULL
            BEGIN
                CREATE TYPE [verify].[Tvp_VerificationOrderHeader] AS TABLE (
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
            END;
            """).ConfigureAwait(false);

        await ExecuteRequiredSchemaBatchAsync(db, """
            IF TYPE_ID('[verify].[Tvp_VerificationOrderLine]') IS NULL
            BEGIN
                CREATE TYPE [verify].[Tvp_VerificationOrderLine] AS TABLE (
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
            END;
            """).ConfigureAwait(false);

        await ExecuteRequiredSchemaBatchAsync(db, """
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
                    @InsertedOrders AS InsertedOrders,
                    @InsertedLines AS InsertedLines,
                    (
                        SELECT COUNT(*)
                        FROM [verify].[VerificationOrderHeaders]
                        WHERE [TenantId] = @TenantId AND [ApprovedAt] IS NULL
                    ) AS OpenOrderCount;
            END
            """).ConfigureAwait(false);
    }

    #endregion

    private static async Task ExecuteRequiredSchemaBatchAsync(IProcedureStage db, string sql)
    {
        DbResult<int> result = await db.Sql(sql).ExecuteAsync().ConfigureAwait(false);
        if (result.IsSuccess)
            return;

        string message = result.Error?.InnerException?.Message
            ?? result.Error?.Message
            ?? "Schema batch failed without an error message.";
        throw new InvalidOperationException(message);
    }

    #region [dbo] 유틸리티 SP

    /// <summary>
    /// dbo 스키마의 유틸리티 SP를 생성한다.
    /// </summary>
    private static async Task EnsureDboUtilityAsync(IProcedureStage db)
    {
        await db.Sql("""
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
                IF OBJECT_ID('[dbo].[libdb_aot_OrderItems]', 'U') IS NOT NULL DELETE FROM [dbo].[libdb_aot_OrderItems];
                IF OBJECT_ID('[dbo].[libdb_bench_OrderItems]', 'U') IS NOT NULL DELETE FROM [dbo].[libdb_bench_OrderItems];
                DELETE FROM [perf].[BulkTest];
                DELETE FROM [resilience].[RetryTest];
                DELETE FROM [resilience].[TimeoutTest];
                IF OBJECT_ID('[gap].[BulkTarget]', 'U') IS NOT NULL DELETE FROM [gap].[BulkTarget];
                IF OBJECT_ID('[gap].[BulkMutationTarget]', 'U') IS NOT NULL DELETE FROM [gap].[BulkMutationTarget];
                IF OBJECT_ID('[gap].[JsonData]', 'U') IS NOT NULL DELETE FROM [gap].[JsonData];
                IF OBJECT_ID('[gap].[MergeTarget]', 'U') IS NOT NULL DELETE FROM [gap].[MergeTarget];
                IF OBJECT_ID('[verify].[VerificationOrderAudit]', 'U') IS NOT NULL DELETE FROM [verify].[VerificationOrderAudit];
                IF OBJECT_ID('[verify].[VerificationOrderLines]', 'U') IS NOT NULL DELETE FROM [verify].[VerificationOrderLines];
                IF OBJECT_ID('[verify].[VerificationOrderHeaders]', 'U') IS NOT NULL DELETE FROM [verify].[VerificationOrderHeaders];
                IF OBJECT_ID('[verify].[VerificationCustomerSegments]', 'U') IS NOT NULL DELETE FROM [verify].[VerificationCustomerSegments];
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion
}
