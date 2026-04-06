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

        // 9. dbo 유틸리티 SP
        await EnsureDboUtilityAsync(db).ConfigureAwait(false);
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

        // TVP 타입: Tvp_Core_User
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
                DELETE FROM [perf].[BulkTest];
                DELETE FROM [resilience].[RetryTest];
                DELETE FROM [resilience].[TimeoutTest];
                IF OBJECT_ID('[gap].[BulkTarget]', 'U') IS NOT NULL DELETE FROM [gap].[BulkTarget];
                IF OBJECT_ID('[gap].[JsonData]', 'U') IS NOT NULL DELETE FROM [gap].[JsonData];
                IF OBJECT_ID('[gap].[MergeTarget]', 'U') IS NOT NULL DELETE FROM [gap].[MergeTarget];
            END
            """).ExecuteAsync().ConfigureAwait(false);
    }

    #endregion
}
