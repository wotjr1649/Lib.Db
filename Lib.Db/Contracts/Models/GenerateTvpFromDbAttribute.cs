using System;

namespace Lib.Db.Contracts.Models;

/// <summary>
/// 디자인 타임에 DB 스키마(libdb.schema.json)를 참조하여 
/// TVP(Table-Valued Parameter) 관련 속성과 검증 코드를 자동 생성하도록 지시합니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>자동화</b>: DB 스키마 변경 시 DTO를 수동으로 수정하는 번거로움과 실수를 방지합니다.<br/>
/// - <b>생산성</b>: 반복적인 보일러플레이트 코드(접근자, 스키마 정의) 작성을 Source Generator에게 위임합니다.
/// </para>
/// <para>
/// <b>[사용법]</b>
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
