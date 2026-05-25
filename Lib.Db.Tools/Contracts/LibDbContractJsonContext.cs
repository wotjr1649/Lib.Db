using System.Text.Json.Serialization;

namespace Lib.Db.Tools.Contracts;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LibDbContractDocument))]
[JsonSerializable(typeof(LibDbContractValidationReport))]
internal sealed partial class LibDbContractJsonContext : JsonSerializerContext;
