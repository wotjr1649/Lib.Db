-- ============================================================================
-- File: teardown-libdb-chaos-server-optin.sql
-- Purpose: Optional server-scoped chaos observer teardown.
-- Scope: SERVER. Do not include this file in the default DB teardown flow.
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -No -b -v EnableServerChaos=1 -i teardown-libdb-chaos-server-optin.sql
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
    THROW 51420, N'EnableServerChaos=1 is required for server-level chaos teardown.', 1;

IF COALESCE(@ChaosDatabaseName, N'') = N''
    SET @ChaosDatabaseName = N'LIBDB_CHAOS_TEST';

IF COALESCE(@ChaosSessionName, N'') = N''
    SET @ChaosSessionName = N'libdb_chaos_observer';

IF @ChaosDatabaseName <> N'LIBDB_CHAOS_TEST'
    THROW 51421, N'This opt-in teardown is restricted to LIBDB_CHAOS_TEST.', 1;

IF COALESCE(HAS_PERMS_BY_NAME(NULL, NULL, N'ALTER ANY EVENT SESSION'), 0) <> 1
AND COALESCE(HAS_PERMS_BY_NAME(NULL, NULL, N'DROP ANY EVENT SESSION'), 0) <> 1
AND IS_SRVROLEMEMBER(N'sysadmin') <> 1
    THROW 51422, N'ALTER ANY EVENT SESSION, DROP ANY EVENT SESSION, or sysadmin is required.', 1;

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

SELECT N'LIBDB_CHAOS_TEST server-level chaos observer removed.' AS [Result],
       @ChaosSessionName AS [EventSessionName],
       @ChaosDatabaseName AS [TargetDatabaseName];
GO
