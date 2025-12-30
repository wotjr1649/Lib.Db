// ============================================================================
// TestDatabaseFixture.cs
// 목적: xUnit 테스트를 위한 DB 컨텍스트 및 DI 설정
// 대상: .NET 10 / C# 14
// 수정: 테스트 시작 시 기본 데이터 자동 삽입 기능 추가
// ============================================================================

using Lib.Db.Contracts.Entry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Lib.Db.Verification.Tests;

/// <summary>
/// 모든 테스트 클래스에서 공유하는 데이터베이스 컨텍스트 Fixture입니다.
/// 테스트 시작 시 DB 초기화 + 기본 데이터 자동 삽입을 수행합니다.
/// </summary>
public class TestDatabaseFixture : IAsyncLifetime
{
    public IServiceProvider Services { get; private set; } = null!;
    public IDbContext Db { get; private set; } = null!;
    
    public IConfiguration Configuration => _configuration;
    private readonly IConfiguration _configuration;

    public TestDatabaseFixture()
    {
        // [핵심 솔루션] 콘솔 출력 인코딩을 UTF-8로 강제 설정
        // Windows CMD/PowerShell 및 VS Test Output 창에서의 한글 깨짐 방지
        Console.OutputEncoding = Encoding.UTF8;

        // appsettings.json 로드
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        // DI Container 구성
        var services = new ServiceCollection();
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        
        // Configuration
        services.AddSingleton(_configuration);
        
        // Lib.Db 서비스 등록
        // [중요] IConfigurationSection을 직접 바인딩해야 IOptionsMonitor가 변경 사항을 실시간으로 감지함
        services.Configure<Lib.Db.Configuration.LibDbOptions>(_configuration.GetSection("LibDb"));
        
        services.AddHighPerformanceDb(_ => {});
        
        // [Integration Phase 4] Override IResumableStateStore with Real DB implementation
        services.RemoveAll(typeof(Lib.Db.Contracts.Execution.IResumableStateStore));
        services.AddSingleton<Lib.Db.Contracts.Execution.IResumableStateStore>(sp => 
            new Lib.Db.Verification.Tests.Infrastructure.TestSqlResumableStateStore(
                _configuration["LibDb:ConnectionStrings:Default"]!));

        // [Fix] Register SchemaFlushService for manual trigger tests
        services.AddTransient<Lib.Db.Schema.SchemaFlushService>();
        
        Services = services.BuildServiceProvider();
        Db = Services.GetRequiredService<IDbContext>();
        
        // [Fix] Register SchemaFlushService manually for testing
        // Note: In real app, this might be internal or registered via AddHighPerformanceDb
        // Use reflection or ensure visibility if needed, but here we assume it's accessible or we fix the test to request interface.
        // Actually, let's create the TVP type first.
        
        // 3. 테스트 데이터 시드 (Clean & Seed)
        await ResetAndSeedAsync();
        
        // [Fix] Create valid TVP for testing
        await CreateTvpObjectsAsync();

        // 4. (For AdvancedQueryTests) Ensure [adv] schema and objects exist
        await CreateAdvSchemaObjectsAsync();

        // 5. (For ExceptionHandlingTests) Ensure [exception] schema and objects exist
        await CreateExceptionSchemaObjectsAsync();

        // 6. Ensure [core] schema and objects exist (Integration MVP)
        await CreateCoreSchemaObjectsAsync();

        // 7. Ensure [perf] schema and objects exist (Integration Phase 2)
        await CreatePerfSchemaObjectsAsync();

        // 8. Ensure [core].CursorState exists (Integration Phase 4)
        await CreateCursorStateTableAsync();
    }

    private async Task CreateCursorStateTableAsync()
    {
        // Setup.sql Update Reflection
        var sql = @"
If NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CursorState' AND schema_id = SCHEMA_ID('core'))
BEGIN
    CREATE TABLE [core].[CursorState](
	    [InstanceHash] [varchar](100) NOT NULL,
	    [QueryKey] [varchar](100) NOT NULL,
	    [CursorValue] [nvarchar](max) NULL,
	    [UpdatedAt] [datetime2](7) NOT NULL DEFAULT SYSDATETIME(),
    PRIMARY KEY CLUSTERED 
    (
	    [InstanceHash] ASC,
	    [QueryKey] ASC
    ));
END";
        await Db.Default.Sql(sql).ExecuteAsync();
    }

    private async Task CreateAdvSchemaObjectsAsync()
    {
        // 1. Schema Creation
        await Db.Default.Sql(@"
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'adv')
            BEGIN
                EXEC('CREATE SCHEMA [adv]')
            END").ExecuteAsync();

        // 2. Table Creation
        await Db.Default.Sql(@"
            IF OBJECT_ID('[adv].[ResumableLogs]', 'U') IS NULL
            BEGIN
                CREATE TABLE [adv].[ResumableLogs] (
                    LogId INT IDENTITY(1,1) PRIMARY KEY,
                    Message NVARCHAR(100),
                    CreatedAt DATETIME2 NOT NULL
                )
            END").ExecuteAsync();

        // 3. Proc 1: usp_Adv_OutputParameters
        // Note: Creating separate batches for Procs as they must be first statement
        await Db.Default.Sql(@"
            CREATE OR ALTER PROCEDURE [adv].[usp_Adv_OutputParameters]
                @InputVal INT,
                @OutputVal INT OUTPUT,
                @InOutVal INT OUTPUT
            AS
            BEGIN
                SET @OutputVal = @InputVal * 2;
                SET @InOutVal = @InOutVal + @InputVal;
                RETURN @InputVal;
            END").ExecuteAsync();

        // 4. Proc 2: usp_Adv_GenerateLogs
        await Db.Default.Sql(@"
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
            END").ExecuteAsync();
    }

    private async Task CreateExceptionSchemaObjectsAsync()
    {
        // 1. Schema Creation
        await Db.Default.Sql(@"
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'exception')
            BEGIN
                EXEC('CREATE SCHEMA [exception]')
            END").ExecuteAsync();

        // 2. ParentTable Creation
        await Db.Default.Sql(@"
            IF OBJECT_ID('[exception].[ParentTable]', 'U') IS NULL
            BEGIN
                CREATE TABLE [exception].[ParentTable] (
                    ParentId INT PRIMARY KEY,
                    ParentName NVARCHAR(100) NOT NULL
                )
            END").ExecuteAsync();

        // 3. ChildTable Creation (with FK)
        await Db.Default.Sql(@"
            IF OBJECT_ID('[exception].[ChildTable]', 'U') IS NULL
            BEGIN
                CREATE TABLE [exception].[ChildTable] (
                    ChildId INT PRIMARY KEY,
                    ParentId INT NOT NULL,
                    ChildName NVARCHAR(100) NOT NULL,
                    CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES [exception].[ParentTable](ParentId)
                )
            END").ExecuteAsync();

        // 4. UniqueTable Creation
        await Db.Default.Sql(@"
            IF OBJECT_ID('[exception].[UniqueTable]', 'U') IS NULL
            BEGIN
                CREATE TABLE [exception].[UniqueTable] (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    UniqueValue NVARCHAR(100) NOT NULL UNIQUE,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
                )
            END").ExecuteAsync();

        // 5. SP: Foreign Key Violation
        await Db.Default.Sql(@"
            CREATE OR ALTER PROCEDURE [exception].[usp_Exception_ForeignKeyViolation]
                @NonExistentParentId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [exception].[ChildTable] (ChildId, ParentId, ChildName)
                VALUES (999, @NonExistentParentId, 'Test Child');
            END").ExecuteAsync();

        // 6. SP: Unique Violation
        await Db.Default.Sql(@"
            CREATE OR ALTER PROCEDURE [exception].[usp_Exception_UniqueViolation]
                @DuplicateValue NVARCHAR(100)
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [exception].[UniqueTable] (UniqueValue) VALUES (@DuplicateValue);
                INSERT INTO [exception].[UniqueTable] (UniqueValue) VALUES (@DuplicateValue);
            END").ExecuteAsync();

        // 7. SP: Invalid Object Name
        await Db.Default.Sql(@"
            CREATE OR ALTER PROCEDURE [exception].[usp_Exception_InvalidObjectName]
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT * FROM [exception].[NonExistentTable];
            END").ExecuteAsync();

        // 8. SP: Divide by Zero
        await Db.Default.Sql(@"
            CREATE OR ALTER PROCEDURE [exception].[usp_Exception_DivideByZero]
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @Result INT;
                SET @Result = 10 / 0;
                SELECT @Result AS Result;
            END").ExecuteAsync();

        // 9. Initial Data
        await Db.Default.Sql(@"
            DELETE FROM [exception].[ChildTable];
            DELETE FROM [exception].[ParentTable];
            DELETE FROM [exception].[UniqueTable];
            INSERT INTO [exception].[ParentTable] (ParentId, ParentName)
            VALUES (1, 'Parent 1'), (2, 'Parent 2');
        ").ExecuteAsync();
    }

    private async Task CreateCoreSchemaObjectsAsync()
    {
        // 1. Schema Creation
        await Db.Default.Sql(@"
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'core')
            BEGIN
                EXEC('CREATE SCHEMA [core]')
            END").ExecuteAsync();

        // 2. Table Creation
        await Db.Default.Sql(@"
            IF OBJECT_ID('[core].[Users]', 'U') IS NULL
            BEGIN
                CREATE TABLE [core].[Users] (
                    UserId INT IDENTITY(1,1) PRIMARY KEY,
                    UserName NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(255) NOT NULL UNIQUE,
                    Age INT NULL,
                    CreatedAt DATETIME2 DEFAULT SYSDATETIME()
                )
            END").ExecuteAsync();

        // 3. Proc: Insert_User
        await Db.Default.Sql(@"
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
            END").ExecuteAsync();

        // 4. Proc: Get_User
        await Db.Default.Sql(@"
            CREATE OR ALTER PROCEDURE [core].[usp_Core_Get_User]
                @UserId INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT UserId, UserName, Email, Age, CreatedAt
                FROM [core].[Users]
                WHERE UserId = @UserId;
            END").ExecuteAsync();
    }

    private async Task CreatePerfSchemaObjectsAsync()
    {
        // 1. Schema Creation
        await Db.Default.Sql(@"
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'perf')
            BEGIN
                EXEC('CREATE SCHEMA [perf]')
            END").ExecuteAsync();

        // 2. Table Creation
        await Db.Default.Sql(@"
            IF OBJECT_ID('[perf].[BulkTest]', 'U') IS NULL
            BEGIN
                CREATE TABLE [perf].[BulkTest] (
                    [Id] [bigint] IDENTITY(1,1) PRIMARY KEY,
                    [BatchNumber] [int] NOT NULL,
                    [Data] [nvarchar](500) NULL,
                    [CreatedAt] [datetime2](7) NULL DEFAULT SYSDATETIME()
                )
            END").ExecuteAsync();

        // 3. TVP Type Creation
        await Db.Default.Sql(@"
            IF TYPE_ID('[perf].[Tvp_Perf_BulkInsert]') IS NULL
            BEGIN
                CREATE TYPE [perf].[Tvp_Perf_BulkInsert] AS TABLE (
                    [BatchNumber] [int] NOT NULL,
                    [Data] [nvarchar](500) NULL
                )
            END").ExecuteAsync();

        // 4. Proc: Bulk Insert
        await Db.Default.Sql(@"
            CREATE OR ALTER PROCEDURE [perf].[usp_Perf_Bulk_Insert]
                @Items [perf].[Tvp_Perf_BulkInsert] READONLY
            AS
            BEGIN
                SET NOCOUNT ON;
                INSERT INTO [perf].[BulkTest] (BatchNumber, Data)
                SELECT BatchNumber, Data FROM @Items;
                SELECT @@ROWCOUNT AS RowsAffected;
            END").ExecuteAsync();

        // 5. Proc: Query With Param
        await Db.Default.Sql(@"
            CREATE OR ALTER PROCEDURE [perf].[usp_Perf_Query_With_Param]
                @BatchNumber INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT Id, BatchNumber, Data, CreatedAt
                FROM [perf].[BulkTest]
                WHERE BatchNumber = @BatchNumber;
            END").ExecuteAsync();
    }

    private async Task CreateTvpObjectsAsync()
    {
        // 1. Schema Creation
        await Db.Default.Sql(@"
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'tvp')
            BEGIN
                EXEC('CREATE SCHEMA [tvp]')
            END").ExecuteAsync();

        // 2. TVP Type Creation
        await Db.Default.Sql(@"
            IF TYPE_ID('[tvp].[TypeTest]') IS NULL
            BEGIN
                CREATE TYPE [tvp].[TypeTest] AS TABLE (
                    Id INT,
                    Name NVARCHAR(50),
                    Value DECIMAL(18,2)
                )
            END").ExecuteAsync();
    }

    public async Task DisposeAsync()
    {
        // 테스트 종료 시 정리 (선택적)
        // await ResetAllDataAsync();
        
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// DB 초기화 및 기본 Seed 데이터 삽입
    /// </summary>
    private async Task ResetAndSeedAsync()
    {
        // 1단계: 모든 데이터 삭제
        await ResetAllDataAsync();
        
        // 2단계: 기본 Seed 데이터 삽입
        await SeedBaseDataAsync();
    }

    /// <summary>
    /// 모든 테이블 데이터 삭제 및 IDENTITY 리셋
    /// </summary>
    private async Task ResetAllDataAsync()
    {
        try
        {
            await Db.Default.Sql(@"
                -- FK 제약 순서 고려하여 삭제
                DELETE FROM [core].[Orders];
                DELETE FROM [core].[Users];
                DELETE FROM [core].[Products];
                DELETE FROM [tvp].[TypeTest];
                DELETE FROM [perf].[BulkTest];
                DELETE FROM [resilience].[RetryTest];
                DELETE FROM [resilience].[TimeoutTest];

                -- IDENTITY 리셋 (1부터 시작)
                DBCC CHECKIDENT ('[core].[Users]', RESEED, 0);
                DBCC CHECKIDENT ('[core].[Products]', RESEED, 0);
                DBCC CHECKIDENT ('[core].[Orders]', RESEED, 0);
                DBCC CHECKIDENT ('[tvp].[TypeTest]', RESEED, 0);
                DBCC CHECKIDENT ('[perf].[BulkTest]', RESEED, 0);
                DBCC CHECKIDENT ('[resilience].[RetryTest]', RESEED, 0);
                DBCC CHECKIDENT ('[resilience].[TimeoutTest]', RESEED, 0);
            ").ExecuteAsync();
        }
        catch
        {
            // 초기화 실패 무시 (첫 실행 시 테이블이 없을 수 있음)
        }
    }

    /// <summary>
    /// 기본 Seed 데이터 삽입 (모든 테스트에서 공통으로 사용)
    /// </summary>
    private async Task SeedBaseDataAsync()
    {
        try
        {
            // [core] 스키마: 기본 사용자 3명 (중복 방지)
            await Db.Default.Sql(@"
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
            ").ExecuteAsync();
        }
        catch (Exception ex)
        {
            // Seed 실패 시 로깅 (테스트는 계속 진행)
            Console.WriteLine($"[WARN] Seed 데이터 삽입 실패: {ex.Message}");
        }
    }
}

/// <summary>
/// 모든 테스트 클래스가 TestDatabaseFixture를 공유하도록 Collection 정의
/// </summary>
[CollectionDefinition("Database Collection")]
public class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>
{
    // xUnit이 자동으로 인스턴스화하므로 구현 불필요
}
