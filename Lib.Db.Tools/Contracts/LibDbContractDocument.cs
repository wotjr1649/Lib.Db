using System.Text;
using System.Text.Json;
using Lib.Db.Tools.Reporting;

namespace Lib.Db.Tools.Contracts;

public sealed class LibDbContractDocument
{
    public string SchemaVersion { get; init; } = "1";

    public List<LibDbProcedureContract> Procedures { get; init; } = [];

    public List<LibDbTableTypeContract> TableTypes { get; init; } = [];

    public List<LibDbBulkTargetContract> BulkTargets { get; init; } = [];
}

public sealed class LibDbProcedureContract
{
    public string Schema { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public List<LibDbParameterContract> Parameters { get; init; } = [];

    public string ResultShape { get; init; } = "Unknown";
}

public sealed class LibDbParameterContract
{
    public string Name { get; init; } = string.Empty;

    public string Direction { get; init; } = "Input";

    public string Type { get; init; } = string.Empty;

    public bool Nullable { get; init; }

    public int? MaxLength { get; init; }

    public byte? Precision { get; init; }

    public byte? Scale { get; init; }
}

public sealed class LibDbTableTypeContract
{
    public string Schema { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public List<LibDbColumnContract> Columns { get; init; } = [];
}

public sealed class LibDbColumnContract
{
    public int Ordinal { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public bool Nullable { get; init; }

    public int? MaxLength { get; init; }

    public byte? Precision { get; init; }

    public byte? Scale { get; init; }
}

public sealed class LibDbBulkTargetContract
{
    public string Schema { get; init; } = string.Empty;

    public string Table { get; init; } = string.Empty;

    public List<string> KeyColumns { get; init; } = [];
}

public sealed class LibDbContractException(string message) : Exception(message);

public static class LibDbContractSerializer
{
    private static readonly HashSet<string> SecretBearingFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey",
        "connection",
        "connectionString",
        "connectionStrings",
        "password",
        "pwd",
        "secret",
        "token"
    };

    public static async ValueTask<LibDbContractDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new LibDbContractException("Contract document must be a JSON object.");

        string? secretField = FindSecretBearingField(document.RootElement);
        if (secretField is not null)
            throw new LibDbContractException("Contract document contains an unsupported secret-bearing field.");

        ValidateJsonShape(document.RootElement);

        LibDbContractDocument? contract = document.RootElement.Deserialize(LibDbContractJsonContext.Default.LibDbContractDocument);
        if (contract is null)
            throw new LibDbContractException("Contract document could not be read.");

        if (!StringComparer.Ordinal.Equals(contract.SchemaVersion, "1"))
            throw new LibDbContractException("Contract schemaVersion must be '1'.");

        ValidateShape(contract);
        return contract;
    }

    public static string SerializeReport(LibDbContractValidationReport report) =>
        JsonSerializer.Serialize(report, LibDbContractJsonContext.Default.LibDbContractValidationReport);

    public static string SerializeDocument(LibDbContractDocument contract) =>
        JsonSerializer.Serialize(CreateRedactedDocument(contract), LibDbContractJsonContext.Default.LibDbContractDocument);

    private static string? FindSecretBearingField(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (IsSecretBearingFieldName(property.Name))
                    return property.Name;

                string? nested = FindSecretBearingField(property.Value);
                if (nested is not null)
                    return nested;
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindSecretBearingField(item);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static void ValidateJsonShape(JsonElement root)
    {
        ValidateObject(
            root,
            "contract document",
            required: ["schemaVersion", "procedures", "tableTypes", "bulkTargets"],
            allowed: ["schemaVersion", "procedures", "tableTypes", "bulkTargets"]);

        ValidateArray(root.GetProperty("procedures"), "procedures", procedure =>
        {
            ValidateObject(
                procedure,
                "procedure",
                required: ["schema", "name", "parameters", "resultShape"],
                allowed: ["schema", "name", "parameters", "resultShape"]);
            ValidateArray(procedure.GetProperty("parameters"), "procedure parameters", parameter =>
            {
                ValidateObject(
                    parameter,
                    "procedure parameter",
                    required: ["name", "direction", "type", "nullable"],
                    allowed: ["name", "direction", "type", "nullable", "maxLength", "precision", "scale"]);
            });
        });

        ValidateArray(root.GetProperty("tableTypes"), "tableTypes", tableType =>
        {
            ValidateObject(
                tableType,
                "table type",
                required: ["schema", "name", "columns"],
                allowed: ["schema", "name", "columns"]);
            ValidateArray(tableType.GetProperty("columns"), "table type columns", column =>
            {
                ValidateObject(
                    column,
                    "table type column",
                    required: ["ordinal", "name", "type", "nullable"],
                    allowed: ["ordinal", "name", "type", "nullable", "maxLength", "precision", "scale"]);
            });
        });

        ValidateArray(root.GetProperty("bulkTargets"), "bulkTargets", bulkTarget =>
        {
            ValidateObject(
                bulkTarget,
                "bulk target",
                required: ["schema", "table", "keyColumns"],
                allowed: ["schema", "table", "keyColumns"]);
        });
    }

    private static void ValidateObject(
        JsonElement element,
        string name,
        IReadOnlyCollection<string> required,
        IReadOnlyCollection<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new LibDbContractException($"Contract document field '{name}' must be an object.");

        HashSet<string> properties = element
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string property in properties)
        {
            if (!allowed.Contains(property, StringComparer.Ordinal))
                throw new LibDbContractException($"Contract document field '{name}' contains an unsupported property.");
        }

        foreach (string property in required)
        {
            if (!properties.Contains(property))
                throw new LibDbContractException($"Contract document field '{name}' is missing a required property.");
        }
    }

    private static void ValidateArray(JsonElement element, string name, Action<JsonElement> validateItem)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new LibDbContractException($"Contract document field '{name}' must be an array.");

        foreach (JsonElement item in element.EnumerateArray())
            validateItem(item);
    }

    private static bool IsSecretBearingFieldName(string name)
    {
        if (SecretBearingFieldNames.Contains(name))
            return true;

        string normalized = NormalizeFieldName(name);
        return normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("credential", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal) ||
            normalized.Contains("connectionstring", StringComparison.Ordinal) ||
            StringComparer.Ordinal.Equals(normalized, "connection");
    }

    private static string NormalizeFieldName(string name)
    {
        StringBuilder builder = new(name.Length);
        foreach (char ch in name)
        {
            if (!char.IsLetterOrDigit(ch))
                continue;

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static void ValidateShape(LibDbContractDocument contract)
    {
        EnsureCollection(contract.Procedures, "procedures");
        EnsureCollection(contract.TableTypes, "tableTypes");
        EnsureCollection(contract.BulkTargets, "bulkTargets");

        EnsureUnique(contract.Procedures.Select(ProcedureKey), "procedures");
        foreach (LibDbProcedureContract procedure in contract.Procedures)
        {
            RequireName(procedure.Schema, "procedure schema");
            RequireName(procedure.Name, "procedure name");
            RequireKnownResultShape(procedure.ResultShape);
            EnsureCollection(procedure.Parameters, "procedure parameters");
            EnsureUnique(procedure.Parameters.Select(parameter => parameter.Name), "procedure parameters");
            foreach (LibDbParameterContract parameter in procedure.Parameters)
            {
                RequireName(parameter.Name, "parameter name");
                RequireName(parameter.Direction, "parameter direction");
                RequireName(parameter.Type, "parameter type");
            }
        }

        EnsureUnique(contract.TableTypes.Select(TableTypeKey), "tableTypes");
        foreach (LibDbTableTypeContract tableType in contract.TableTypes)
        {
            RequireName(tableType.Schema, "table type schema");
            RequireName(tableType.Name, "table type name");
            EnsureCollection(tableType.Columns, "table type columns");
            EnsureUnique(tableType.Columns.Select(column => column.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)), "table type column ordinals");
            EnsureUnique(tableType.Columns.Select(column => column.Name), "table type columns");
            foreach (LibDbColumnContract column in tableType.Columns)
            {
                RequireName(column.Name, "column name");
                RequireName(column.Type, "column type");
            }
        }

        EnsureUnique(contract.BulkTargets.Select(BulkTargetKey), "bulkTargets");
        foreach (LibDbBulkTargetContract bulkTarget in contract.BulkTargets)
        {
            RequireName(bulkTarget.Schema, "bulk target schema");
            RequireName(bulkTarget.Table, "bulk target table");
            EnsureCollection(bulkTarget.KeyColumns, "bulk target key columns");
            EnsureUnique(bulkTarget.KeyColumns, "bulk target key columns");
            foreach (string keyColumn in bulkTarget.KeyColumns)
                RequireName(keyColumn, "bulk target key column");
        }
    }

    private static void EnsureCollection<T>(IReadOnlyCollection<T>? collection, string name)
    {
        if (collection is null)
            throw new LibDbContractException($"Contract document field '{name}' must be an array.");

        if (collection.Any(static item => item is null))
            throw new LibDbContractException($"Contract document field '{name}' must not contain null entries.");
    }

    private static void EnsureUnique(IEnumerable<string> keys, string name)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in keys)
        {
            if (!seen.Add(key))
                throw new LibDbContractException($"Contract document field '{name}' contains duplicate entries.");
        }
    }

    private static void RequireName(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new LibDbContractException($"Contract document field '{name}' is required.");
    }

    private static void RequireKnownResultShape(string? resultShape)
    {
        if (StringComparer.Ordinal.Equals(resultShape, "Known") ||
            StringComparer.Ordinal.Equals(resultShape, "Unknown"))
        {
            return;
        }

        throw new LibDbContractException("Contract document field 'resultShape' must be Known or Unknown.");
    }

    private static LibDbContractDocument CreateRedactedDocument(LibDbContractDocument contract) =>
        new()
        {
            SchemaVersion = ContractOutputRedactor.Redact(contract.SchemaVersion),
            Procedures = contract.Procedures.Select(static procedure => new LibDbProcedureContract
            {
                Schema = ContractOutputRedactor.Redact(procedure.Schema),
                Name = ContractOutputRedactor.Redact(procedure.Name),
                ResultShape = ContractOutputRedactor.Redact(procedure.ResultShape),
                Parameters = procedure.Parameters.Select(static parameter => new LibDbParameterContract
                {
                    Name = ContractOutputRedactor.Redact(parameter.Name),
                    Direction = ContractOutputRedactor.Redact(parameter.Direction),
                    Type = ContractOutputRedactor.Redact(parameter.Type),
                    Nullable = parameter.Nullable,
                    MaxLength = parameter.MaxLength,
                    Precision = parameter.Precision,
                    Scale = parameter.Scale
                }).ToList()
            }).ToList(),
            TableTypes = contract.TableTypes.Select(static tableType => new LibDbTableTypeContract
            {
                Schema = ContractOutputRedactor.Redact(tableType.Schema),
                Name = ContractOutputRedactor.Redact(tableType.Name),
                Columns = tableType.Columns.Select(static column => new LibDbColumnContract
                {
                    Ordinal = column.Ordinal,
                    Name = ContractOutputRedactor.Redact(column.Name),
                    Type = ContractOutputRedactor.Redact(column.Type),
                    Nullable = column.Nullable,
                    MaxLength = column.MaxLength,
                    Precision = column.Precision,
                    Scale = column.Scale
                }).ToList()
            }).ToList(),
            BulkTargets = contract.BulkTargets.Select(static target => new LibDbBulkTargetContract
            {
                Schema = ContractOutputRedactor.Redact(target.Schema),
                Table = ContractOutputRedactor.Redact(target.Table),
                KeyColumns = target.KeyColumns.Select(ContractOutputRedactor.Redact).ToList()
            }).ToList()
        };

    private static string ProcedureKey(LibDbProcedureContract procedure) => $"{procedure.Schema}.{procedure.Name}";

    private static string TableTypeKey(LibDbTableTypeContract tableType) => $"{tableType.Schema}.{tableType.Name}";

    private static string BulkTargetKey(LibDbBulkTargetContract target) => $"{target.Schema}.{target.Table}";
}
