using System.Text.Json;
using System.Text.Json.Serialization;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Models;

namespace Lib.Db.Configuration
{
    [JsonSerializable(typeof(LibDbOptions))]
    [JsonSerializable(typeof(SpSchema))]
    [JsonSerializable(typeof(TvpSchema))]
    [JsonSourceGenerationOptions(WriteIndented = true, 
        IgnoreReadOnlyProperties = false,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal partial class LibDbJsonContext : JsonSerializerContext
    {
    }
}
