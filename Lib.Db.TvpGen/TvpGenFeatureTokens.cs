// File: Lib.Db.TvpGen/TvpGenFeatureTokens.cs
#nullable enable

namespace Lib.Db.TvpGen;

#region [TVPGEN 스냅샷 토큰] 고정 계약(Shape-Test 안정화)

/// <summary>
/// 제너레이터가 생성하는 코드에 삽입되는 "스냅샷 토큰" 모음입니다.
/// <para>
/// 테스트는 구현 디테일(코드 레이아웃/헬퍼명/최적화 단계) 대신
/// 이 토큰을 기준으로 트랙/기능 계약을 검증합니다.
/// </para>
/// <para>
/// ⚠️ 이 문자열은 "고정 계약"이므로, 리팩토링 시에도 변경하지 마십시오.
/// </para>
/// </summary>
public static class TvpGenFeatureTokens
{
    /// <summary>Result(조회 매핑) 제너레이터 트랙 토큰</summary>
    public const string ResultTrack5 = "TVPGEN:RESULT:TRACK5";

    /// <summary>TVP(입력) 제너레이터 트랙 토큰</summary>
    public const string TvpTrack5 = "TVPGEN:TVP:TRACK5";

    /// <summary>알고리즘/출력 규약 버전(필요 시 확장)</summary>
    public const string AlgoVersion = "TVPGEN:ALGO:2025-12-18";

    /// <summary>DateTime 타입 매핑 토큰 prefix</summary>
    public const string DateTimeTypeToken = "TVPGEN:DATETIME_TYPE";

    /// <summary>
    /// DateTime vs DateTime2 사용 여부를 나타내는 토큰을 생성합니다.
    /// <para>
    /// ✅ 목적: 스냅샷 테스트에서 DateTime2 옵션 검증 가능
    /// </para>
    /// </summary>
    /// <param name="useDatetime2">DateTime2 사용 여부</param>
    /// <returns>
    /// - true: "TVPGEN:DATETIME_TYPE:DateTime2"
    /// - false: "TVPGEN:DATETIME_TYPE:DateTime"
    /// </returns>
    public static string GetDateTimeToken(bool useDatetime2)
        => $"{DateTimeTypeToken}:{(useDatetime2 ? "DateTime2" : "DateTime")}";
}

#endregion

