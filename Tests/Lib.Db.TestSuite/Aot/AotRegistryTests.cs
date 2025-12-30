using System.Collections;
using System.Reflection;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;
using Xunit;

namespace Lib.Db.TestSuite.Aot;

[TvpRow(TypeName = "dbo.AotTestTvp")]
public class AotDto 
{ 
    public int Id { get; set; } 
    public string Name { get; set; } = "AotTest";
}

public class AotRegistryTests
{
    [Fact]
    public void TvpGen_Should_Register_Dto_In_TvpFactoryRegistry()
    {
        // 1. Arrange
        var dtoType = typeof(AotDto);

        // 2. Act & Assert
        // We use reflection to inspect the private static registry
        var registryType = typeof(Lib.Db.Execution.Binding.TvpFactoryRegistry);
        var field = registryType.GetField("s_registry", BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(field); // Field must exist if Lib.Db is connected
        
        var dict = field.GetValue(null) as IDictionary;
        Assert.NotNull(dict); // Registry must be initialized
        
        bool found = false;
        foreach (var key in dict.Keys)
        {
            if (key.ToString().Contains("AotDto"))
            {
                found = true;
                break;
            }
        }
        
        Assert.True(found, "AotDto was not found in TvpFactoryRegistry. Source Generator might have failed.");
    }
}

