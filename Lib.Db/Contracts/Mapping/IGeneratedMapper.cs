// ============================================================================
// 파일명: Lib.Db/Contracts/Mapping/IGeneratedMapper.cs
// 작성일: 2025-12-10
// 환경  : .NET 10 (Preview) / C# 14
// 역할  : Source Generator가 생성한 매퍼를 구분하기 위한 마커 인터페이스
// 설명  :
//   - Priority 속성을 통해 매퍼 선택 우선순위를 결정합니다.
//   - 값이 클수록 우선 선택됩니다.
//   - 100 = Source Generator (컴파일 타임 생성, 최우선)
//   - 50  = Expression Tree 기반 (JIT 환경 최적)
//   - 0   = Reflection 기반 (AOT Fallback)
// ============================================================================

#nullable enable

namespace Lib.Db.Contracts.Mapping;

#region 소스 생성 매퍼 식별 계약

/// <summary>
/// Source Generator가 생성한 고성능 매퍼를 식별하기 위한 마커 인터페이스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>적응형 최적화</b>: JIT(Expression Tree)와 AOT(Static Code) 환경 모두에서 최적의 매퍼를 선택할 수 있는 유연성을 제공합니다.<br/>
/// - <b>우선순위 기반 전략</b>: 단일 인터페이스로 여러 구현체(Reflection, Expression, SourceGen)를 통일하고, <see cref="Priority"/> 점수로 런타임에 구현체를 교체합니다.
/// </para>
/// <para>
/// 런타임(JIT) / AOT 환경에 따라 사용 가능한 매퍼 구현체가 달라질 수 있으므로,
/// 이 인터페이스와 <see cref="Priority"/> 값을 통해
/// 현재 환경에서 가장 적합한 매퍼를 자동으로 선택하는 데 사용됩니다.
/// </para>
/// </summary>
/// <typeparam name="T">매핑 대상 DTO 타입</typeparam>
internal interface IGeneratedMapper<T> : ISqlMapper<T>
{
    #region 선택 우선순위

    /// <summary>
    /// 매퍼 선택 우선순위를 나타내는 점수입니다.
    /// <para>
    /// 점수가 높을수록 우선적으로 선택됩니다.
    /// </para>
    /// <para>
    /// <b>[우선순위 규칙]</b>
    /// <list type="bullet">
    /// <item>
    /// <c>100</c> : Source Generator 기반 매퍼<br/>
    /// → 컴파일 타임에 코드가 생성되어 가장 빠르고, AOT/JIT 모두에서 안정적
    /// </item>
    /// <item>
    /// <c>50</c> : Expression Tree 기반 매퍼<br/>
    /// → 런타임 코드 생성이 가능할 때(JIT)만 사용되며 성능이 우수
    /// </item>
    /// <item>
    /// <c>0</c> : Reflection 기반 매퍼<br/>
    /// → 가장 범용적이지만 성능은 가장 낮으며, AOT 환경의 최후 수단(Fallback)
    /// </item>
    /// </list>
    /// </para>
    /// </summary>
    int Priority { get; }

    #endregion
}

#endregion
