// ============================================================================
// 파일: SorterDb/WriteFlowTests.cs
// 설명: LV_ANP_SORTER 소터 흐름 Write SP 테스트 (TrayIn→Barcode→TrayOn→Tiltok→TrayCfm)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

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
        SqlParameter totalOrderQty = OutputInt("@O_T_OQTY");
        SqlParameter itemCode = OutputText("@O_ITEM_CD", 50);
        SqlParameter errorNo = OutputInt("@ERROR_NO");

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
                O_T_OQTY = totalOrderQty,
                O_T_WQTY = OutputInt("@O_T_WQTY"),
                O_T_RQTY = OutputInt("@O_T_RQTY"),
                O_ITEM_CD = itemCode,
                O_ITEM_STYLE = OutputText("@O_ITEM_STYLE", 50),
                O_ITEM_COLOR = OutputText("@O_ITEM_COLOR", 50),
                O_ITEM_SIZE = OutputText("@O_ITEM_SIZE", 50),
                O_ITEM_NM = OutputText("@O_ITEM_NM", 100),
                O_SORT_TYPE = OutputText("@O_SORT_TYPE", 50),
                O_SKU_OQTY = OutputInt("@O_SKU_OQTY"),
                O_SKU_WQTY = OutputInt("@O_SKU_WQTY"),
                O_SKU_RQTY = OutputInt("@O_SKU_RQTY"),
                ERROR_NO = errorNo
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);
        // Fluent API로 SP + OUTPUT 매개변수 실행 검증
        result.IsSuccess.Should().BeTrue();
        Convert.ToInt32(totalOrderQty.Value).Should().Be(0);
        itemCode.Value.ToString().Should().Be("TEST_ITEM");
        Convert.ToInt32(errorNo.Value).Should().Be(0);
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

    private static SqlParameter OutputInt(string name)
        => new(name, SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };

    private static SqlParameter OutputText(string name, int size)
        => new(name, SqlDbType.NVarChar, size)
        {
            Direction = ParameterDirection.Output
        };
}
