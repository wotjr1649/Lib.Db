#nullable enable

using System;
using Polly;

namespace Lib.Db.Contracts.Infrastructure;

#region 회복 탄력성 파이프라인 제공자

/// <summary>
/// 설정된 회복 탄력성(Resilience) 파이프라인을 제공하는 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>조건부 활성화</b>: 불필요한 파이프라인 오버헤드(비동기 상태 머신 할당 등)를 방지하기 위해, 활성화 여부(<see cref="IsEnabled"/>)를 미리 검사할 수 있게 합니다.<br/>
/// - <b>중앙 집중 관리</b>: 재시도, 회로 차단기 등 복잡한 정책 구성을 모듈화하여 실행 로직에서 분리합니다.
/// </para>
/// </summary>
public interface IResiliencePipelineProvider
{
    /// <summary>
    /// 회복 탄력성 기능이 활성화되었는지 여부를 반환합니다.
    /// <para>
    /// Hot-Path 최적화를 위해 사용됩니다.
    /// 이 값이 <c>false</c>이면 실행 전략은 파이프라인을 완전히 우회해야 합니다.
    /// </para>
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 설정된 Polly 회복 탄력성 파이프라인을 가져옵니다.
    /// </summary>
    ResiliencePipeline Pipeline { get; }
}

#endregion

#region 일시적 오류 감지

/// <summary>
/// 일시적인 SQL 오류(Transient Error)를 감지하는 로직을 정의합니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>재시도 기준 명확화</b>: 어떤 예외가 일시적이고 안전하게 재시도 가능한지 판단합니다.<br/>
/// - <b>확장성</b>: 클라우드(Azure SQL)나 온프레미스 등 환경에 따라 다른 오류 코드를 유연하게 추가할 수 있습니다.
/// </para>
/// </summary>
public interface ITransientSqlErrorDetector
{
    /// <summary>
    /// 지정된 예외가 일시적인 오류인지 여부를 판단합니다.
    /// </summary>
    /// <param name="ex">검사할 예외 객체</param>
    /// <returns>일시적 오류여서 재시도 가능한 경우 <c>true</c>, 그렇지 않으면 <c>false</c></returns>
    bool IsTransient(Exception ex);
}

#endregion
