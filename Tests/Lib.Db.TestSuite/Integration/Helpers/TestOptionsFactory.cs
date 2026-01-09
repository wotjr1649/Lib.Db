// ============================================================================
// File : Lib.Db.Verification.Tests/Helpers/TestOptionsFactory.cs
// Role : 테스트용 LibDbOptions 생성 헬퍼 (최소 유효 설정)
// Env  : .NET 10 / C# 14
// Notes:
//   - ConnectionStrings 누락으로 인한 OptionsValidationException 방지
//   - 테스트별 커스텀 설정 지원(WithOverrides)
// ============================================================================

#nullable enable

using System;
using System.Collections.Generic;
using Lib.Db.Configuration;

namespace Lib.Db.Verification.Tests.Helpers;

/// <summary>
/// 테스트용으로 유효한 LibDbOptions 인스턴스를 생성하는 정적 팩토리 클래스입니다.
/// ConnectionStrings 필수 설정을 포함하여 OptionsValidationException을 방지합니다.
/// </summary>
public static class TestOptionsFactory
{
    /// <summary>
    /// 최소한의 유효한 설정을 가진 LibDbOptions를 생성합니다.
    /// </summary>
    /// <returns>테스트용 LibDbOptions 인스턴스</returns>
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
            // 기본 테스트 환경에서는 SharedMemoryCache를 비활성화하는 것을 기본값으로 둡니다.
            EnableSharedMemoryCache = false
        };
    }

    /// <summary>
    /// 유효한 기본 옵션을 생성한 뒤, 호출자가 지정한 설정으로 덮어씁니다.
    /// </summary>
    /// <param name="configure">옵션 오버라이드 델리게이트</param>
    /// <returns>커스터마이징된 LibDbOptions 인스턴스</returns>
    public static LibDbOptions CreateValidWithOverrides(Action<LibDbOptions> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var options = CreateValidOptions();
        configure(options);
        return options;
    }

    /// <summary>
    /// ConnectionStrings만 포함하는 최소 옵션을 생성합니다. (그 외 설정 없음)
    /// </summary>
    /// <returns>최소 구성의 LibDbOptions 인스턴스</returns>
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
