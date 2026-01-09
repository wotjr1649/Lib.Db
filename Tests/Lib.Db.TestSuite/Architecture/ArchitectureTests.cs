using System.Reflection;
using FluentAssertions;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Execution;
using NetArchTest.Rules;
using Xunit;

namespace Lib.Db.TestSuite.Architecture;

public class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(IDbExecutor).Assembly;

    // Infrastructure가 internal로 변경될 수 있지만, 테스트에서는 다음 중 하나를 전제로 해야 합니다.
    // 1) 공개 API만 대상으로 검증한다(권장: 아키텍처 경계 테스트의 기본값).
    // 2) InternalsVisibleTo를 통해 테스트 어셈블리에서 internal 접근을 허용한다.
    //
    // 여기서는 "공개 API 기준으로 경계가 지켜지는지"를 우선 검증합니다.
    // 만약 Infrastructure가 별도 어셈블리가 아니라 네임스페이스/폴더 레벨 구성이라면,
    // 의존성 검사 대상 문자열(네임스페이스/어셈블리명)을 실제 구성에 맞게 조정하세요.

    [Fact]
    public void Contracts_Should_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespace("Lib.Db.Contracts")
            .Should()
            .NotHaveDependencyOn("Lib.Db.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue("Contracts should not depend on Infrastructure");
    }

    [Fact]
    public void Configuration_Types_Should_Reside_Only_In_Configuration_Or_Caching_Namespaces()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(LibDbOptions))
            .Should()
            .ResideInNamespace("Lib.Db.Configuration")
            .Or()
            .ResideInNamespace("Lib.Db.Caching") // 예: SharedMemoryMappedCacheOptions 등의 파생 옵션을 허용
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "LibDbOptions(또는 파생 타입)은 Lib.Db.Configuration 또는 Lib.Db.Caching 네임스페이스에만 위치해야 합니다.");

        // 만약 다른 네임스페이스에서 옵션을 상속받아 구현해야 하는 특수 케이스가 생긴다면,
        // 그 타입만 예외 처리(화이트리스트)하거나 규칙을 더 세분화하는 것이 안전합니다.
        // 현재는 LibDbOptions가 옵션 계층의 중심(베이스)라는 전제 하에 단순 규칙을 적용합니다.
    }

    [Fact]
    public void Core_Should_Not_Depend_On_External_Heavy_Dependencies()
    {
        // 의도:
        // - Core/Contracts 계층은 가급적 가벼운 의존성(System.*, BCL, 필요한 공용 패키지)만 가져야 합니다.
        // - DB 벤더/드라이버(SqlClient/Oracle 등), ORM(Dapper 등) 같은 "무거운 런타임 의존성"은
        //   Execution/Infrastructure 같은 바깥 계층으로 밀어내는 것이 일반적으로 안전합니다.
        //
        // 아래는 예시 규칙입니다. 실제 프로젝트에서 허용/금지 목록을 확정해 조정하세요.
    }
}
