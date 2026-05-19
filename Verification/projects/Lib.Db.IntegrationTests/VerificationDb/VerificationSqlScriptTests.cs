// ============================================================================
// 파일: VerificationDb/VerificationSqlScriptTests.cs
// 설명: LIBDB_VERIFICATION_TEST 독립 검증 SQL 스크립트 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class VerificationSqlScriptTests(MultiDbFixture fixture)
{
    [Fact]
    public async Task VerifySqlScript_ShouldExecuteRepresentativeVerificationChecks()
    {
        string connectionString = fixture.GetConnectionString(TestConnectionStrings.Verification);

        await SqlScriptRunner.ExecuteScriptAsync(
            connectionString,
            "verify-libdb-verification-test.sql",
            TestContext.Current.CancellationToken);
    }
}
