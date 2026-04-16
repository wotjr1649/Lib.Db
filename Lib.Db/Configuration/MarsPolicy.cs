// ============================================================================
// 파일: Lib.Db/Configuration/MarsPolicy.cs
// 설명: MARS(다중 활성 결과 집합) 정책 열거형
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

namespace Lib.Db.Configuration;

/// <summary>
/// MARS(Multiple Active Result Sets) 정책을 지정합니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// ConnectionString에 <c>MultipleActiveResultSets=True</c> 설정을
/// 라이브러리 수준에서 관리하여 사용자 편의를 향상합니다.
/// </para>
/// </summary>
public enum MarsPolicy
{
    /// <summary>
    /// MARS를 사용하지 않습니다.
    /// <para>QueryMultipleAsync 호출 시 <see cref="System.InvalidOperationException"/> 발생.</para>
    /// </summary>
    Disabled,

    /// <summary>
    /// 자동 감지 모드 (기본값).
    /// <para>QueryMultipleAsync 사용 시 MARS 미설정이면 경고 로그 후 예외를 발생시킵니다.</para>
    /// </summary>
    Auto,

    /// <summary>
    /// 강제 활성화.
    /// <para>AddLibDb() 등록 시점에서 ConnectionString에 MARS가 없으면 자동 주입합니다.</para>
    /// </summary>
    ForceEnable
}
