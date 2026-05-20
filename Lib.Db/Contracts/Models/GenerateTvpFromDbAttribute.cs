// ============================================================================
// 파일: Lib.Db/Contracts/Models/GenerateTvpFromDbAttribute.cs
// 설명: v2.2 DB-first TVP source-generation compatibility marker
// 대상: .NET 10 / C# 14
// ============================================================================

using System;

namespace Lib.Db.Contracts.Models;

/// <summary>
/// v2.2 DB-first TVP source-generation 경로와의 호환을 위해 남아 있는 마커 특성입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// v2.3 신규 TVP 호출부는 별도 generator가 아니라 단일 <c>Lib.Db</c> 런타임의
/// <c>LibDb.Tvp(...)</c>, <c>options.Tvp.Map&lt;T&gt;()</c>, <c>TvpShape.For&lt;T&gt;()</c>를 사용합니다.
/// 이 특성은 과거 코드의 소스 호환과 마이그레이션 식별을 위한 용도입니다.
/// </para>
/// <para>
/// <b>[legacy 사용 예]</b>
/// <code>
/// [GenerateTvpFromDb("dbo.MyTvps")]
/// public partial class MyTvpDto { }
/// </code>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GenerateTvpFromDbAttribute : Attribute
{
    /// <summary>
    /// 대상 TVP 이름 (예: "dbo.UserTable", "MyType")
    /// </summary>
    public string TvpName { get; }

    /// <summary>
    /// 속성 생성 시 소문자(camelCase) 대신 파스칼케이스(PascalCase) 변환 여부 (기본: true)
    /// </summary>
    public bool UsePascalCase { get; set; } = true;

    /// <summary>
    /// 생성자를 통해 TVP 이름을 지정합니다.
    /// </summary>
    /// <param name="tvpName">DB 상의 TVP 이름 (스키마 포함 권장)</param>
    public GenerateTvpFromDbAttribute(string tvpName)
    {
        TvpName = tvpName;
    }
}
