-- ============================================================================
-- File: verify-libdb-chaos-server-optin.sql
-- Purpose: Optional server-scoped chaos observer verification.
-- Scope: SERVER. Do not include this file in the default DB verify flow.
-- Secret: set SQLCMDPASSWORD in the environment before running sqlcmd.
-- Run: sqlcmd -S localhost -U SA -No -b -v EnableServerChaos=1 -i verify-libdb-chaos-server-optin.sql
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
    THROW 51410, N'EnableServerChaos=1 is required for server-level chaos verification.', 1;

IF COALESCE(@ChaosDatabaseName, N'') = N''
    SET @ChaosDatabaseName = N'LIBDB_CHAOS_TEST';

IF COALESCE(@ChaosSessionName, N'') = N''
    SET @ChaosSessionName = N'libdb_chaos_observer';

IF @ChaosDatabaseName <> N'LIBDB_CHAOS_TEST'
    THROW 51411, N'This opt-in verification is restricted to LIBDB_CHAOS_TEST.', 1;

DECLARE @Failures TABLE ([Scope] NVARCHAR(120) NOT NULL, [Detail] NVARCHAR(4000) NOT NULL);

IF DB_ID(@ChaosDatabaseName) IS NULL
    INSERT INTO @Failures VALUES (N'database', N'LIBDB_CHAOS_TEST does not exist.');

IF NOT EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE [name] = @ChaosSessionName)
    INSERT INTO @Failures VALUES (N'xevent-definition', N'Server event session is missing.');

IF NOT EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE [name] = @ChaosSessionName)
    INSERT INTO @Failures VALUES (N'xevent-running', N'Server event session is not running.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.dm_xe_sessions AS sessions
    INNER JOIN sys.dm_xe_session_targets AS targets
        ON targets.[event_session_address] = sessions.[address]
    WHERE sessions.[name] = @ChaosSessionName
      AND targets.[target_name] = N'ring_buffer'
)
    INSERT INTO @Failures VALUES (N'xevent-target', N'Ring buffer target is missing or inactive.');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.dm_xe_session_events AS events
    INNER JOIN sys.dm_xe_sessions AS sessions
        ON sessions.[address] = events.[event_session_address]
    WHERE sessions.[name] = @ChaosSessionName
      AND events.[name] IN (N'error_reported', N'attention', N'xml_deadlock_report')
)
    INSERT INTO @Failures VALUES (N'xevent-events', N'Expected chaos observer events are missing.');

IF EXISTS (SELECT 1 FROM @Failures)
BEGIN
    SELECT [Scope], [Detail] FROM @Failures ORDER BY [Scope], [Detail];
    DECLARE @Message NVARCHAR(2048) = CONCAT(N'LIBDB_CHAOS_TEST server opt-in verification failed: ', (SELECT COUNT(*) FROM @Failures), N' issue(s).');
    THROW 51412, @Message, 1;
END;

SELECT N'LIBDB_CHAOS_TEST server-level chaos observer verified.' AS [Result],
       @ChaosSessionName AS [EventSessionName],
       @ChaosDatabaseName AS [TargetDatabaseName],
       DB_ID(@ChaosDatabaseName) AS [TargetDatabaseId];
GO
