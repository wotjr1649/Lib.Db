-- ============================================================================
-- LIBDB_VERIFICATION_TEST Database Setup Script
-- 생성일: 2025-12-19
-- 목적: 모든 TVP를 알파벳 순서로 정의하여 Source Generator와 일치
-- 변경사항:
--   - core.Tvp_Core_User: Age, Email, UserName (알파벳 순서)
--   - tvp.Tvp_Tvp_AllTypes: DateOnlyValue, DecimalValue, GuidValue, HalfValue, TimeOnlyValue
-- ============================================================================

USE [master]
GO

-- 기존 DB가 있으면 삭제
IF EXISTS (SELECT name FROM sys.databases WHERE name = N'LIBDB_VERIFICATION_TEST')
BEGIN
    ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [LIBDB_VERIFICATION_TEST];
END
GO

-- DB 생성
CREATE DATABASE [LIBDB_VERIFICATION_TEST]
GO

ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET COMPATIBILITY_LEVEL = 160
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET ANSI_NULL_DEFAULT OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET ANSI_NULLS OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET ANSI_PADDING OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET ANSI_WARNINGS OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET ARITHABORT OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET AUTO_CLOSE ON 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET AUTO_SHRINK OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET AUTO_UPDATE_STATISTICS ON 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET CURSOR_CLOSE_ON_COMMIT OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET CURSOR_DEFAULT  GLOBAL 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET CONCAT_NULL_YIELDS_NULL OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET NUMERIC_ROUNDABORT OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET QUOTED_IDENTIFIER OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET RECURSIVE_TRIGGERS OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET  ENABLE_BROKER 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET AUTO_UPDATE_STATISTICS_ASYNC OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET DATE_CORRELATION_OPTIMIZATION OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET TRUSTWORTHY OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET ALLOW_SNAPSHOT_ISOLATION OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET PARAMETERIZATION SIMPLE 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET READ_COMMITTED_SNAPSHOT OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET HONOR_BROKER_PRIORITY OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET RECOVERY SIMPLE 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET  MULTI_USER 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET PAGE_VERIFY CHECKSUM  
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET DB_CHAINING OFF 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET FILESTREAM( NON_TRANSACTED_ACCESS = OFF ) 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET TARGET_RECOVERY_TIME = 60 SECONDS 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET DELAYED_DURABILITY = DISABLED 
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET ACCELERATED_DATABASE_RECOVERY = OFF  
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET QUERY_STORE = ON
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET QUERY_STORE (OPERATION_MODE = READ_WRITE, CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30), DATA_FLUSH_INTERVAL_SECONDS = 900, INTERVAL_LENGTH_MINUTES = 60, MAX_STORAGE_SIZE_MB = 1000, QUERY_CAPTURE_MODE = AUTO, SIZE_BASED_CLEANUP_MODE = AUTO, MAX_PLANS_PER_QUERY = 200, WAIT_STATS_CAPTURE_MODE = ON)
GO

USE [LIBDB_VERIFICATION_TEST]
GO

-- ============================================================================
-- Schemas
-- ============================================================================
CREATE SCHEMA [adv]
GO
CREATE SCHEMA [core]
GO
CREATE SCHEMA [exception]
GO
CREATE SCHEMA [perf]
GO
CREATE SCHEMA [resilience]
GO
CREATE SCHEMA [tvp]
GO

-- ============================================================================
-- TVP Types (알파벳 순서로 정의됨)
-- ============================================================================

-- ✅ core.Tvp_Core_User: Age, Email, UserName (알파벳 순서)
CREATE TYPE [core].[Tvp_Core_User] AS TABLE(
	[Age] [int] NULL,
	[Email] [nvarchar](255) NOT NULL,
	[UserName] [nvarchar](100) NOT NULL
)
GO

-- ✅ perf.Tvp_Perf_BulkInsert: BatchNumber, Data (이미 알파벳 순서)
CREATE TYPE [perf].[Tvp_Perf_BulkInsert] AS TABLE(
	[BatchNumber] [int] NOT NULL,
	[Data] [nvarchar](500) NULL
)
GO

-- ✅ tvp.Tvp_Tvp_AllTypes: DateOnlyValue, DecimalValue, GuidValue, HalfValue, TimeOnlyValue (알파벳 순서)
CREATE TYPE [tvp].[Tvp_Tvp_AllTypes] AS TABLE(
	[DateOnlyValue] [date] NOT NULL,
	[DecimalValue] [decimal](18, 4) NOT NULL,
	[GuidValue] [uniqueidentifier] NOT NULL,
	[HalfValue] [real] NOT NULL,
	[TimeOnlyValue] [time](7) NOT NULL
)
GO

-- ✅ tvp.Tvp_Tvp_Nullable: NullableDateOnly, NullableHalf, NullableTimeOnly (이미 알파벳 순서)
CREATE TYPE [tvp].[Tvp_Tvp_Nullable] AS TABLE(
	[NullableDateOnly] [date] NULL,
	[NullableHalf] [real] NULL,
	[NullableTimeOnly] [time](7) NULL
)
GO

-- ✅ tvp.Tvp_Tvp_SchemaMismatch: ColumnA, ColumnB, ColumnC (이미 알파벳 순서)
CREATE TYPE [tvp].[Tvp_Tvp_SchemaMismatch] AS TABLE(
	[ColumnA] [nvarchar](50) NULL,
	[ColumnB] [int] NULL,
	[ColumnC] [datetime2](7) NULL
)
GO

-- 100ns 정밀도가 필요한 경우 (기본값)
CREATE TYPE dbo.T_PrecisionEvent AS TABLE
(
    EventId INT,
    CreatedAt DATETIME2(7) -- C# DateTime.Precision과 일치
);
GO
-- 밀리초 정밀도로 충분한 경우 (저장 공간 절약)
CREATE TYPE dbo.T_StandardEvent AS TABLE
(
    EventId INT,
    CreatedAt DATETIME2(3) -- 7bytes (DateTime보다 1byte 절약)
);
go
-- ============================================================================
-- Tables
-- ============================================================================
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [adv].[ResumableLogs](
	[LogId] [int] IDENTITY(1,1) NOT NULL,
	[Message] [nvarchar](100) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[LogId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [core].[Orders](
	[OrderId] [int] IDENTITY(1,1) NOT NULL,
	[UserId] [int] NOT NULL,
	[ProductId] [int] NOT NULL,
	[Quantity] [int] NOT NULL,
	[TotalPrice] [decimal](18, 2) NOT NULL,
	[OrderDate] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[OrderId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [core].[Products](
	[ProductId] [int] IDENTITY(1,1) NOT NULL,
	[ProductName] [nvarchar](200) NOT NULL,
	[Price] [decimal](18, 2) NOT NULL,
	[Stock] [int] NOT NULL,
	[CreatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[ProductId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [core].[Users](
	[UserId] [int] IDENTITY(1,1) NOT NULL,
	[UserName] [nvarchar](100) NOT NULL,
	[Email] [nvarchar](255) NOT NULL,
	[Age] [int] NULL,
	[CreatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[UserId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED 
(
	[Email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [core].[CursorState](
	[InstanceHash] [varchar](100) NOT NULL,
	[QueryKey] [varchar](100) NOT NULL,
	[CursorValue] [nvarchar](max) NULL,
	[UpdatedAt] [datetime2](7) NOT NULL DEFAULT SYSDATETIME(),
PRIMARY KEY CLUSTERED 
(
	[InstanceHash] ASC,
	[QueryKey] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

CREATE TABLE [exception].[ChildTable](
	[ChildId] [int] NOT NULL,
	[ParentId] [int] NOT NULL,
	[ChildName] [nvarchar](100) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[ChildId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [exception].[ParentTable](
	[ParentId] [int] NOT NULL,
	[ParentName] [nvarchar](100) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[ParentId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [exception].[UniqueTable](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[UniqueValue] [nvarchar](100) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED 
(
	[UniqueValue] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [perf].[BulkTest](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[BatchNumber] [int] NOT NULL,
	[Data] [nvarchar](500) NULL,
	[CreatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [resilience].[RetryTest](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[AttemptNumber] [int] NOT NULL,
	[SuccessFlag] [bit] NOT NULL,
	[AttemptedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [resilience].[TimeoutTest](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[DelaySeconds] [int] NOT NULL,
	[CompletedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

CREATE TABLE [tvp].[TypeTest](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[DateOnlyValue] [date] NULL,
	[TimeOnlyValue] [time](7) NULL,
	[HalfValue] [real] NULL,
	[GuidValue] [uniqueidentifier] NULL,
	[DecimalValue] [decimal](18, 4) NULL,
	[NullableDateOnly] [date] NULL,
	[NullableTimeOnly] [time](7) NULL,
	[NullableHalf] [real] NULL,
	[CreatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

-- ============================================================================
-- Indexes
-- ============================================================================
SET ANSI_PADDING ON
GO

CREATE NONCLUSTERED INDEX [IX_Users_Email] ON [core].[Users]
(
	[Email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

CREATE NONCLUSTERED INDEX [IX_BulkTest_BatchNumber] ON [perf].[BulkTest]
(
	[BatchNumber] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

-- ============================================================================
-- Defaults
-- ============================================================================
ALTER TABLE [core].[Orders] ADD  DEFAULT (sysdatetime()) FOR [OrderDate]
GO
ALTER TABLE [core].[Products] ADD  DEFAULT ((0)) FOR [Stock]
GO
ALTER TABLE [core].[Products] ADD  DEFAULT (sysdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [core].[Users] ADD  DEFAULT (sysdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [exception].[UniqueTable] ADD  DEFAULT (sysdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [perf].[BulkTest] ADD  DEFAULT (sysdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [resilience].[RetryTest] ADD  DEFAULT (sysdatetime()) FOR [AttemptedAt]
GO
ALTER TABLE [resilience].[TimeoutTest] ADD  DEFAULT (sysdatetime()) FOR [CompletedAt]
GO
ALTER TABLE [tvp].[TypeTest] ADD  DEFAULT (sysdatetime()) FOR [CreatedAt]
GO

-- ============================================================================
-- Foreign Keys
-- ============================================================================
ALTER TABLE [core].[Orders]  WITH CHECK ADD FOREIGN KEY([ProductId])
REFERENCES [core].[Products] ([ProductId])
GO

ALTER TABLE [core].[Orders]  WITH CHECK ADD FOREIGN KEY([UserId])
REFERENCES [core].[Users] ([UserId])
GO

ALTER TABLE [exception].[ChildTable]  WITH CHECK ADD  CONSTRAINT [FK_Child_Parent] FOREIGN KEY([ParentId])
REFERENCES [exception].[ParentTable] ([ParentId])
GO
ALTER TABLE [exception].[ChildTable] CHECK CONSTRAINT [FK_Child_Parent]
GO

-- ============================================================================
-- Stored Procedures
-- ============================================================================

-- adv Schema
CREATE PROCEDURE [adv].[usp_Adv_GenerateLogs]
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
GO

CREATE PROCEDURE [adv].[usp_Adv_OutputParameters]
    @InputVal INT,
    @OutputVal INT OUTPUT,
    @InOutVal INT OUTPUT
AS
BEGIN
    SET @OutputVal = @InputVal * 2;
    SET @InOutVal = @InOutVal + @InputVal;
    RETURN @InputVal;
END
GO

-- core Schema
-- ⚠️ 주의: TVP 컬럼 순서가 알파벳 순서로 변경됨 (Age, Email, UserName)
CREATE PROCEDURE [core].[usp_Core_Bulk_Insert_Users]
    @Users [core].[Tvp_Core_User] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    -- TVP 알파벳 순서: Age, Email, UserName
    -- 하지만 INSERT는 컬럼명으로 매핑되므로 순서 무관
    INSERT INTO [core].[Users] (UserName, Email, Age)
    SELECT UserName, Email, Age
    FROM @Users;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

CREATE PROCEDURE [core].[usp_Core_Get_Dashboard]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT UserId, UserName, Email
    FROM [core].[Users]
    WHERE UserId = @UserId;
    
    SELECT OrderId, ProductId, Quantity, TotalPrice, OrderDate
    FROM [core].[Orders]
    WHERE UserId = @UserId;
    
    SELECT 
        COUNT(*) AS TotalOrders,
        SUM(TotalPrice) AS TotalSpent
    FROM [core].[Orders]
    WHERE UserId = @UserId;
END;
GO

CREATE PROCEDURE [core].[usp_Core_Get_User]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, UserName, Email, Age, CreatedAt
    FROM [core].[Users]
    WHERE UserId = @UserId;
END;
GO

CREATE PROCEDURE [core].[usp_Core_Insert_User]
    @UserName NVARCHAR(100),
    @Email NVARCHAR(255),
    @Age INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO [core].[Users] (UserName, Email, Age)
    VALUES (@UserName, @Email, @Age);
    
    SELECT CAST(SCOPE_IDENTITY() AS INT) AS NewUserId;
END;
GO

CREATE PROCEDURE [core].[usp_Core_Search_Users]
    @SearchTerm NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT UserId, UserName, Email, Age
    FROM [core].[Users]
    WHERE UserName LIKE '%' + @SearchTerm + '%'
       OR Email LIKE '%' + @SearchTerm + '%';
END;
GO

CREATE PROCEDURE [core].[usp_Core_Transaction_Test]
    @UserName NVARCHAR(100),
    @Email NVARCHAR(255),
    @ProductId INT,
    @Quantity INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    
    BEGIN TRANSACTION;
    
    DECLARE @NewUserId INT;
    
    SAVE TRANSACTION SP_InsertUser;
    
    INSERT INTO [core].[Users] (UserName, Email)
    VALUES (@UserName, @Email);
    
    SET @NewUserId = CAST(SCOPE_IDENTITY() AS INT);
    
    SAVE TRANSACTION SP_CreateOrder;
    
    DECLARE @Price DECIMAL(18,2);
    SELECT @Price = Price FROM [core].[Products] WHERE ProductId = @ProductId;
    
    INSERT INTO [core].[Orders] (UserId, ProductId, Quantity, TotalPrice)
    VALUES (@NewUserId, @ProductId, @Quantity, @Price * @Quantity);
    
    COMMIT TRANSACTION;
    
    SELECT @NewUserId AS NewUserId;
END;
GO

-- dbo Schema
CREATE PROCEDURE [dbo].[usp_Test_Reset_All_Data]
AS
BEGIN
    SET NOCOUNT ON;
    
    EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';
    
    TRUNCATE TABLE [core].[Orders];
    TRUNCATE TABLE [core].[Users];
    TRUNCATE TABLE [core].[Products];
    TRUNCATE TABLE [tvp].[TypeTest];
    TRUNCATE TABLE [perf].[BulkTest];
    TRUNCATE TABLE [resilience].[RetryTest];
    TRUNCATE TABLE [resilience].[TimeoutTest];
    
    EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';
    
    DBCC CHECKIDENT ('[core].[Users]', RESEED, 0);
    DBCC CHECKIDENT ('[core].[Products]', RESEED, 0);
    DBCC CHECKIDENT ('[core].[Orders]', RESEED, 0);
    DBCC CHECKIDENT ('[tvp].[TypeTest]', RESEED, 0);
    DBCC CHECKIDENT ('[perf].[BulkTest]', RESEED, 0);
    DBCC CHECKIDENT ('[resilience].[RetryTest]', RESEED, 0);
    DBCC CHECKIDENT ('[resilience].[TimeoutTest]', RESEED, 0);
    
    PRINT '모든 테스트 데이터가 초기화되었습니다.';
END;
GO

-- exception Schema
CREATE PROCEDURE [exception].[usp_Exception_DivideByZero]
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Result INT;
    SET @Result = 10 / 0;
    SELECT @Result AS Result;
END
GO

CREATE PROCEDURE [exception].[usp_Exception_ForeignKeyViolation]
    @NonExistentParentId INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [exception].[ChildTable] (ChildId, ParentId, ChildName)
    VALUES (999, @NonExistentParentId, 'Test Child');
END
GO

CREATE PROCEDURE [exception].[usp_Exception_InvalidObjectName]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM [exception].[NonExistentTable];
END
GO

CREATE PROCEDURE [exception].[usp_Exception_UniqueViolation]
    @DuplicateValue NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [exception].[UniqueTable] (UniqueValue) VALUES (@DuplicateValue);
    INSERT INTO [exception].[UniqueTable] (UniqueValue) VALUES (@DuplicateValue);
END
GO

-- perf Schema
CREATE PROCEDURE [perf].[usp_Perf_Bulk_Insert]
    @Items [perf].[Tvp_Perf_BulkInsert] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO [perf].[BulkTest] (BatchNumber, Data)
    SELECT BatchNumber, Data
    FROM @Items;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

CREATE PROCEDURE [perf].[usp_Perf_Query_With_Param]
    @BatchNumber INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT Id, BatchNumber, Data, CreatedAt
    FROM [perf].[BulkTest]
    WHERE BatchNumber = @BatchNumber;
END;
GO

-- resilience Schema
CREATE PROCEDURE [resilience].[usp_Resilience_Simulate_Delay]
    @DelaySeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @DelayTime CHAR(8);
    SET @DelayTime = CONVERT(CHAR(8), DATEADD(SECOND, @DelaySeconds, '00:00:00'), 108);
    WAITFOR DELAY @DelayTime;
    
    INSERT INTO [resilience].[TimeoutTest] (DelaySeconds)
    VALUES (@DelaySeconds);
    
    SELECT 'Delay completed' AS Message;
END;
GO

CREATE PROCEDURE [resilience].[usp_Resilience_Simulate_Failure]
    @FailureRate FLOAT,
    @AttemptNumber INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @RandomValue FLOAT = RAND();
    DECLARE @Success BIT;
    
    IF @RandomValue < @FailureRate
    BEGIN
        SET @Success = 0;
        INSERT INTO [resilience].[RetryTest] (AttemptNumber, SuccessFlag)
        VALUES (@AttemptNumber, @Success);
        
        DECLARE @ErrorMsg NVARCHAR(500);
        SET @ErrorMsg = 'Simulated failure (Rate: ' + 
                        CAST(@FailureRate AS NVARCHAR(20)) + 
                        ', Random: ' + 
                        CAST(@RandomValue AS NVARCHAR(20)) + ')';
        RAISERROR(@ErrorMsg, 16, 1);
    END
    ELSE
    BEGIN
        SET @Success = 1;
        INSERT INTO [resilience].[RetryTest] (AttemptNumber, SuccessFlag)
        VALUES (@AttemptNumber, @Success);
        
        SELECT 'Success' AS Message;
    END
END;
GO

-- tvp Schema
-- ⚠️ 주의: TVP 컬럼 순서가 알파벳 순서로 변경됨
CREATE PROCEDURE [tvp].[usp_Tvp_Bulk_Insert_AllTypes]
    @Types [tvp].[Tvp_Tvp_AllTypes] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    -- TVP 알파벳 순서: DateOnlyValue, DecimalValue, GuidValue, HalfValue, TimeOnlyValue
    INSERT INTO [tvp].[TypeTest] (
        DateOnlyValue, TimeOnlyValue, HalfValue,
        GuidValue, DecimalValue
    )
    SELECT 
        DateOnlyValue, TimeOnlyValue, HalfValue,
        GuidValue, DecimalValue
    FROM @Types;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

CREATE PROCEDURE [tvp].[usp_Tvp_Get_AllTypes]
    @Top INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT TOP (@Top)
        Id, DateOnlyValue, TimeOnlyValue, HalfValue,
        GuidValue, DecimalValue,
        NullableDateOnly, NullableTimeOnly, NullableHalf,
        CreatedAt
    FROM [tvp].[TypeTest]
    ORDER BY Id DESC;
END;
GO

CREATE PROCEDURE [tvp].[usp_Tvp_Test_Schema_Mismatch]
    @Data [tvp].[Tvp_Tvp_SchemaMismatch] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) AS DataCount FROM @Data;
END;
GO

USE [master]
GO
ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET  READ_WRITE 
GO

PRINT ''
PRINT '✅ LIBDB_VERIFICATION_TEST 데이터베이스 생성 완료!'
PRINT '   - 모든 TVP가 알파벳 순서로 정의되었습니다.'
PRINT '   - Source Generator와 100% 일치합니다.'
PRINT ''
PRINT '변경된 TVP:'
PRINT '   1. core.Tvp_Core_User: Age, Email, UserName'
PRINT '   2. tvp.Tvp_Tvp_AllTypes: DateOnlyValue, DecimalValue, GuidValue, HalfValue, TimeOnlyValue'
PRINT ''
GO
