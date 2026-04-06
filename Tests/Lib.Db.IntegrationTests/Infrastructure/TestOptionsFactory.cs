// ============================================================================
// 파일: Infrastructure/TestOptionsFactory.cs
// 설명: 테스트용 LibDbOptions 생성 헬퍼 (최소 유효 설정)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Configuration;

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// 테스트용으로 유효한 LibDbOptions 인스턴스를 생성하는 정적 팩토리 클래스입니다.
/// ConnectionStrings 필수 설정을 포함하여 OptionsValidationException을 방지합니다.
/// </summary>
public static class TestOptionsFactory
{
    /// <summary>
    /// 최소한의 유효한 설정을 가진 LibDbOptions를 생성합니다.
    /// </summary>
    public static LibDbOptions CreateValidOptions()
    {
        return new LibDbOptions
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] =
                    "Server=127.0.0.1;Database=LIBDB_VERIFICATION_TEST;User Id=sa;Password=123456;TrustServerCertificate=True;Encrypt=False;",
                ["Admin"] =
                    "Server=127.0.0.1;Database=LIBDB_VERIFICATION_TEST;User Id=sa;Password=123456;TrustServerCertificate=True;Encrypt=False;"
            },
            EnableSharedMemoryCache = false
        };
    }

    /// <summary>
    /// 유효한 기본 옵션을 생성한 뒤, 호출자가 지정한 설정으로 덮어씁니다.
    /// </summary>
    public static LibDbOptions CreateValidWithOverrides(Action<LibDbOptions> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        LibDbOptions options = CreateValidOptions();
        configure(options);
        return options;
    }

    /// <summary>
    /// ConnectionStrings만 포함하는 최소 옵션을 생성합니다. (그 외 설정 없음)
    /// </summary>
    public static LibDbOptions CreateMinimal()
    {
        return new LibDbOptions
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] =
                    "Server=127.0.0.1;Database=TEST;User Id=sa;Password=123456;TrustServerCertificate=True;Encrypt=False;"
            }
        };
    }
}
