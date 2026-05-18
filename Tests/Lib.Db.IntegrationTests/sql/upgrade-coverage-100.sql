-- ============================================================================
-- 파일: upgrade-coverage-100.sql
-- 설명: Lib.Db v2 테스트 커버리지 85→100 달성을 위한 추가 SP 생성
-- 대상: LIBDB_VERIFICATION_TEST
-- 실행: sqlcmd -S localhost -U sa -P <password> -i upgrade-coverage-100.sql -f 65001 -C
-- ============================================================================

USE [LIBDB_VERIFICATION_TEST];
GO

-- ============================================================================
-- 1. TransactionAborted (SQL 3930) 유발 SP
--    SET XACT_ABORT ON → NOT NULL 위반 → 트랜잭션 DOOMED → COMMIT 시 3930
-- ============================================================================
CREATE OR ALTER PROCEDURE [test].[usp_Simulate_TransactionAborted]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- NOT NULL 위반으로 에러 발생 → XACT_ABORT에 의해 트랜잭션 DOOMED
    INSERT INTO [core].[Users] (UserName, Email)
    VALUES (NULL, 'txabort_test@test.com');

    -- 여기 도달 불가 (XACT_ABORT가 즉시 중단)
    COMMIT TRANSACTION;
END
GO

-- ============================================================================
-- 2. Unknown 에러 (SQL 999) 유발 SP
--    RAISERROR code 999 — DbErrorMapper 매핑 테이블에 없는 코드
--    1-49999 범위이므로 Unknown으로 분류됨
-- ============================================================================
CREATE OR ALTER PROCEDURE [test].[usp_RaiseError_Unknown_999]
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR(N'매핑되지 않은 에러 코드 999 테스트', 16, 1);
END
GO

-- ============================================================================
-- 3. Savepoint 부분 커밋 SP
--    INSERT A → SAVE TRANSACTION → INSERT B → ROLLBACK TO SAVE → COMMIT
--    결과: A만 영속, B는 롤백됨
-- ============================================================================
CREATE OR ALTER PROCEDURE [test].[usp_Savepoint_PartialCommit]
    @EmailA NVARCHAR(255),
    @EmailB NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;

    -- 1단계: User A 삽입 (유지됨)
    INSERT INTO [core].[Users] (UserName, Email, Age)
    VALUES ('SavepointA', @EmailA, 10);

    -- 세이브포인트 설정
    SAVE TRANSACTION SP_AfterA;

    -- 2단계: User B 삽입 (롤백됨)
    INSERT INTO [core].[Users] (UserName, Email, Age)
    VALUES ('SavepointB', @EmailB, 20);

    -- 세이브포인트로 롤백 → B만 취소
    ROLLBACK TRANSACTION SP_AfterA;

    -- 전체 커밋 → A만 영속
    COMMIT TRANSACTION;

    SELECT 'PARTIAL_COMMIT' AS Result;
END
GO

-- ============================================================================
-- 4. SP→SP 조합 V2 (SCOPE_IDENTITY 우회)
--    OUTPUT 절을 사용하여 INSERT된 UserId를 직접 캡처
-- ============================================================================
CREATE OR ALTER PROCEDURE [test].[usp_Composite_V2]
    @UserName NVARCHAR(100),
    @Email NVARCHAR(255),
    @NewUserId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- OUTPUT 절로 INSERT된 ID 직접 캡처 (SCOPE_IDENTITY 범위 문제 우회)
    DECLARE @InsertedIds TABLE (UserId INT);

    INSERT INTO [core].[Users] (UserName, Email)
    OUTPUT INSERTED.UserId INTO @InsertedIds
    VALUES (@UserName, @Email);

    SELECT @NewUserId = UserId FROM @InsertedIds;

    -- 삽입된 사용자 정보 반환
    SELECT UserId, UserName, Email, Age, CreatedAt
    FROM [core].[Users]
    WHERE UserId = @NewUserId;
END
GO

PRINT N'[완료] 4개 SP 생성 성공: usp_Simulate_TransactionAborted, usp_RaiseError_Unknown_999, usp_Savepoint_PartialCommit, usp_Composite_V2';
GO
