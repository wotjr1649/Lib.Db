param(
    [string] $Server = '127.0.0.1',
    [string] $UserId = 'SA',
    [string] $PasswordEnvironmentVariable = 'LIBDB_TEST_SQL_PASSWORD',
    [switch] $SetExplicitConnectionStrings,
    [switch] $NoBenchmarkReset,
    [switch] $PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$password = [Environment]::GetEnvironmentVariable($PasswordEnvironmentVariable)
if ([string]::IsNullOrWhiteSpace($password)) {
    throw "Set $PasswordEnvironmentVariable before loading this script."
}

function New-LibDbVerificationConnectionString {
    param(
        [Parameter(Mandatory = $true)] [string] $Database
    )

    return "Server=$Server;Database=$Database;User ID=$UserId;Password=$password;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=15;Application Name=Lib.Db.Verification"
}

$databases = [ordered]@{
    Verification = 'LIBDB_VERIFICATION_TEST'
    Sorter       = 'LIBDB_VERIFICATION_TEST'
    Stress       = 'LIBDB_STRESS_TEST'
    Chaos        = 'LIBDB_CHAOS_TEST'
    Benchmark    = 'LIBDB_BENCH_TEST'
}

foreach ($item in $databases.GetEnumerator()) {
    $name = [string] $item.Key
    $connectionString = New-LibDbVerificationConnectionString -Database ([string] $item.Value)

    if ($SetExplicitConnectionStrings) {
        [Environment]::SetEnvironmentVariable("ConnectionStrings__$name", $connectionString, 'Process')
        [Environment]::SetEnvironmentVariable("LIBDB_TEST_CONNECTION_$($name.ToUpperInvariant())", $connectionString, 'Process')
    }
}

[Environment]::SetEnvironmentVariable('LIBDB_BENCHMARK_CONNECTION', (New-LibDbVerificationConnectionString -Database 'LIBDB_BENCH_TEST'), 'Process')

if (-not $NoBenchmarkReset) {
    [Environment]::SetEnvironmentVariable('LIBDB_BENCHMARK_ALLOW_RESET', 'true', 'Process')
}

if ($PassThru) {
    @(
        'ConnectionStrings__Verification'
        'ConnectionStrings__Sorter'
        'ConnectionStrings__Stress'
        'ConnectionStrings__Chaos'
        'ConnectionStrings__Benchmark'
        'LIBDB_TEST_CONNECTION_VERIFICATION'
        'LIBDB_TEST_CONNECTION_SORTER'
        'LIBDB_TEST_CONNECTION_STRESS'
        'LIBDB_TEST_CONNECTION_CHAOS'
        'LIBDB_TEST_CONNECTION_BENCHMARK'
        'LIBDB_TEST_SQL_PASSWORD'
        'LIBDB_BENCHMARK_CONNECTION'
        'LIBDB_BENCHMARK_ALLOW_RESET'
    ) | ForEach-Object {
        [pscustomobject]@{
            Name = $_
            Present = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))
        }
    }
}
