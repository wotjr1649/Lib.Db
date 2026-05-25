using Lib.Db.Tools.Reporting;

namespace Lib.Db.Tools.Contracts;

public static class LibDbContractValidator
{
    private const string Breaking = "Breaking";
    private const string Warning = "Warning";
    private const string Informational = "Informational";

    public static LibDbContractValidationReport Validate(LibDbContractDocument expected, LibDbContractDocument actual)
    {
        List<LibDbContractDifference> differences = [];

        CompareProcedures(expected, actual, differences);
        CompareTableTypes(expected, actual, differences);
        CompareBulkTargets(expected, actual, differences);

        List<LibDbContractDifference> ordered = differences
            .OrderBy(difference => SeverityRank(difference.Severity))
            .ThenBy(difference => difference.Path, StringComparer.Ordinal)
            .ThenBy(difference => difference.Message, StringComparer.Ordinal)
            .ToList();

        return new LibDbContractValidationReport
        {
            Status = ordered.Count == 0 ? "Passed" : "Failed",
            Summary = new LibDbContractReportSummary
            {
                Total = ordered.Count,
                Breaking = ordered.Count(difference => StringComparer.Ordinal.Equals(difference.Severity, Breaking)),
                Warning = ordered.Count(difference => StringComparer.Ordinal.Equals(difference.Severity, Warning)),
                Informational = ordered.Count(difference => StringComparer.Ordinal.Equals(difference.Severity, Informational))
            },
            Differences = ordered
        };
    }

    private static void CompareProcedures(
        LibDbContractDocument expected,
        LibDbContractDocument actual,
        List<LibDbContractDifference> differences)
    {
        Dictionary<string, LibDbProcedureContract> actualProcedures = actual.Procedures.ToDictionary(ProcedureKey, StringComparer.OrdinalIgnoreCase);
        foreach (LibDbProcedureContract expectedProcedure in expected.Procedures.OrderBy(ProcedureKey, StringComparer.OrdinalIgnoreCase))
        {
            string key = ProcedureKey(expectedProcedure);
            if (!actualProcedures.TryGetValue(key, out LibDbProcedureContract? actualProcedure))
            {
                Add(differences, Breaking, $"Procedure[{SafeKey(key)}]", "Missing procedure in actual contract.");
                continue;
            }

            CompareParameters(expectedProcedure, actualProcedure, differences);
            CompareResultShape(expectedProcedure, actualProcedure, differences);
        }

        HashSet<string> expectedKeys = expected.Procedures.Select(ProcedureKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (LibDbProcedureContract actualProcedure in actual.Procedures.OrderBy(ProcedureKey, StringComparer.OrdinalIgnoreCase))
        {
            string key = ProcedureKey(actualProcedure);
            if (!expectedKeys.Contains(key))
                Add(differences, Informational, $"Procedure[{SafeKey(key)}]", "Additional procedure found in actual contract.");
        }
    }

    private static void CompareParameters(
        LibDbProcedureContract expectedProcedure,
        LibDbProcedureContract actualProcedure,
        List<LibDbContractDifference> differences)
    {
        Dictionary<string, LibDbParameterContract> actualParameters = actualProcedure.Parameters.ToDictionary(
            parameter => parameter.Name,
            StringComparer.OrdinalIgnoreCase);
        string procedureKey = ProcedureKey(expectedProcedure);

        foreach (LibDbParameterContract expectedParameter in expectedProcedure.Parameters.OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!actualParameters.TryGetValue(expectedParameter.Name, out LibDbParameterContract? actualParameter))
            {
                Add(differences, Breaking, $"Procedure[{SafeKey(procedureKey)}].Parameter[{SafeKey(expectedParameter.Name)}]", "Missing parameter in actual contract.");
                continue;
            }

            string path = $"Procedure[{SafeKey(procedureKey)}].Parameter[{SafeKey(expectedParameter.Name)}]";
            CompareScalar(differences, $"{path}.Direction", expectedParameter.Direction, actualParameter.Direction);
            CompareScalar(differences, $"{path}.Type", expectedParameter.Type, actualParameter.Type);
            CompareScalar(differences, $"{path}.Nullable", expectedParameter.Nullable, actualParameter.Nullable);
            CompareScalar(differences, $"{path}.MaxLength", expectedParameter.MaxLength, actualParameter.MaxLength);
            CompareScalar(differences, $"{path}.Precision", expectedParameter.Precision, actualParameter.Precision);
            CompareScalar(differences, $"{path}.Scale", expectedParameter.Scale, actualParameter.Scale);
        }

        HashSet<string> expectedParameterNames = expectedProcedure.Parameters
            .Select(static parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (LibDbParameterContract actualParameter in actualProcedure.Parameters.OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (expectedParameterNames.Contains(actualParameter.Name))
                continue;

            Add(
                differences,
                Breaking,
                $"Procedure[{SafeKey(procedureKey)}].Parameter[{SafeKey(actualParameter.Name)}]",
                "Additional parameter found in actual contract.");
        }
    }

    private static void CompareResultShape(
        LibDbProcedureContract expectedProcedure,
        LibDbProcedureContract actualProcedure,
        List<LibDbContractDifference> differences)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(expectedProcedure.ResultShape, actualProcedure.ResultShape))
            return;

        string procedureKey = ProcedureKey(expectedProcedure);
        string path = $"Procedure[{SafeKey(procedureKey)}].ResultShape";
        if (StringComparer.OrdinalIgnoreCase.Equals(actualProcedure.ResultShape, "Unknown"))
        {
            Add(differences, Warning, path, "Actual result shape is Unknown; manual review required.");
            return;
        }

        Add(
            differences,
            Breaking,
            path,
            $"Value mismatch: expected {ContractOutputRedactor.Redact(expectedProcedure.ResultShape)} but found {ContractOutputRedactor.Redact(actualProcedure.ResultShape)}.");
    }

    private static void CompareTableTypes(
        LibDbContractDocument expected,
        LibDbContractDocument actual,
        List<LibDbContractDifference> differences)
    {
        Dictionary<string, LibDbTableTypeContract> actualTableTypes = actual.TableTypes.ToDictionary(TableTypeKey, StringComparer.OrdinalIgnoreCase);
        foreach (LibDbTableTypeContract expectedTableType in expected.TableTypes.OrderBy(TableTypeKey, StringComparer.OrdinalIgnoreCase))
        {
            string key = TableTypeKey(expectedTableType);
            if (!actualTableTypes.TryGetValue(key, out LibDbTableTypeContract? actualTableType))
            {
                Add(differences, Breaking, $"TableType[{SafeKey(key)}]", "Missing table type in actual contract.");
                continue;
            }

            LibDbColumnContract[] expectedColumns = expectedTableType.Columns.OrderBy(column => column.Ordinal).ToArray();
            LibDbColumnContract[] actualColumns = actualTableType.Columns.OrderBy(column => column.Ordinal).ToArray();
            if (expectedColumns.Length != actualColumns.Length)
            {
                Add(differences, Breaking, $"TableType[{SafeKey(key)}].Columns", $"Column count mismatch: expected {expectedColumns.Length} but found {actualColumns.Length}.");
                continue;
            }

            for (int index = 0; index < expectedColumns.Length; index++)
            {
                LibDbColumnContract expectedColumn = expectedColumns[index];
                LibDbColumnContract actualColumn = actualColumns[index];
                string columnName = SafeKey(expectedColumn.Name);
                string path = $"TableType[{SafeKey(key)}].Column[{columnName}]";
                CompareScalar(differences, $"{path}.Ordinal", expectedColumn.Ordinal, actualColumn.Ordinal);
                CompareScalar(differences, $"{path}.Name", expectedColumn.Name, actualColumn.Name);
                CompareScalar(differences, $"{path}.Type", expectedColumn.Type, actualColumn.Type);
                CompareScalar(differences, $"{path}.Nullable", expectedColumn.Nullable, actualColumn.Nullable);
                CompareScalar(differences, $"{path}.MaxLength", expectedColumn.MaxLength, actualColumn.MaxLength);
                CompareScalar(differences, $"{path}.Precision", expectedColumn.Precision, actualColumn.Precision);
                CompareScalar(differences, $"{path}.Scale", expectedColumn.Scale, actualColumn.Scale);
            }
        }

        HashSet<string> expectedKeys = expected.TableTypes.Select(TableTypeKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (LibDbTableTypeContract actualTableType in actual.TableTypes.OrderBy(TableTypeKey, StringComparer.OrdinalIgnoreCase))
        {
            string key = TableTypeKey(actualTableType);
            if (!expectedKeys.Contains(key))
                Add(differences, Informational, $"TableType[{SafeKey(key)}]", "Additional table type found in actual contract.");
        }
    }

    private static void CompareBulkTargets(
        LibDbContractDocument expected,
        LibDbContractDocument actual,
        List<LibDbContractDifference> differences)
    {
        Dictionary<string, LibDbBulkTargetContract> actualTargets = actual.BulkTargets.ToDictionary(BulkTargetKey, StringComparer.OrdinalIgnoreCase);
        foreach (LibDbBulkTargetContract expectedTarget in expected.BulkTargets.OrderBy(BulkTargetKey, StringComparer.OrdinalIgnoreCase))
        {
            string key = BulkTargetKey(expectedTarget);
            if (!actualTargets.TryGetValue(key, out LibDbBulkTargetContract? actualTarget))
            {
                Add(differences, Breaking, $"BulkTarget[{SafeKey(key)}]", "Missing bulk target in actual contract.");
                continue;
            }

            CompareKeyColumns(key, expectedTarget.KeyColumns, actualTarget.KeyColumns, differences);
        }

        HashSet<string> expectedKeys = expected.BulkTargets.Select(BulkTargetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (LibDbBulkTargetContract actualTarget in actual.BulkTargets.OrderBy(BulkTargetKey, StringComparer.OrdinalIgnoreCase))
        {
            string key = BulkTargetKey(actualTarget);
            if (!expectedKeys.Contains(key))
                Add(differences, Informational, $"BulkTarget[{SafeKey(key)}]", "Additional bulk target found in actual contract.");
        }
    }

    private static void CompareKeyColumns(
        string bulkTargetKey,
        IReadOnlyList<string> expectedColumns,
        IReadOnlyList<string> actualColumns,
        List<LibDbContractDifference> differences)
    {
        if (expectedColumns.Count != actualColumns.Count)
        {
            Add(
                differences,
                Breaking,
                $"BulkTarget[{SafeKey(bulkTargetKey)}].KeyColumns",
                $"Key column count mismatch: expected {expectedColumns.Count} but found {actualColumns.Count}.");
            return;
        }

        for (int index = 0; index < expectedColumns.Count; index++)
        {
            CompareScalar(
                differences,
                $"BulkTarget[{SafeKey(bulkTargetKey)}].KeyColumns[{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}]",
                expectedColumns[index],
                actualColumns[index]);
        }
    }

    private static void CompareScalar<T>(List<LibDbContractDifference> differences, string path, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
            return;

        Add(
            differences,
            Breaking,
            path,
            $"Value mismatch: expected {ContractOutputRedactor.Redact(Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)} but found {ContractOutputRedactor.Redact(Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)}.");
    }

    private static void Add(List<LibDbContractDifference> differences, string severity, string path, string message) =>
        differences.Add(new LibDbContractDifference
        {
            Severity = severity,
            Path = ContractOutputRedactor.Redact(path),
            Message = ContractOutputRedactor.Redact(message)
        });

    private static string ProcedureKey(LibDbProcedureContract procedure) => $"{procedure.Schema}.{procedure.Name}";

    private static string TableTypeKey(LibDbTableTypeContract tableType) => $"{tableType.Schema}.{tableType.Name}";

    private static string BulkTargetKey(LibDbBulkTargetContract target) => $"{target.Schema}.{target.Table}";

    private static string SafeKey(string key) => ContractOutputRedactor.Redact(key);

    private static int SeverityRank(string severity) =>
        severity switch
        {
            Breaking => 0,
            Warning => 1,
            Informational => 2,
            _ => 3
        };
}
