using System.Text;
using Lib.Db.Tools.Contracts;

namespace Lib.Db.Tools.Reporting;

public static class LibDbContractReportWriter
{
    public static string WriteValidationMarkdown(LibDbContractValidationReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Lib.Db Contract Validation Report");
        builder.AppendLine();
        builder.AppendLine($"- Status: {report.Status}");
        builder.AppendLine($"- Differences: {report.Summary.Total}");
        builder.AppendLine($"- Breaking: {report.Summary.Breaking}");
        builder.AppendLine($"- Warning: {report.Summary.Warning}");
        builder.AppendLine($"- Informational: {report.Summary.Informational}");
        builder.AppendLine();
        builder.AppendLine("| Severity | Path | Message |");
        builder.AppendLine("| --- | --- | --- |");

        foreach (LibDbContractDifference difference in report.Differences)
        {
            builder.Append("| ")
                .Append(ContractOutputRedactor.EscapeMarkdown(difference.Severity))
                .Append(" | ")
                .Append(ContractOutputRedactor.EscapeMarkdown(difference.Path))
                .Append(" | ")
                .Append(ContractOutputRedactor.EscapeMarkdown(difference.Message))
                .AppendLine(" |");
        }

        return builder.ToString();
    }

    public static string WriteInventoryMarkdown(LibDbContractDocument contract)
    {
        int unknownResultShapes = contract.Procedures.Count(procedure =>
            StringComparer.OrdinalIgnoreCase.Equals(procedure.ResultShape, "Unknown"));

        StringBuilder builder = new();
        builder.AppendLine("# Lib.Db Contract Report");
        builder.AppendLine();
        builder.AppendLine($"- Schema version: {ContractOutputRedactor.EscapeMarkdown(contract.SchemaVersion)}");
        builder.AppendLine($"- Procedures: {contract.Procedures.Count}");
        builder.AppendLine($"- Table types: {contract.TableTypes.Count}");
        builder.AppendLine($"- Bulk targets: {contract.BulkTargets.Count}");
        builder.AppendLine($"- Unknown result shapes: {unknownResultShapes}");
        builder.AppendLine();
        builder.AppendLine("## Procedures");
        builder.AppendLine();
        builder.AppendLine("| Name | Parameters | Result Shape |");
        builder.AppendLine("| --- | ---: | --- |");

        foreach (LibDbProcedureContract procedure in contract.Procedures.OrderBy(procedure => $"{procedure.Schema}.{procedure.Name}", StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("| ")
                .Append(ContractOutputRedactor.EscapeMarkdown($"{procedure.Schema}.{procedure.Name}"))
                .Append(" | ")
                .Append(procedure.Parameters.Count)
                .Append(" | ")
                .Append(ContractOutputRedactor.EscapeMarkdown(procedure.ResultShape))
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Table Types");
        builder.AppendLine();
        builder.AppendLine("| Name | Columns |");
        builder.AppendLine("| --- | ---: |");
        foreach (LibDbTableTypeContract tableType in contract.TableTypes.OrderBy(tableType => $"{tableType.Schema}.{tableType.Name}", StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("| ")
                .Append(ContractOutputRedactor.EscapeMarkdown($"{tableType.Schema}.{tableType.Name}"))
                .Append(" | ")
                .Append(tableType.Columns.Count)
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Bulk Targets");
        builder.AppendLine();
        builder.AppendLine("| Name | Key Columns |");
        builder.AppendLine("| --- | --- |");
        foreach (LibDbBulkTargetContract target in contract.BulkTargets.OrderBy(target => $"{target.Schema}.{target.Table}", StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("| ")
                .Append(ContractOutputRedactor.EscapeMarkdown($"{target.Schema}.{target.Table}"))
                .Append(" | ")
                .Append(ContractOutputRedactor.EscapeMarkdown(string.Join(", ", target.KeyColumns)))
                .AppendLine(" |");
        }

        return builder.ToString();
    }
}
