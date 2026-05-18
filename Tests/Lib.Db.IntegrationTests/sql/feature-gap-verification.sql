-- ============================================================================
-- 파일: feature-gap-verification.sql
-- 설명: Lib.Db v2 SQL Server 기능 완전성 검증용 객체 생성
-- 대상: LIBDB_VERIFICATION_TEST (SQL Server 2025 Express)
-- 실행: sqlcmd -S localhost -U sa -P <password> -i feature-gap-verification.sql -f 65001 -C
-- ============================================================================

USE [LIBDB_VERIFICATION_TEST];
GO

-- ============================================================================
-- [gap] 스키마 생성
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'gap')
    EXEC('CREATE SCHEMA [gap]')
GO

-- ============================================================================
-- 1. BulkTarget 테이블 — SqlBulkCopy 대안(TVP) 성능 검증용
-- ============================================================================
IF OBJECT_ID('[gap].[BulkTarget]', 'U') IS NULL
CREATE TABLE [gap].[BulkTarget] (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    Data NVARCHAR(200) NOT NULL,
    BatchId INT NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

-- TVP for bulk insert
IF TYPE_ID('[gap].[Tvp_BulkTarget]') IS NULL
CREATE TYPE [gap].[Tvp_BulkTarget] AS TABLE (
    Data NVARCHAR(200) NOT NULL,
    BatchId INT NOT NULL
);
GO

CREATE OR ALTER PROCEDURE [gap].[usp_BulkInsert_Tvp]
    @Items [gap].[Tvp_BulkTarget] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [gap].[BulkTarget] (Data, BatchId)
    SELECT Data, BatchId FROM @Items;
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- ============================================================================
-- 2. 트랜잭션 격리 수준 검증 SP
-- ============================================================================
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
GO

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
GO

-- ============================================================================
-- 3. JSON 컬럼 검증 (SQL Server 2025 native JSON 타입)
-- ============================================================================
IF OBJECT_ID('[gap].[JsonData]', 'U') IS NULL
CREATE TABLE [gap].[JsonData] (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Payload NVARCHAR(MAX) NOT NULL,  -- SQL Server 2025 JSON 타입 대신 NVARCHAR(MAX) 사용 (호환성)
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE OR ALTER PROCEDURE [gap].[usp_Json_Insert]
    @JsonPayload NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [gap].[JsonData] (Payload)
    VALUES (@JsonPayload);
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewId;
END
GO

CREATE OR ALTER PROCEDURE [gap].[usp_Json_Query]
    @Key NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, JSON_VALUE(Payload, CONCAT('$.', @Key)) AS ExtractedValue, Payload
    FROM [gap].[JsonData]
    WHERE ISJSON(Payload) = 1;
END
GO

-- ============================================================================
-- 4. MERGE + OUTPUT 절 검증
-- ============================================================================
IF OBJECT_ID('[gap].[MergeTarget]', 'U') IS NULL
CREATE TABLE [gap].[MergeTarget] (
    Id INT PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

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
GO

-- ============================================================================
-- 5. 페이지네이션 검증
-- ============================================================================
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
GO

-- ============================================================================
-- 6. CTE + 윈도우 함수 검증
-- ============================================================================
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
GO

PRINT N'[완료] gap 스키마: 테이블 3개, TVP 1개, SP 8개 생성 성공';
GO
