namespace Lib.Db.Tools.Contracts;

public sealed class LibDbContractDifference
{
    public string Severity { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public sealed class LibDbContractReportSummary
{
    public int Total { get; init; }

    public int Breaking { get; init; }

    public int Warning { get; init; }

    public int Informational { get; init; }
}

public sealed class LibDbContractValidationReport
{
    public string ReportVersion { get; init; } = "1";

    public string Status { get; init; } = "Passed";

    public LibDbContractReportSummary Summary { get; init; } = new();

    public List<LibDbContractDifference> Differences { get; init; } = [];
}
