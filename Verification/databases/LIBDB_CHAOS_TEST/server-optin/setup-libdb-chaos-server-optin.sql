-- ============================================================================
-- File: setup-libdb-chaos-server-optin.sql
-- Purpose: Optional server-scoped chaos observer for LIBDB_CHAOS_TEST.
-- Scope: SERVER. Do not include this file in the default DB setup flow.
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -No -b -v EnableServerChaos=1 -i setup-libdb-chaos-server-optin.sql
-- ============================================================================

:ON ERROR EXIT
:setvar ChaosDatabaseName "LIBDB_CHAOS_TEST"
:setvar ChaosSessionName "libdb_chaos_observer"

USE [master];
GO

SET NOCOUNT ON;

DECLARE @EnableServerChaos NVARCHAR(20) = N'$(EnableServerChaos)';
DECLARE @ChaosDatabaseName SYSNAME = N'$(ChaosDatabaseName)';
DECLARE @ChaosSessionName SYSNAME = N'$(ChaosSessionName)';

IF @EnableServerChaos <> N'1'
    THROW 51400, N'EnableServerChaos=1 is required for server-level chaos setup.', 1;

IF COALESCE(@ChaosDatabaseName, N'') = N''
    SET @ChaosDatabaseName = N'LIBDB_CHAOS_TEST';

IF COALESCE(@ChaosSessionName, N'') = N''
    SET @ChaosSessionName = N'libdb_chaos_observer';

IF DB_ID(@ChaosDatabaseName) IS NULL
    THROW 51401, N'Target chaos database does not exist.', 1;

IF @ChaosDatabaseName <> N'LIBDB_CHAOS_TEST'
    THROW 51402, N'This opt-in script is restricted to LIBDB_CHAOS_TEST.', 1;

IF COALESCE(HAS_PERMS_BY_NAME(NULL, NULL, N'ALTER ANY EVENT SESSION'), 0) <> 1
AND COALESCE(HAS_PERMS_BY_NAME(NULL, NULL, N'CREATE ANY EVENT SESSION'), 0) <> 1
AND IS_SRVROLEMEMBER(N'sysadmin') <> 1
    THROW 51403, N'ALTER ANY EVENT SESSION, CREATE ANY EVENT SESSION, or sysadmin is required.', 1;

DECLARE @DatabaseId INT = DB_ID(@ChaosDatabaseName);
DECLARE @QuotedSessionName NVARCHAR(260) = QUOTENAME(@ChaosSessionName);

IF EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE [name] = @ChaosSessionName)
BEGIN
    DECLARE @StopSql NVARCHAR(MAX) = N'ALTER EVENT SESSION ' + @QuotedSessionName + N' ON SERVER STATE = STOP;';
    EXEC sys.sp_executesql @StopSql;
END;

IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE [name] = @ChaosSessionName)
BEGIN
    DECLARE @DropSql NVARCHAR(MAX) = N'DROP EVENT SESSION ' + @QuotedSessionName + N' ON SERVER;';
    EXEC sys.sp_executesql @DropSql;
END;

DECLARE @CreateSql NVARCHAR(MAX) = N'
CREATE EVENT SESSION ' + @QuotedSessionName + N' ON SERVER
ADD EVENT sqlserver.error_reported
(
    ACTION
    (
        sqlserver.client_app_name,
        sqlserver.database_id,
        sqlserver.session_id,
        sqlserver.sql_text,
        sqlserver.username
    )
    WHERE
    (
        [database_id] = (' + CONVERT(NVARCHAR(20), @DatabaseId) + N')
        AND
        (
            [error_number] = (1205)
            OR [error_number] = (1222)
            OR [severity] >= (11)
        )
    )
),
ADD EVENT sqlserver.attention
(
    ACTION
    (
        sqlserver.client_app_name,
        sqlserver.database_id,
        sqlserver.session_id,
        sqlserver.sql_text,
        sqlserver.username
    )
),
ADD EVENT sqlserver.xml_deadlock_report
ADD TARGET package0.ring_buffer
(
    SET MAX_EVENTS_LIMIT = (200),
        MAX_MEMORY = (1024)
)
WITH
(
    MAX_MEMORY = 2 MB,
    EVENT_RETENTION_MODE = ALLOW_SINGLE_EVENT_LOSS,
    MAX_DISPATCH_LATENCY = 5 SECONDS,
    TRACK_CAUSALITY = ON,
    STARTUP_STATE = OFF
);';

EXEC sys.sp_executesql @CreateSql;

DECLARE @StartSql NVARCHAR(MAX) = N'ALTER EVENT SESSION ' + @QuotedSessionName + N' ON SERVER STATE = START;';
EXEC sys.sp_executesql @StartSql;

SELECT N'LIBDB_CHAOS_TEST server-level chaos observer started.' AS [Result],
       @ChaosSessionName AS [EventSessionName],
       @ChaosDatabaseName AS [TargetDatabaseName],
       @DatabaseId AS [TargetDatabaseId];
GO
