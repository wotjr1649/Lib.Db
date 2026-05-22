using Lib.Db.IntegrationTests.Infrastructure;

[assembly: AssemblyFixture(typeof(VerificationEnvironmentGuard))]

namespace Lib.Db.IntegrationTests.Infrastructure;

public sealed class VerificationEnvironmentGuard
{
    private const string GuardMessage =
        "Lib.Db integration tests require the verification environment before dotnet test starts. " +
        "Use pwsh -NoProfile -File .\\Verification\\scripts\\Invoke-Tests.ps1 so " +
        "Set-LibDbVerificationEnvironment.local.ps1 is loaded first, or set " +
        "LIBDB_TEST_SQL_PASSWORD / all LIBDB_TEST_CONNECTION_* / all ConnectionStrings__* values " +
        "in the current process. For non-database-only local runs, pass " +
        "-p:LIBDB_SKIP_TEST_ENV_GUARD=true explicitly.";

    public VerificationEnvironmentGuard()
    {
        if (IsPresent("LIBDB_SKIP_TEST_ENV_GUARD")
            && string.Equals(
                Environment.GetEnvironmentVariable("LIBDB_SKIP_TEST_ENV_GUARD"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsPresent("LIBDB_TEST_SQL_PASSWORD")
            || AllPresent(
                "LIBDB_TEST_CONNECTION_VERIFICATION",
                "LIBDB_TEST_CONNECTION_SORTER",
                "LIBDB_TEST_CONNECTION_STRESS",
                "LIBDB_TEST_CONNECTION_CHAOS",
                "LIBDB_TEST_CONNECTION_BENCHMARK")
            || AllPresent(
                "ConnectionStrings__Verification",
                "ConnectionStrings__Sorter",
                "ConnectionStrings__Stress",
                "ConnectionStrings__Chaos",
                "ConnectionStrings__Benchmark"))
        {
            return;
        }

        throw new InvalidOperationException(GuardMessage);
    }

    private static bool AllPresent(params string[] names)
        => names.All(IsPresent);

    private static bool IsPresent(string name)
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
}
