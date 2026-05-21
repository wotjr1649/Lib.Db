using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

return await ChaosHarness.RunAsync(args, CancellationToken.None);

internal static partial class ChaosHarness
{
    private const string DefaultDatabase = "LIBDB_CHAOS_TEST";
    private const string DefaultSessionName = "libdb_chaos_observer";
    private const string ProgramPrefix = "LibDb.ChaosHarness";
    private const string ConnectionEnvironmentVariable = "LIBDB_CHAOS_CONNECTION";
    private const string PasswordEnvironmentVariable = "LIBDB_CHAOS_PASSWORD";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (!options.EnableServerChaos)
        {
            Console.Error.WriteLine("--enable-server-chaos is required for every command.");
            return 2;
        }

        if (!StringComparer.Ordinal.Equals(options.Database, DefaultDatabase))
        {
            Console.Error.WriteLine($"Only {DefaultDatabase} is supported.");
            return 2;
        }

        try
        {
            return options.Command switch
            {
                "setup" => await ExecuteSqlFileAsync(options, "setup-libdb-chaos-server-optin.sql", "master", ct),
                "verify" => await VerifyAsync(options, ct),
                "run" => await RunChaosAsync(options, ct),
                "teardown" => await ExecuteSqlFileAsync(options, "teardown-libdb-chaos-server-optin.sql", "master", ct),
                "all" => await RunAllAsync(options, ct),
                _ => UnknownCommand(options.Command)
            };
        }
        catch (SqlException ex)
        {
            Console.Error.WriteLine($"SQL error {ex.Number}: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 2;
    }

    private static async Task<int> RunAllAsync(Options options, CancellationToken ct)
    {
        int setup = await ExecuteSqlFileAsync(options, "setup-libdb-chaos-server-optin.sql", "master", ct);
        if (setup != 0)
            return setup;

        int run = await RunChaosAsync(options, ct);
        if (run != 0)
            return run;

        return await VerifyAsync(options, ct);
    }

    private static async Task<int> VerifyAsync(Options options, CancellationToken ct)
    {
        int verify = await ExecuteSqlFileAsync(options, "verify-libdb-chaos-server-optin.sql", "master", ct);
        if (verify != 0)
            return verify;

        await using SqlConnection connection = await OpenConnectionAsync(options, "master", $"{ProgramPrefix}.Verify", ct);
        int eventCount = await ReadRingBufferEventCountAsync(connection, options.SessionName, ct);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ObserverEventCount={eventCount}"));
        return 0;
    }

    private static async Task<int> RunChaosAsync(Options options, CancellationToken ct)
    {
        await EnsureObserverIsRunningAsync(options, ct);
        await RunExpectedUserErrorAsync(options, ct);
        await RunLockTimeoutAsync(options, ct);
        await RunDeadlockAsync(options, ct);

        if (options.AllowKill)
            await RunKillProbeAsync(options, ct);
        else
            Console.WriteLine("KillProbeSkipped=--allow-kill not supplied");

        return 0;
    }

    private static async Task ExecuteSqlFileCoreAsync(Options options, string fileName, string database, CancellationToken ct)
    {
        string path = Path.Combine(options.SqlRoot, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"SQL file was not found: {path}");

        string sql = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        sql = PrepareSqlcmdScript(sql, options);
        string[] batches = GoSplitter().Split(sql)
            .Where(static batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

        await using SqlConnection connection = await OpenConnectionAsync(options, database, $"{ProgramPrefix}.SqlFile", ct);
        for (int index = 0; index < batches.Length; index++)
        {
            await using SqlCommand command = new(batches[index], connection)
            {
                CommandTimeout = options.CommandTimeoutSeconds
            };

            try
            {
                await command.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"{fileName} failed at batch {index + 1}: SQL error {ex.Number}: {ex.Message}"),
                    ex);
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SqlFileExecuted={fileName} Batches={batches.Length}"));
    }

    private static async Task<int> ExecuteSqlFileAsync(Options options, string fileName, string database, CancellationToken ct)
    {
        await ExecuteSqlFileCoreAsync(options, fileName, database, ct);
        return 0;
    }

    private static string PrepareSqlcmdScript(string sql, Options options)
    {
        string replaced = sql
            .Replace("$(EnableServerChaos)", "1", StringComparison.Ordinal)
            .Replace("$(ChaosDatabaseName)", options.Database, StringComparison.Ordinal)
            .Replace("$(ChaosSessionName)", options.SessionName, StringComparison.Ordinal);

        StringBuilder builder = new(replaced.Length);
        using StringReader reader = new(replaced);
        while (reader.ReadLine() is { } line)
        {
            if (line.TrimStart().StartsWith(':'))
                continue;

            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static async Task EnsureObserverIsRunningAsync(Options options, CancellationToken ct)
    {
        await using SqlConnection connection = await OpenConnectionAsync(options, "master", $"{ProgramPrefix}.Guard", ct);
        await using SqlCommand command = new(
            "SELECT COUNT(*) FROM sys.dm_xe_sessions WHERE [name] = @SessionName;",
            connection);
        command.Parameters.Add("@SessionName", SqlDbType.NVarChar, 128).Value = options.SessionName;

        int running = Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (running != 1)
            throw new InvalidOperationException("Server chaos observer is not running. Execute the setup command first.");
    }

    private static async Task<int> ReadRingBufferEventCountAsync(SqlConnection connection, string sessionName, CancellationToken ct)
    {
        const string sql = """
            SELECT COALESCE(CAST(CAST(targets.[target_data] AS XML).value('(/RingBufferTarget/@eventCount)[1]', 'int') AS INT), 0)
            FROM sys.dm_xe_sessions AS sessions
            INNER JOIN sys.dm_xe_session_targets AS targets
                ON targets.[event_session_address] = sessions.[address]
            WHERE sessions.[name] = @SessionName
              AND targets.[target_name] = N'ring_buffer';
            """;

        await using SqlCommand command = new(sql, connection);
        command.Parameters.Add("@SessionName", SqlDbType.NVarChar, 128).Value = sessionName;
        object? scalar = await command.ExecuteScalarAsync(ct);
        return scalar is null or DBNull ? 0 : Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    private static async Task RunExpectedUserErrorAsync(Options options, CancellationToken ct)
    {
        await using SqlConnection connection = await OpenConnectionAsync(options, options.Database, $"{ProgramPrefix}.Error", ct);
        await using SqlCommand command = new("[chaos].[usp_ThrowUserError]", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@ErrorNumber", SqlDbType.Int).Value = 50101;
        command.Parameters.Add("@Message", SqlDbType.NVarChar, 2048).Value = "Expected server chaos harness error";

        try
        {
            await command.ExecuteNonQueryAsync(ct);
            throw new InvalidOperationException("Expected chaos user error was not thrown.");
        }
        catch (SqlException ex) when (ex.Number == 50101)
        {
            Console.WriteLine("ChaosStimulusPassed=user-error ErrorNumber=50101");
        }
    }

    private static async Task RunLockTimeoutAsync(Options options, CancellationToken ct)
    {
        await using SqlConnection holder = await OpenConnectionAsync(options, options.Database, $"{ProgramPrefix}.LockHolder", ct);
        await using SqlConnection victim = await OpenConnectionAsync(options, options.Database, $"{ProgramPrefix}.LockVictim", ct);

        await using SqlCommand hold = new(
            "BEGIN TRANSACTION; UPDATE [chaos].[LockA] SET [Value] = [Value] + 1 WHERE [Id] = 1;",
            holder)
        {
            CommandTimeout = options.CommandTimeoutSeconds
        };
        await hold.ExecuteNonQueryAsync(ct);

        try
        {
            await using SqlCommand victimCommand = new("[chaos].[usp_LockTimeoutVictim]", victim)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 10
            };
            victimCommand.Parameters.Add("@LockTimeoutMilliseconds", SqlDbType.Int).Value = 100;
            await victimCommand.ExecuteNonQueryAsync(ct);
            throw new InvalidOperationException("Expected lock timeout did not occur.");
        }
        catch (SqlException ex) when (ex.Number == 1222)
        {
            Console.WriteLine("ChaosStimulusPassed=lock-timeout ErrorNumber=1222");
        }
        finally
        {
            await RollbackIfNeededAsync(holder, ct);
        }
    }

    private static async Task RunDeadlockAsync(Options options, CancellationToken ct)
    {
        Task<SqlOutcome> left = ExecuteCommandOutcomeAsync(options, options.Database, $"{ProgramPrefix}.DeadlockLeft", "EXEC [chaos].[usp_Deadlock_Left];", ct);
        Task<SqlOutcome> right = ExecuteCommandOutcomeAsync(options, options.Database, $"{ProgramPrefix}.DeadlockRight", "EXEC [chaos].[usp_Deadlock_Right];", ct);

        SqlOutcome[] outcomes = await Task.WhenAll(left, right);
        int deadlockVictims = outcomes.Count(static outcome => outcome.Number == 1205);
        int successes = outcomes.Count(static outcome => outcome.Number == 0);
        if (deadlockVictims != 1 || successes != 1)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Expected one deadlock victim and one success. Victims={deadlockVictims}, Successes={successes}."));

        Console.WriteLine("ChaosStimulusPassed=deadlock ErrorNumber=1205");
    }

    private static async Task RunKillProbeAsync(Options options, CancellationToken ct)
    {
        string victimProgram = string.Create(CultureInfo.InvariantCulture, $"{ProgramPrefix}.Victim.{Environment.ProcessId}.{Guid.NewGuid():N}");
        await using SqlConnection victimConnection = await OpenConnectionAsync(options, options.Database, victimProgram, ct);
        int spid = await ReadCurrentSpidAsync(victimConnection, ct);
        await EnsureVictimSessionMatchesAsync(options, spid, victimProgram, ct);

        Task<SqlOutcome> victimTask = ExecuteOnExistingConnectionOutcomeAsync(
            victimConnection,
            "WAITFOR DELAY '00:00:30';",
            options.CommandTimeoutSeconds,
            CancellationToken.None);

        await Task.Delay(500, ct);

        await using SqlConnection killerConnection = await OpenConnectionAsync(options, options.Database, $"{ProgramPrefix}.Killer", ct);
        await using SqlCommand kill = new("[chaos].[usp_KillSession]", killerConnection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = options.CommandTimeoutSeconds
        };
        kill.Parameters.Add("@TargetSpid", SqlDbType.Int).Value = spid;
        kill.Parameters.Add("@Confirm", SqlDbType.NVarChar, 64).Value = "KILL_LIBDB_SESSION";
        await kill.ExecuteNonQueryAsync(ct);

        Task completed = await Task.WhenAny(victimTask, Task.Delay(TimeSpan.FromSeconds(15), ct));
        if (!ReferenceEquals(completed, victimTask))
            throw new InvalidOperationException("Victim session did not terminate after KILL.");

        SqlOutcome outcome = await victimTask;
        if (outcome.Number == 0)
            throw new InvalidOperationException("Victim command completed normally after KILL.");

        if (await SessionExistsAsync(options, spid, ct))
            throw new InvalidOperationException("Victim session still exists after KILL.");

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"KillProbePassed=spid:{spid}"));
    }

    private static async Task<SqlOutcome> ExecuteCommandOutcomeAsync(Options options, string database, string applicationName, string sql, CancellationToken ct)
    {
        await using SqlConnection connection = await OpenConnectionAsync(options, database, applicationName, ct);
        return await ExecuteOnExistingConnectionOutcomeAsync(connection, sql, options.CommandTimeoutSeconds, ct);
    }

    private static async Task<SqlOutcome> ExecuteOnExistingConnectionOutcomeAsync(SqlConnection connection, string sql, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            await using SqlCommand command = new(sql, connection)
            {
                CommandTimeout = timeoutSeconds
            };
            await command.ExecuteNonQueryAsync(ct);
            return new SqlOutcome(0);
        }
        catch (SqlException ex)
        {
            return new SqlOutcome(ex.Number);
        }
        catch (InvalidOperationException)
        {
            return new SqlOutcome(-1);
        }
    }

    private static async Task<int> ReadCurrentSpidAsync(SqlConnection connection, CancellationToken ct)
    {
        await using SqlCommand command = new("SELECT @@SPID;", connection);
        object? scalar = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    private static async Task EnsureVictimSessionMatchesAsync(Options options, int spid, string victimProgram, CancellationToken ct)
    {
        await using SqlConnection connection = await OpenConnectionAsync(options, "master", $"{ProgramPrefix}.SessionGuard", ct);
        const string sql = """
            SELECT COUNT(*)
            FROM sys.dm_exec_sessions
            WHERE [session_id] = @Spid
              AND [program_name] = @ProgramName
              AND [is_user_process] = 1
              AND [session_id] <> @@SPID
              AND [session_id] > 50;
            """;

        await using SqlCommand command = new(sql, connection);
        command.Parameters.Add("@Spid", SqlDbType.Int).Value = spid;
        command.Parameters.Add("@ProgramName", SqlDbType.NVarChar, 128).Value = victimProgram;
        int count = Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (count != 1)
            throw new InvalidOperationException("Refusing to KILL because the victim session guard did not match exactly one harness-owned user session.");
    }

    private static async Task<bool> SessionExistsAsync(Options options, int spid, CancellationToken ct)
    {
        await using SqlConnection connection = await OpenConnectionAsync(options, "master", $"{ProgramPrefix}.SessionCheck", ct);
        await using SqlCommand command = new("SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE [session_id] = @Spid;", connection);
        command.Parameters.Add("@Spid", SqlDbType.Int).Value = spid;
        int count = Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static async Task RollbackIfNeededAsync(SqlConnection connection, CancellationToken ct)
    {
        await using SqlCommand rollback = new("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;", connection)
        {
            CommandTimeout = 10
        };
        await rollback.ExecuteNonQueryAsync(ct);
    }

    private static async Task<SqlConnection> OpenConnectionAsync(Options options, string database, string applicationName, CancellationToken ct)
    {
        SqlConnectionStringBuilder builder = options.CreateConnectionStringBuilder();
        builder.InitialCatalog = database;
        builder.ApplicationName = applicationName;
        SqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Lib.Db.ChaosHarness

            Commands:
              setup      Create and start the server-level Extended Events observer.
              run        Run opt-in chaos stimuli. KILL is skipped unless --allow-kill is supplied.
              verify     Verify the observer exists, is running, and has a ring_buffer target.
              teardown   Stop and drop the server-level observer.
              all        setup + run + verify. Teardown remains a separate command.

            Required:
              --enable-server-chaos

            Connection:
              Use LIBDB_CHAOS_CONNECTION, or set LIBDB_CHAOS_PASSWORD and optional --server/--user.

            Examples:
              dotnet run --project Verification/projects/Lib.Db.ChaosHarness -- setup --enable-server-chaos
              dotnet run --project Verification/projects/Lib.Db.ChaosHarness -- run --enable-server-chaos --allow-kill
              dotnet run --project Verification/projects/Lib.Db.ChaosHarness -- teardown --enable-server-chaos
            """);
    }

    [GeneratedRegex(@"(?im)^\s*GO\s*(?:--.*)?$")]
    private static partial Regex GoSplitter();

    private readonly record struct SqlOutcome(int Number);

    private sealed class Options
    {
        private readonly string? _connectionString;

        private Options(string command, string? connectionString)
        {
            Command = command;
            _connectionString = connectionString;
        }

        public string Command { get; }

        public bool ShowHelp { get; private init; }

        public bool EnableServerChaos { get; private set; }

        public bool AllowKill { get; private set; }

        public string Server { get; private set; } = "127.0.0.1";

        public string User { get; private set; } = "SA";

        public string Database { get; private set; } = DefaultDatabase;

        public string SessionName { get; private set; } = DefaultSessionName;

        public string SqlRoot { get; private set; } = FindDefaultSqlRoot();

        public int CommandTimeoutSeconds { get; private set; } = 60;

        public static Options Parse(string[] args)
        {
            if (args.Length == 0)
                return new Options("help", null) { ShowHelp = true };

            string command = args[0].StartsWith("--", StringComparison.Ordinal) ? "help" : args[0].ToLowerInvariant();
            Options options = new(command, Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable));

            int index = args[0].StartsWith("--", StringComparison.Ordinal) ? 0 : 1;
            while (index < args.Length)
            {
                string arg = args[index];
                switch (arg)
                {
                    case "--help":
                    case "-h":
                        return options.WithShowHelp();
                    case "--enable-server-chaos":
                        options.EnableServerChaos = true;
                        index++;
                        break;
                    case "--allow-kill":
                        options.AllowKill = true;
                        index++;
                        break;
                    case "--server":
                        options.Server = ReadValue(args, ref index, arg);
                        break;
                    case "--user":
                        options.User = ReadValue(args, ref index, arg);
                        break;
                    case "--database":
                        options.Database = ReadValue(args, ref index, arg);
                        break;
                    case "--session-name":
                        options.SessionName = ReadValue(args, ref index, arg);
                        break;
                    case "--sql-root":
                        options.SqlRoot = ReadValue(args, ref index, arg);
                        break;
                    case "--connection-string":
                        options = options.WithConnectionString(ReadValue(args, ref index, arg));
                        break;
                    case "--command-timeout":
                        string value = ReadValue(args, ref index, arg);
                        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int timeout) || timeout < 5)
                            throw new ArgumentException("--command-timeout must be an integer greater than or equal to 5.");
                        options.CommandTimeoutSeconds = timeout;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {arg}");
                }
            }

            if (options.Command == "help")
                return options.WithShowHelp();

            if (!Directory.Exists(options.SqlRoot))
                throw new ArgumentException($"SQL root was not found: {options.SqlRoot}");

            return options;

        }

        public SqlConnectionStringBuilder CreateConnectionStringBuilder()
        {
            if (!string.IsNullOrWhiteSpace(_connectionString))
                return new SqlConnectionStringBuilder(_connectionString)
                {
                    Encrypt = false,
                    TrustServerCertificate = true
                };

            string? password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException($"{ConnectionEnvironmentVariable} or {PasswordEnvironmentVariable} must be set.");

            return new SqlConnectionStringBuilder
            {
                DataSource = Server,
                UserID = User,
                Password = password,
                Encrypt = false,
                TrustServerCertificate = true,
                ConnectTimeout = 15
            };
        }

        private Options WithConnectionString(string connectionString)
            => new(Command, connectionString)
            {
                AllowKill = AllowKill,
                CommandTimeoutSeconds = CommandTimeoutSeconds,
                Database = Database,
                EnableServerChaos = EnableServerChaos,
                Server = Server,
                SessionName = SessionName,
                ShowHelp = ShowHelp,
                SqlRoot = SqlRoot,
                User = User
            };

        private Options WithShowHelp()
            => new(Command, _connectionString)
            {
                AllowKill = AllowKill,
                CommandTimeoutSeconds = CommandTimeoutSeconds,
                Database = Database,
                EnableServerChaos = EnableServerChaos,
                Server = Server,
                SessionName = SessionName,
                ShowHelp = true,
                SqlRoot = SqlRoot,
                User = User
            };

        private static string ReadValue(IReadOnlyList<string> args, ref int index, string optionName)
        {
            if (index + 1 >= args.Count)
                throw new ArgumentException($"{optionName} requires a value.");

            string value = args[index + 1];
            index += 2;
            return value;
        }

        private static string FindDefaultSqlRoot()
        {
            DirectoryInfo? current = new(Directory.GetCurrentDirectory());
            while (current is not null)
            {
                string candidate = Path.Combine(current.FullName, "Tests", "Lib.Db.IntegrationTests", "sql");
                if (Directory.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "Tests", "Lib.Db.IntegrationTests", "sql");
        }
    }
}
