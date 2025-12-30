// ============================================================================
// File : Lib.Db/Contracts/Cache/IIsolationKeyGenerator.cs
// Role : ConnectionString 기반 IsolationKey 생성 인터페이스
// Env  : .NET 10 / C# 14
// Notes:
//   - 의존성 역전 원칙 (DIP) 적용
//   - 테스트 가능성 향상 (Mock 가능)
//   - Connection String 정규화 및 해싱 로직 캡슐화
// ============================================================================

#nullable enable

using Microsoft.Extensions.Logging;

namespace Lib.Db.Contracts.Cache;

#region IsolationKey 생성 인터페이스

/// <summary>
/// Connection String을 기반으로 IsolationKey를 생성하는 서비스 인터페이스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>의존성 역전 (DIP)</b>: 해싱 알고리즘이나 키 생성 전략이 변경되더라도 클라이언트 코드에 영향을 주지 않도록 인터페이스 뒤에 숨깁니다.<br/>
/// - <b>보안 고려</b>: 원본 Connection String에 포함된 민감 정보(Password)가 그대로 키로 사용되지 않도록 해싱 과정을 캡슐화합니다.<br/>
/// - <b>테스트 용이성</b>: Mock 객체를 통해 다양한 연결 문자열 시나리오를 손쉽게 테스트할 수 있습니다.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>[역할]</b></para>
/// <para>
/// Connection String을 입력받아 고유한 IsolationKey를 생성합니다.
/// <br/>동일한 DB를 사용하는 프로세스는 동일한 IsolationKey를 가져야 합니다.
/// </para>
/// </remarks>
public interface IIsolationKeyGenerator
{
    /// <summary>
    /// Connection String을 기반으로 IsolationKey를 생성합니다.
    /// </summary>
    /// <param name="connectionString">
    /// 데이터베이스 연결 문자열
    /// <br/>예: "Server=localhost;Database=Test;User=sa;Password=pass;"
    /// </param>
    /// <returns>
    /// 32자 Hex 문자열 (XxHash128) 또는 null (connectionString이 null/empty인 경우)
    /// </returns>
    /// <remarks>
    /// <para><b>[동작 흐름]</b></para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <b>Step 1</b>: Connection String 검증 (null/empty 체크)
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Step 2</b>: 정규화 시도 (SqlConnectionStringBuilder 사용)
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Step 3</b>: 해싱 (XxHash128)
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>폴백</b>: 정규화 실패 시 원본 문자열 해싱
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>[예시]</b></para>
    /// <code>
    /// var generator = new IsolationKeyGenerator(logger);
    /// var key = generator.Generate("Server=localhost;Database=Test;");
    /// // key: "a1b2c3d4e5f6g7h8..." (32자)
    /// </code>
    /// </remarks>
    string? Generate(string? connectionString);
}

#endregion
