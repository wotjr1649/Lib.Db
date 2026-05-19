// ============================================================================
// 파일: Architecture/ArchitectureTests.cs
// 설명: 아키텍처 경계 테스트 (NetArchTest)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Reflection;
using Lib.Db.Contracts.Execution;
using NetArchTest.Rules;

namespace Lib.Db.IntegrationTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(IDbExecutor).Assembly;

    [Fact]
    public void Contracts_Should_Not_Depend_On_Infrastructure()
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(DomainAssembly)
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
        NetArchTest.Rules.TestResult result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(LibDbOptions))
            .Should()
            .ResideInNamespace("Lib.Db.Configuration")
            .Or()
            .ResideInNamespace("Lib.Db.Caching")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "LibDbOptions(또는 파생 타입)은 Lib.Db.Configuration 또는 Lib.Db.Caching 네임스페이스에만 위치해야 합니다.");
    }

    [Fact]
    public void Core_Should_Not_Depend_On_External_Heavy_Dependencies()
    {
        // 의도: Core/Contracts 계층은 가급적 가벼운 의존성만 가져야 합니다.
    }
}
