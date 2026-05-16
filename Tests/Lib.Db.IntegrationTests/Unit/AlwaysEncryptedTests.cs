// ============================================================================
// 파일: Unit/AlwaysEncryptedTests.cs
// 설명: Always Encrypted 연결 문자열 검증 유닛 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.IntegrationTests.Unit;

/// <summary>
/// <see cref="LibDbOptions.IsAlwaysEncryptedEnabled"/> 메서드의 유닛 테스트.
/// <para><b>[설계 의도]</b> Always Encrypted 연결 문자열 감지가 다양한 시나리오에서
/// 올바르게 동작하는지 검증한다.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlwaysEncryptedTests
{
    #region AE01: Column Encryption Setting=Enabled 포함 시 true

    [Fact]
    public void AE_ConnString_WithColumnEncryption_ShouldDetect()
    {
        string connectionString =
            "Server=localhost;Database=TestDb;Integrated Security=True;Column Encryption Setting=Enabled;";

        bool result = LibDbOptions.IsAlwaysEncryptedEnabled(connectionString);

        result.Should().BeTrue("Column Encryption Setting=Enabled 가 포함된 연결 문자열은 true를 반환해야 합니다.");
    }

    #endregion

    #region AE02: Column Encryption Setting 미포함 시 false

    [Fact]
    public void AE_ConnString_Without_ShouldReturnFalse()
    {
        string connectionString = "Server=localhost;Database=TestDb;Integrated Security=True;";

        bool result = LibDbOptions.IsAlwaysEncryptedEnabled(connectionString);

        result.Should().BeFalse("Column Encryption Setting 이 없는 연결 문자열은 false를 반환해야 합니다.");
    }

    #endregion
}
