namespace Lib.Db.Tools;

public sealed class SystemToolConsole : IToolConsole
{
    public void WriteLine(string message) => Console.Out.WriteLine(message);

    public void WriteError(string message) => Console.Error.WriteLine(message);
}
