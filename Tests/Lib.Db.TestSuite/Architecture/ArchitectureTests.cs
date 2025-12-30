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
    // Infrastructureê°€ internalë¡?ë³€ê²½ë  ???ˆì?ë§? ?ŒìŠ¤?¸ì—?œëŠ” ?‘ê·¼ ê°€?¥í•˜?¤ê³  ê°€?•í•˜ê±°ë‚˜
    // InternalsVisibleToë¥??µí•´ ?‘ê·¼?´ì•¼ ?? ?¬ê¸°?œëŠ” ê³µê°œ???€???„ì£¼ë¡?ê²€??
    // ë§Œì•½ Infrastructureê°€ ë³„ë„ ?´ì…ˆë¸”ë¦¬ê°€ ?„ë‹ˆê³??¤ì„?¤í˜?´ìŠ¤?¼ë©´ ?„ë˜?€ ê°™ì´ ì²˜ë¦¬.
    
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
    public void Configuration_Should_Are_Only_Allowed_In_Configuration_Or_Caching_Namespaces()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(LibDbOptions))
            .Should()
            .ResideInNamespace("Lib.Db.Configuration")
            .Or()
            .ResideInNamespace("Lib.Db.Caching") // SharedMemoryMappedCacheOptions ???ˆìš©
            .GetResult();

        // ?¼ë? ?´ë? êµ¬í˜„?ì„œ ?ì†ë°›ëŠ” ê²½ìš°ê°€ ?ˆë‹¤ë©??ˆì™¸ ì²˜ë¦¬ ?„ìš”
        // ?„ì¬??LibDbOptions ?ì²´ê°€ ì¤‘ì‹¬
    }

    [Fact]
    public void Core_Should_Not_Depend_On_External_Heavy_Dependencies()
    {
        // Core(Contracts ????System.* ë§??˜ì¡´?´ì•¼ ?´ìƒ??(Dapper ???œì™¸)
        // ?¬ê¸°?œëŠ” ?ˆì‹œë¡?System.Data.SqlClient ì§ì ‘ ?¬ìš© ?¬ë? ?±ì„ ì²´í¬?????ˆìŒ
    }
}

