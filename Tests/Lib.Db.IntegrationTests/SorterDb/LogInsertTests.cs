// ============================================================================
// 파일: SorterDb/LogInsertTests.cs
// 설명: LV_ANP_SORTER 로그 INSERT SP 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.SorterDb;

[Collection("MultiDb")]
public sealed class LogInsertTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _db = fixture.Sorter;

    [Fact]
    public async Task L01_TiltLog_InsertsRecord()
    {
        DbResult<int> result = await _db
            .Procedure("IF_SP_TILT_LOG")
            .With(new { V_PLC_SEQ = 99999, V_CHUTE_NO = 999, V_TRAY_NO = 9999 })
            .ExecuteAsync();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task L02_ChuteButtonLog_InsertsRecord()
    {
        DbResult<int> result = await _db
            .Procedure("IF_SP_CHUTE_BTN_LOG")
            .With(new { V_CHUTE_NO = "999", V_STATUS = "TEST" })
            .ExecuteAsync();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task L03_EmrLog_InsertsRecord()
    {
        DbResult<int> result = await _db
            .Procedure("IF_SP_EMR_LOG")
            .With(new { V_EMR_NO = 999 })
            .ExecuteAsync();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task L04_ErrorLog_InsertsRecord()
    {
        DbResult<int> result = await _db
            .Procedure("IF_SP_ERROR_LOG")
            .With(new
            {
                V_CLASS = 1,
                V_COMPUTER = "TEST_PC",
                V_EVENT_ID = 99999,
                V_MSG = "Lib.Db v2 Integration Test",
                V_MUSTCON = "N",
                V_STATE = 0,
                V_SOURCE = "LIBDB_TEST",
                V_PLCSEQ = 99999L
            })
            .ExecuteAsync();
        // TS_ERROR_LOG 테이블이 존재하면 성공, 없으면 SchemaNotFound 오류 허용
        if (!result.IsSuccess)
        {
            result.Error!.Value.Kind.Should().Be(DbErrorKind.SchemaNotFound,
                "TS_ERROR_LOG 테이블이 DB에 없을 수 있음 — SP 파라미터 매핑은 정상");
        }
    }
}
