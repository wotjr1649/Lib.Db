// ============================================================================
// 파일: Aot/AotRegistryTests.cs
// 설명: AOT TvpFactoryRegistry 등록 검증 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Reflection;
using FluentAssertions;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;

namespace Lib.Db.IntegrationTests.Aot;

[TvpRow(TypeName = "dbo.AotTestTvp")]
public sealed class AotDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "AotTest";
}

public sealed class AotStrictResultDto
{
    public int Id { get; set; }
}

public sealed class AotStrictCachedResultDto
{
    public int Id { get; set; }
}

public sealed class AotRegistryTests
{
    [Fact]
    public void TvpGen_Should_Register_Dto_In_TvpFactoryRegistry()
    {
        // 1. Arrange
        Type dtoType = typeof(AotDto);

        // 2. Act & Assert
        Type registryType = typeof(TvpFactoryRegistry);
        FieldInfo? field = registryType.GetField("s_registry", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);

        IDictionary? dict = field.GetValue(null) as IDictionary;
        Assert.NotNull(dict);

        bool found = false;
        foreach (object? key in dict.Keys)
        {
            if (key.ToString()!.Contains("AotDto"))
            {
                found = true;
                break;
            }
        }

        Assert.True(found, "AotDto was not found in TvpFactoryRegistry. Source Generator might have failed.");
    }

    [Fact]
    public void MapperFactory_AotStrict_ShouldReject_DtoWithoutGeneratedResultMapper()
    {
        MapperFactory factory = new(
            new EmptyServiceProvider(),
            new LibDbOptions { MapperCompatibilityMode = MapperCompatibilityMode.AotStrict });

        Action act = () => factory.GetMapper<AotStrictResultDto>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AotStrict*source-generated*scalar*");
    }

    [Fact]
    public void MapperFactory_AotStrict_ShouldNotReuse_DefaultCachedFallbackMapper()
    {
        MapperFactory defaultFactory = new(new EmptyServiceProvider(), new LibDbOptions());
        _ = defaultFactory.GetMapper<AotStrictCachedResultDto>();

        MapperFactory strictFactory = new(
            new EmptyServiceProvider(),
            new LibDbOptions { MapperCompatibilityMode = MapperCompatibilityMode.AotStrict });

        Action act = () => strictFactory.GetMapper<AotStrictCachedResultDto>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AotStrict*source-generated*scalar*");
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
