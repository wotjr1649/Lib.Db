// ============================================================================
// 파일: Aot/AotRegistryTests.cs
// 설명: AOT TvpFactoryRegistry 등록 검증 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Reflection;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Tvp;

namespace Lib.Db.IntegrationTests.Aot;

[TvpRow(TypeName = "dbo.AotTestTvp")]
public sealed class AotDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "AotTest";
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
    public void TvpGen_Should_Register_Dto_In_TvpAccessorRegistry()
    {
        bool found = TvpAccessorRegistry.TryGet<AotDto>(out TvpAccessors<AotDto>? accessors);

        Assert.True(found, "AotDto was not found in TvpAccessorRegistry. Static validator and buffer adder are not wired.");
        Assert.NotNull(accessors);
        Assert.NotNull(accessors.StaticValidator);
        Assert.NotNull(accessors.BufferAdder);
    }
}
