-- ============================================================================
-- File: verify-libdb-all.migration-reference.sql
-- Purpose: Migration-only reference for the former Lib.Db v2.3.0 DB verification orchestrator.
-- Usage:
--   cd Verification\databases
--   Set SQLCMDPASSWORD in the environment before running sqlcmd.
--   sqlcmd -S localhost -U SA -N o -i verify-libdb-all.migration-reference.sql -f 65001 -b
-- Notes:
--   - Requires SQLCMD mode because this file uses :r includes.
--   - Run setup-libdb-*.sql files before this orchestrator.
--   - Multi-session chaos/load and .NET-only checks are reported as harness-required notes by per-DB files.
-- ============================================================================

:r .\verify-libdb-verification-test.sql
:r .\verify-libdb-stress-test.sql
:r .\verify-libdb-chaos-test.sql
:r .\verify-libdb-bench-test.sql
:r .\verify-libdb-sqlserver2025-syntax.sql

PRINT N'All Lib.Db v2.3.0 SQL verification files completed.';
GO
