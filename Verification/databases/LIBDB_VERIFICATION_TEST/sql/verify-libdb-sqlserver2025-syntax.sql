-- ============================================================================
-- File: verify-libdb-sqlserver2025-syntax.sql
-- Purpose: Feature probes for current and SQL Server 2025 JSON/T-SQL syntax.
-- Target DB: LIBDB_VERIFICATION_TEST
-- Run after: setup-libdb-verification-test.sql
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -N o -i verify-libdb-sqlserver2025-syntax.sql -f 65001 -b
-- ============================================================================

USE [LIBDB_VERIFICATION_TEST];
GO

SET NOCOUNT ON;

DECLARE @FeatureProbe TABLE
(
    [FeatureName] NVARCHAR(120) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL,
    [Detail] NVARCHAR(4000) NULL
);

BEGIN TRY
    EXEC sys.sp_executesql N'
        DECLARE @json NVARCHAR(MAX) = N''{"items":[{"id":1},{"id":2}],"name":"probe"}'';
        SELECT [id] FROM OPENJSON(@json, N''$.items'') WITH ([id] INT N''$.id'');
    ';
    INSERT INTO @FeatureProbe VALUES (N'OPENJSON_WITH', N'PASS', N'OPENJSON WITH projection compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'OPENJSON_WITH', N'FAIL', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        DECLARE @json NVARCHAR(MAX) = N''{"name":"probe","meta":{"v":1}}'';
        SELECT JSON_VALUE(@json, N''$.name'') AS [Name], JSON_QUERY(@json, N''$.meta'') AS [Meta];
    ';
    INSERT INTO @FeatureProbe VALUES (N'JSON_VALUE_QUERY', N'PASS', N'JSON_VALUE and JSON_QUERY compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'JSON_VALUE_QUERY', N'FAIL', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        DECLARE @json NVARCHAR(MAX) = N''{"name":"probe"}'';
        SELECT JSON_MODIFY(@json, N''$.value'', 230) AS [ModifiedJson];
    ';
    INSERT INTO @FeatureProbe VALUES (N'JSON_MODIFY', N'PASS', N'JSON_MODIFY compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'JSON_MODIFY', N'FAIL', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        SELECT JSON_OBJECT(''name'':N''probe'', ''value'':230) AS [JsonObject],
               JSON_ARRAY(1, 2, 3) AS [JsonArray];
    ';
    INSERT INTO @FeatureProbe VALUES (N'JSON_OBJECT_ARRAY', N'PASS', N'JSON_OBJECT and JSON_ARRAY compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'JSON_OBJECT_ARRAY', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        SELECT JSON_ARRAYAGG([value]) AS [JsonArrayAgg]
        FROM (VALUES (1), (2), (3)) AS v([value]);
    ';
    INSERT INTO @FeatureProbe VALUES (N'JSON_ARRAYAGG', N'PASS', N'JSON_ARRAYAGG compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'JSON_ARRAYAGG', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        DECLARE @json NVARCHAR(MAX) = N''{"name":"probe"}'';
        SELECT JSON_PATH_EXISTS(@json, N''$.name'') AS [PathExists];
    ';
    INSERT INTO @FeatureProbe VALUES (N'JSON_PATH_EXISTS', N'PASS', N'JSON_PATH_EXISTS compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'JSON_PATH_EXISTS', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        DECLARE @json NVARCHAR(MAX) = N''{"value":230,"tags":["tvp","runtime"]}'';
        SELECT JSON_CONTAINS(@json, 230, N''$.value'') AS [ContainsValue];
    ';
    INSERT INTO @FeatureProbe VALUES (N'JSON_CONTAINS', N'PASS', N'JSON_CONTAINS compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'JSON_CONTAINS', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        DECLARE @j json = CAST(JSON_OBJECT(''name'':N''probe'', ''value'':230) AS json);
        SELECT JSON_VALUE(@j, N''$.name'') AS [Name];
    ';
    INSERT INTO @FeatureProbe VALUES (N'NATIVE_JSON_TYPE', N'PASS', N'native json type compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'NATIVE_JSON_TYPE', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        DECLARE @j json = CAST(JSON_OBJECT(''name'':N''probe'', ''value'':230) AS json);
        SET @j.modify(N''$.value'', 231);
        SELECT JSON_VALUE(@j, N''$.value'') AS [Value];
    ';
    INSERT INTO @FeatureProbe VALUES (N'NATIVE_JSON_MODIFY_METHOD', N'PASS', N'native json modify method compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'NATIVE_JSON_MODIFY_METHOD', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        DROP TABLE IF EXISTS [dbo].[libdb_verify_json_index_probe];
        CREATE TABLE [dbo].[libdb_verify_json_index_probe]
        (
            [Id] INT NOT NULL PRIMARY KEY CLUSTERED,
            [Payload] json NOT NULL
        );
        INSERT INTO [dbo].[libdb_verify_json_index_probe] ([Id], [Payload])
        VALUES (1, CAST(JSON_OBJECT(''name'':N''probe'', ''value'':230) AS json));
        CREATE JSON INDEX [IX_libdb_verify_json_index_probe_Payload]
            ON [dbo].[libdb_verify_json_index_probe] ([Payload])
            FOR (N''$.name'', N''$.value'');
        DROP TABLE [dbo].[libdb_verify_json_index_probe];
    ';
    INSERT INTO @FeatureProbe VALUES (N'CREATE_JSON_INDEX', N'PASS', N'CREATE JSON INDEX compiled and executed.');
END TRY
BEGIN CATCH
    IF OBJECT_ID(N'dbo.libdb_verify_json_index_probe', N'U') IS NOT NULL
        DROP TABLE [dbo].[libdb_verify_json_index_probe];
    INSERT INTO @FeatureProbe VALUES (N'CREATE_JSON_INDEX', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        SELECT 1 AS [RegexMatched]
        WHERE REGEXP_LIKE(N''libdb-v230'', N''^libdb-v[0-9]+$'', N''i'');
    ';
    INSERT INTO @FeatureProbe VALUES (N'REGEXP_LIKE', N'PASS', N'REGEXP_LIKE compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'REGEXP_LIKE', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        SELECT REGEXP_REPLACE(N''libdb-230'', N''[0-9]+'', N''231'') AS [RegexReplaced];
    ';
    INSERT INTO @FeatureProbe VALUES (N'REGEXP_REPLACE', N'PASS', N'REGEXP_REPLACE compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'REGEXP_REPLACE', N'UNSUPPORTED', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    EXEC sys.sp_executesql N'
        DECLARE @rows TABLE ([Id] INT NOT NULL, [Name] NVARCHAR(100) NOT NULL);
        INSERT INTO @rows VALUES (1, N''A''), (2, N''B'');
        SELECT [Id], [Name], ROW_NUMBER() OVER (ORDER BY [Id]) AS [RowNum]
        FROM @rows
        ORDER BY [Id]
        OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY;
    ';
    INSERT INTO @FeatureProbe VALUES (N'OFFSET_WINDOW', N'PASS', N'OFFSET/FETCH and ROW_NUMBER compiled and executed.');
END TRY
BEGIN CATCH
    INSERT INTO @FeatureProbe VALUES (N'OFFSET_WINDOW', N'FAIL', ERROR_MESSAGE());
END CATCH;

SELECT [FeatureName], [Status], [Detail]
FROM @FeatureProbe
ORDER BY [FeatureName];

IF EXISTS (SELECT 1 FROM @FeatureProbe WHERE [Status] = N'FAIL')
BEGIN
    DECLARE @Message NVARCHAR(2048) = CONCAT(N'SQL Server syntax verification failed: ', (SELECT COUNT(*) FROM @FeatureProbe WHERE [Status] = N'FAIL'), N' hard failure(s).');
    THROW 51400, @Message, 1;
END;

SELECT N'SQL Server syntax feature probe completed. UNSUPPORTED rows indicate engine/version-gated syntax, not setup failure.' AS [Result];
GO
