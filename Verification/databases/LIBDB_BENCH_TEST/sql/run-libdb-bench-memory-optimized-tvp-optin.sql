-- ============================================================================
-- File: run-libdb-bench-memory-optimized-tvp-optin.sql
-- Purpose: Direct sqlcmd orchestrator for the LIBDB_BENCH_TEST memory-optimized TVP opt-in smoke.
-- Scope: DATABASE. Restricted by included files to LIBDB_BENCH_TEST.
-- Run:
--   cd Verification\databases\LIBDB_BENCH_TEST\sql
--   Set SQLCMDPASSWORD in the environment before running sqlcmd.
--   sqlcmd -S localhost -U SA -N o -b -i run-libdb-bench-memory-optimized-tvp-optin.sql -f 65001
-- ============================================================================

:ON ERROR EXIT

:r .\setup-libdb-bench-memory-optimized-tvp-optin.sql
:r .\verify-libdb-bench-memory-optimized-tvp-optin.sql

PRINT N'LIBDB_BENCH_TEST memory-optimized TVP opt-in sqlcmd smoke completed.';
GO
