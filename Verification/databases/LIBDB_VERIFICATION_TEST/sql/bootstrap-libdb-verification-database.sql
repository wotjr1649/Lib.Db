-- ============================================================================
-- File: bootstrap-libdb-verification-database.sql
-- Purpose: Create the empty LIBDB_VERIFICATION_TEST database before IDbSession opens it.
-- Run after: SQL Server is available.
-- Notes:
--   - Keep this script database-only. Schema, TVP types, and procedures are owned by SchemaInitializer.
-- ============================================================================

USE [master];
GO

IF DB_ID(N'LIBDB_VERIFICATION_TEST') IS NULL
BEGIN
    CREATE DATABASE [LIBDB_VERIFICATION_TEST];
END;
GO

ALTER DATABASE [LIBDB_VERIFICATION_TEST] SET QUERY_STORE = ON;
GO
