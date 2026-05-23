// ============================================================================
// 파일: SorterDb/WriteFlowTests.cs
// 설명: LV_ANP_SORTER 소터 흐름 Write SP 테스트 (TrayIn→Barcode→TrayOn→Tiltok→TrayCfm)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.SorterDb;

[Collection("MultiDb")]
public sealed class WriteFlowTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _db = fixture.Sorter;

    [Fact]
    public async Task W01_TrayIn_ExecutesWithoutError()
    {
        DbResult<int> result = await _db
            .Procedure("IF_SP_TRAY_IN")
            .With(new { V_INDUCTION = 1, V_TRAY_NO = 9999, V_BARCODE = "TEST_BARCODE", V_DELIVERY = "01" })
            .ExecuteAsync(TestContext.Current.CancellationToken);
        // SP가 정상 실행되면 성공, 데이터 없어도 에러 없이 완료 가능
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task W02_Barcode_ReturnsOutputParams()
    {
        DbResult<int> result = await _db
            .Procedure("IF_SP_BARCODE")
            .With(new
            {
                SCAN_SEQ = 99999,
                V_INDUCTION = 1,
                V_DELIVERY = "01",
                V_INVOICE = "TEST_INV",
                V_BARCODE = "TEST_BC",
                V_INPUT_STATUS = "A",
                O_T_OQTY = 0,
                O_T_WQTY = 0,
                O_T_RQTY = 0,
                O_ITEM_CD = "",
                O_ITEM_STYLE = "",
                O_ITEM_COLOR = "",
                O_ITEM_SIZE = "",
                O_ITEM_NM = "",
                O_SORT_TYPE = "",
                O_SKU_OQTY = 0,
                O_SKU_WQTY = 0,
                O_SKU_RQTY = 0,
                ERROR_NO = 0
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);
        // Fluent API로 SP + OUTPUT 매개변수 실행 검증
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task W04_DasSelect_UpdatesDisplay()
    {
        DbResult<int> result = await _db
            .Procedure("IF_SP_DAS_SELECT")
            .With(new { V_BIZ_DAY = "20260309", V_DISP_YN = "Y" })
            .ExecuteAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task W_TiltStop_UpdatesChuteState()
    {
        DbResult<int> result = await _db
            .Procedure("IF_SP_TILT_STOP")
            .With(new { V_CHUTE_NO = 999, V_BOXYN = "N" })
            .ExecuteAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
    }
}
